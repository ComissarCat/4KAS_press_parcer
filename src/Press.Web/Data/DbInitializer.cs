using Microsoft.EntityFrameworkCore;
using Press.Web.Domain;
using Press.Web.Services;

namespace Press.Web.Data;

public static class DbInitializer
{
    private static readonly string[] DefaultTypes =
    [
        "Новость", "Пресс-релиз", "Статья", "Аналитика", "Обзор практики", "Интервью",
        "Анонс", "Комментарий", "Фото", "Видео", "Инфографика",
    ];

    /// <summary>Сторонние ресурсы, которые пресс-служба просматривает (список от октября 2026 г.).</summary>
    private static IEnumerable<Resource> MonitoredDefaults()
    {
        Resource Rss(string name, string prefix, string home, string feed, int interval, bool fullText = false) => new()
        {
            Name = name, Kind = ResourceKind.Website, UrlPrefix = prefix, HomeUrl = home,
            CollectorKind = CollectorKind.Rss, CollectorTarget = feed, CollectIntervalMinutes = interval,
            FetchFullText = fullText, IsCollectorEnabled = true,
        };
        Resource Tg(string name, string channel) => new()
        {
            Name = name, Kind = ResourceKind.Telegram, UrlPrefix = $"t.me/{channel}/", HomeUrl = $"https://t.me/{channel}",
            CollectorKind = CollectorKind.TelegramPublic, CollectorTarget = channel, CollectIntervalMinutes = 30,
            IsCollectorEnabled = true,
        };

        yield return new Resource
        {
            Name = "Business FM Кубань", Kind = ResourceKind.Website, UrlPrefix = "kuban.bfm.ru", HomeUrl = "https://kuban.bfm.ru/",
            CollectorKind = CollectorKind.HtmlList, CollectorTarget = "https://kuban.bfm.ru/", CollectorSelector = "a[href*='/news/']",
            CollectIntervalMinutes = 60, FetchFullText = true, IsCollectorEnabled = true,
        };
        yield return Rss("РАПСИ", "rapsinews.ru", "https://rapsinews.ru/", "https://rapsinews.ru/export/rss2/index.xml", 30);
        // в ленте «РГ» 100 последних публикаций — это несколько часов, поэтому проверяем часто
        yield return Rss("Российская газета", "rg.ru", "https://rg.ru/", "https://rg.ru/xml/index.xml", 20);
        yield return Rss("Коммерсантъ", "kommersant.ru", "https://www.kommersant.ru/", "https://www.kommersant.ru/RSS/news.xml", 30);
        yield return Rss("Кубанские новости", "kubnews.ru", "https://kubnews.ru/all/", "https://kubnews.ru/rss/", 30, fullText: true);
        yield return Tg("Telegram: Типичный Краснодар", "typodar");
        yield return Tg("Telegram: РАПСИ", "rapsinewsru");
    }

    public static async Task InitializeAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = await scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>().CreateDbContextAsync();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var log = scope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("DbInitializer");

        await db.Database.MigrateAsync();

        if (!await db.MaterialTypes.AnyAsync())
        {
            for (var i = 0; i < DefaultTypes.Length; i++)
                db.MaterialTypes.Add(new MaterialType { Name = DefaultTypes[i], SortOrder = (i + 1) * 10 });
            await db.SaveChangesAsync();
        }

        if (!await db.Resources.AnyAsync())
        {
            var news = await db.MaterialTypes.FirstAsync(t => t.Name == "Новость");
            db.Resources.AddRange(
                new Resource
                {
                    Name = "Сайт суда (пресс-служба)", Kind = ResourceKind.Website, IsOwn = true,
                    HomeUrl = "https://4kas.sudrf.ru/modules.php?name=press_dep", UrlPrefix = "4kas.sudrf.ru",
                    CollectorKind = CollectorKind.SudrfPress, CollectorTarget = "https://4kas.sudrf.ru",
                    IsCollectorEnabled = true, CollectIntervalMinutes = 60, DefaultMaterialType = news,
                },
                new Resource
                {
                    Name = "Telegram-канал", Kind = ResourceKind.Telegram, IsOwn = true,
                    HomeUrl = "https://t.me/sud_kas_4", UrlPrefix = "t.me/sud_kas_4/",
                    CollectorKind = CollectorKind.TelegramPublic, CollectorTarget = "sud_kas_4",
                    IsCollectorEnabled = true, CollectIntervalMinutes = 30, DefaultMaterialType = news,
                },
                new Resource
                {
                    Name = "Группа ВКонтакте", Kind = ResourceKind.Vk, IsOwn = true,
                    HomeUrl = "https://vk.ru/4kassud", UrlPrefix = "vk.com/wall-217666653_",
                    CollectorKind = CollectorKind.VkWall, CollectorTarget = "-217666653",
                    IsCollectorEnabled = false, CollectIntervalMinutes = 60, DefaultMaterialType = news,
                    Notes = "Для сбора нужен сервисный ключ ВКонтакте (VK_SERVICE_TOKEN в .env или Vk:ServiceToken в настройках)",
                },
                new Resource
                {
                    Name = "Канал MAX", Kind = ResourceKind.Max, IsOwn = true,
                    HomeUrl = "https://max.ru/id2308258969_gos", UrlPrefix = "max.ru/id2308258969_gos",
                    Notes = "Веб-версия MAX не отдаёт посты без входа в аккаунт — ссылки добавляются вручную",
                });
            db.Resources.AddRange(MonitoredDefaults());
            await db.SaveChangesAsync();
        }

        if (!await db.Users.AnyAsync())
        {
            var login = config["Auth:InitialAdmin:Login"] ?? "admin";
            var password = config["Auth:InitialAdmin:Password"] ?? "admin123";
            var users = scope.ServiceProvider.GetRequiredService<UserService>();
            await users.CreateAsync(login, "Администратор", password);
            log.LogWarning("Создан пользователь «{Login}» с начальным паролем из настроек. Смените пароль на странице «Пользователи».", login);
        }
    }
}
