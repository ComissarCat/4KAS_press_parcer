using System.Text.Json;
using Microsoft.Extensions.Options;
using Press.Web.Domain;
using Press.Web.Services;

namespace Press.Web.Collectors;

public class VkOptions
{
    /// <summary>
    /// Сервисный ключ доступа приложения ВК (бесплатно: id.vk.com/about/business/go → создать приложение →
    /// «Сервисный ключ доступа»). Достаточно для чтения стен публичных сообществ.
    /// </summary>
    public string? ServiceToken { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.vk.com/method/";
    public string ApiVersion { get; set; } = "5.199";
}

/// <summary>Стена сообщества ВКонтакте через API wall.get.</summary>
public class VkWallCollector(IHttpClientFactory httpFactory, IOptionsMonitor<VkOptions> options) : ICollector
{
    private const int PageSize = 100;
    private const int MaxPages = 100;

    public CollectorKind Kind => CollectorKind.VkWall;

    public async Task<IReadOnlyList<CollectedItem>> CollectAsync(CollectRequest request, CancellationToken ct)
    {
        var opt = options.CurrentValue;
        if (string.IsNullOrWhiteSpace(opt.ServiceToken))
            throw new InvalidOperationException(
                "Не задан сервисный ключ ВКонтакте (VK_SERVICE_TOKEN в .env или Vk:ServiceToken в настройках)");
        var owner = OwnerParam(request.Resource.CollectorTarget ?? request.Resource.HomeUrl)
            ?? throw new InvalidOperationException("Не задано сообщество ВКонтакте");

        var http = httpFactory.CreateClient(HtmlFetcher.HttpClientName);
        var result = new List<CollectedItem>();
        for (var page = 0; page < MaxPages; page++)
        {
            var count = request.Since == null ? 20 : PageSize;
            var url = $"{opt.ApiBaseUrl}wall.get?{owner}&count={count}&offset={page * count}" +
                      $"&access_token={Uri.EscapeDataString(opt.ServiceToken)}&v={opt.ApiVersion}";
            var json = await http.GetStringAsync(url, ct);
            var items = ParseResponse(json, out var total);
            result.AddRange(items);

            if (request.Since is not { } since) break;
            // закреплённый пост может быть старым, поэтому смотрим на все посты страницы
            if (items.Count == 0 || items.All(i => i.PublishedAt < since) || (page + 1) * count >= total) break;
            await Task.Delay(400, ct); // лимит API — 3 запроса в секунду
        }

        return result
            .Where(i => request.Since is not { } s || i.PublishedAt >= s)
            .Where(i => !request.KnownKeys.Contains(UrlKey.Create(i.Url)))
            .OrderBy(i => i.PublishedAt)
            .ToList();
    }

    /// <summary>«4kassud», «vk.ru/4kassud», «-217666653», «club217666653», «public217666653» → параметр запроса.</summary>
    public static string? OwnerParam(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var t = target.Trim();
        if (t.Contains("vk.com/") || t.Contains("vk.ru/"))
            t = UrlKey.TryCreate(t)?.Split('/').Skip(1).FirstOrDefault() ?? "";
        foreach (var prefix in new[] { "club", "public" })
            if (t.StartsWith(prefix) && long.TryParse(t[prefix.Length..], out var cid))
                return $"owner_id=-{cid}";
        if (long.TryParse(t, out var id)) return $"owner_id=-{Math.Abs(id)}";
        return t.Length > 0 ? $"domain={Uri.EscapeDataString(t)}" : null;
    }

    public static IReadOnlyList<CollectedItem> ParseResponse(string json, out int total)
    {
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("error", out var err))
            throw new InvalidOperationException(
                $"Ошибка API ВКонтакте {err.GetProperty("error_code").GetInt32()}: {err.GetProperty("error_msg").GetString()}");

        var resp = doc.RootElement.GetProperty("response");
        total = resp.TryGetProperty("count", out var c) ? c.GetInt32() : 0;
        var list = new List<CollectedItem>();
        foreach (var post in resp.GetProperty("items").EnumerateArray())
        {
            var ownerId = post.GetProperty("owner_id").GetInt64();
            var id = post.GetProperty("id").GetInt64();
            var text = post.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
            var links = UrlKey.FindUrls(text).ToList();
            CollectLinks(post, links);

            var isRepost = false;
            string? repostSource = null;
            if (post.TryGetProperty("copy_history", out var hist) && hist.GetArrayLength() > 0)
            {
                var orig = hist[0];
                isRepost = true;
                repostSource = $"https://vk.com/wall{orig.GetProperty("owner_id").GetInt64()}_{orig.GetProperty("id").GetInt64()}";
                var origText = orig.TryGetProperty("text", out var ot) ? ot.GetString() ?? "" : "";
                if (text.Length == 0) text = origText;
                links.AddRange(UrlKey.FindUrls(origText));
                CollectLinks(orig, links);
            }

            text = CollectorText.Clean(text);
            list.Add(new CollectedItem
            {
                Url = $"https://vk.com/wall{ownerId}_{id}",
                Title = CollectorText.FirstLine(text) ?? "Публикация без текста",
                Text = text,
                PublishedAt = DateTimeOffset.FromUnixTimeSeconds(post.GetProperty("date").GetInt64()).UtcDateTime,
                OutLinks = links.Distinct().ToList(),
                IsRepost = isRepost,
                RepostSource = repostSource,
            });
        }
        return list;
    }

    private static void CollectLinks(JsonElement post, List<string> links)
    {
        if (!post.TryGetProperty("attachments", out var atts)) return;
        foreach (var a in atts.EnumerateArray())
            if (a.TryGetProperty("link", out var link) && link.TryGetProperty("url", out var u) && u.GetString() is { } s)
                links.Add(s);
    }
}
