using System.Globalization;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Press.Web.Domain;
using Press.Web.Services;

namespace Press.Web.Collectors;

/// <summary>
/// Публичная веб-версия Telegram-канала: https://t.me/s/канал (20 последних постов,
/// более ранние — ?before=номер). Не требует аккаунта и токенов.
/// </summary>
public class TelegramPublicCollector(HtmlFetcher fetcher) : ICollector
{
    private const int MaxPages = 300;

    public CollectorKind Kind => CollectorKind.TelegramPublic;

    public async Task<IReadOnlyList<CollectedItem>> CollectAsync(CollectRequest request, CancellationToken ct)
    {
        var channel = ChannelName(request.Resource.CollectorTarget ?? request.Resource.HomeUrl)
            ?? throw new InvalidOperationException("Не задано имя Telegram-канала");

        var result = new List<CollectedItem>();
        int? before = null;
        for (var page = 0; page < MaxPages; page++)
        {
            var url = $"https://t.me/s/{channel}" + (before is { } b ? $"?before={b}" : "");
            var doc = await fetcher.GetDocumentAsync(url, ct);
            var posts = ParsePage(doc);
            if (posts.Count == 0) break;
            result.AddRange(posts.Select(p => p.Item));

            // без архивного режима хватает одной страницы
            if (request.Since is not { } since) break;
            if (posts.Min(p => p.Item.PublishedAt) is { } oldest && oldest < since) break;
            var minId = posts.Min(p => p.Id);
            if (minId <= 1 || minId == before) break;
            before = minId;
            await Task.Delay(500, ct);
        }

        return result
            .Where(i => request.Since is not { } s || i.PublishedAt >= s)
            .Where(i => !request.KnownKeys.Contains(UrlKey.Create(i.Url)))
            .OrderBy(i => i.PublishedAt)
            .ToList();
    }

    /// <summary>«sud_kas_4», «@sud_kas_4», «https://t.me/sud_kas_4», «t.me/s/sud_kas_4» → «sud_kas_4».</summary>
    public static string? ChannelName(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return null;
        var t = target.Trim().TrimStart('@');
        if (t.Contains("t.me/") || t.Contains("telegram.me/"))
        {
            var key = UrlKey.TryCreate(t);
            t = key?.Split('/').Skip(1).FirstOrDefault() ?? "";
        }
        return t.Length > 0 ? t : null;
    }

    public sealed record Post(int Id, CollectedItem Item);

    public static IReadOnlyList<Post> ParsePage(IHtmlDocument doc)
    {
        var posts = new List<Post>();
        foreach (var msg in doc.QuerySelectorAll(".tgme_widget_message[data-post]"))
        {
            var dataPost = msg.GetAttribute("data-post")!; // "канал/123"
            var slash = dataPost.LastIndexOf('/');
            if (slash < 0 || !int.TryParse(dataPost[(slash + 1)..], out var id)) continue;

            DateTime? published = null;
            var dt = msg.QuerySelector(".tgme_widget_message_date time[datetime]")?.GetAttribute("datetime");
            if (DateTimeOffset.TryParse(dt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dto))
                published = dto.UtcDateTime;

            var textEl = msg.QuerySelectorAll(".tgme_widget_message_text")
                .FirstOrDefault(e => e.Closest(".tgme_widget_message_reply") == null);
            var text = textEl == null ? "" : ExtractText(textEl);

            var links = new List<string>();
            if (textEl != null)
                links.AddRange(textEl.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>().Select(a => a.Href));
            links.AddRange(msg.QuerySelectorAll("a.tgme_widget_message_link_preview[href]")
                .OfType<IHtmlAnchorElement>().Select(a => a.Href));

            var fwd = msg.QuerySelector(".tgme_widget_message_forwarded_from_name");
            string? fwdSource = fwd == null ? null : (fwd as IHtmlAnchorElement)?.Href ?? CollectorText.Clean(fwd.TextContent);

            posts.Add(new Post(id, new CollectedItem
            {
                Url = $"https://t.me/{dataPost}",
                Title = CollectorText.FirstLine(text) ?? "Публикация без текста",
                Text = text,
                PublishedAt = published,
                OutLinks = links.Where(h => h.StartsWith("http")).Distinct().ToList(),
                IsRepost = fwd != null,
                RepostSource = fwdSource,
            }));
        }
        return posts;
    }

    private static string ExtractText(IElement el)
    {
        var clone = (IElement)el.Clone();
        foreach (var br in clone.QuerySelectorAll("br").ToList())
            br.Replace(el.Owner!.CreateTextNode("\n"));
        return CollectorText.Clean(clone.TextContent);
    }
}
