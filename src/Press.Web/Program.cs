using System.Net;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using Press.Web.Collectors;
using Press.Web.Components;
using Press.Web.Data;
using Press.Web.Services;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // при запуске как служба текущая папка — system32, поэтому явно берём папку приложения
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});
builder.Host.UseWindowsService(); // служба Windows; вне службы ничего не делает
builder.Host.UseSystemd();        // то же для systemd (Linux)

var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Не задана строка подключения ConnectionStrings:Default");
builder.Services.AddDbContextFactory<AppDbContext>(o => o.UseNpgsql(connectionString));

// Ключи шифрования cookie храним в базе — контейнер приложения не хранит состояния
builder.Services.AddDataProtection()
    .PersistKeysToDbContext<AppDbContext>()
    .SetApplicationName("Press");

// HTTP-клиент для сбора: таймаут, User-Agent и, при необходимости, прокси организации
builder.Services.AddHttpClient(HtmlFetcher.HttpClientName, c =>
    {
        c.Timeout = TimeSpan.FromSeconds(60);
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            builder.Configuration["Http:UserAgent"] ??
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0 Safari/537.36");
        c.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9");
    })
    .ConfigurePrimaryHttpMessageHandler(() =>
    {
        var handler = new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All };
        var proxy = builder.Configuration.GetSection("Http:Proxy");
        if (!string.IsNullOrWhiteSpace(proxy["Url"]))
        {
            var wp = new WebProxy(proxy["Url"]) { BypassProxyOnLocal = true };
            if (!string.IsNullOrWhiteSpace(proxy["UserName"]))
                wp.Credentials = new NetworkCredential(proxy["UserName"], proxy["Password"], proxy["Domain"]);
            else if (proxy.GetValue<bool>("UseDefaultCredentials"))
                wp.UseDefaultCredentials = true;
            handler.Proxy = wp;
            handler.UseProxy = true;
        }
        return handler;
    });

builder.Services.Configure<VkOptions>(builder.Configuration.GetSection("Vk"));
builder.Services.Configure<MonitoringOptions>(builder.Configuration.GetSection("Monitoring"));
builder.Services.AddSingleton<HtmlFetcher>();
builder.Services.AddSingleton<PageMetadataService>();
builder.Services.AddSingleton<ICollector, SudrfPressCollector>();
builder.Services.AddSingleton<ICollector, TelegramPublicCollector>();
builder.Services.AddSingleton<ICollector, VkWallCollector>();
builder.Services.AddSingleton<ICollector, RssCollector>();
builder.Services.AddSingleton<ICollector, HtmlListCollector>();
builder.Services.AddSingleton<MonitoringService>();
builder.Services.AddSingleton<SettingsService>();
builder.Services.AddSingleton<CollectionService>();
builder.Services.AddHostedService<CollectionScheduler>();
builder.Services.AddSingleton<MaterialService>();
builder.Services.AddSingleton<ReportService>();
builder.Services.AddSingleton<UserService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/account/login";
        o.ExpireTimeSpan = TimeSpan.FromDays(30);
        o.SlidingExpiration = true;
        o.Cookie.Name = "press.auth";
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest; // в локальной сети работаем по http
    });
builder.Services.AddAuthorization();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddHttpContextAccessor();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.MapPost("/account/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.LocalRedirect("/account/login");
});

app.MapGet("/reports/export", async (DateTime from, DateTime to, bool? reposts, ReportService reports, CancellationToken ct) =>
{
    var data = await reports.BuildAsync(new ReportOptions(from, to, reposts ?? false), ct);
    var bytes = ReportService.ToExcel(data);
    return Results.File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        $"Отчёт {from:yyyy-MM-dd} — {to:yyyy-MM-dd}.xlsx");
}).RequireAuthorization();

app.Run();
