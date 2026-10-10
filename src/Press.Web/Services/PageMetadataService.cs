using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Press.Web.Collectors;

namespace Press.Web.Services;

public sealed record PageMetadata(string? Title, DateTime? PublishedAt, string? Text, IReadOnlyList<string>? OutLinks = null);

/// <summary>Достаёт заголовок, дату и текст публикации по произвольной ссылке (для ручного добавления).</summary>
public partial class PageMetadataService(HtmlFetcher fetcher, ILogger<PageMetadataService> log)
{
    private const int MaxTextLength = 50_000;

    public async Task<PageMetadata> FetchAsync(string url, CancellationToken ct)
    {
        try
        {
            var key = UrlKey.TryCreate(url);
            // пост Telegram: виджет ?embed=1 содержит ту же разметку, что и t.me/s/канал
            if (key != null && key.StartsWith("t.me/") && key.Split('/').Length == 3)
            {
                var doc = await fetcher.GetDocumentAsync($"https://{key}?embed=1", ct);
                var post = TelegramPublicCollector.ParsePage(doc).FirstOrDefault();
                if (post != null) return new PageMetadata(post.Item.Title, post.Item.PublishedAt, post.Item.Text, post.Item.OutLinks);
            }
            return Parse(await fetcher.GetDocumentAsync(url, ct));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogInformation(ex, "Не удалось получить данные страницы {Url}", url);
            return new PageMetadata(null, null, null);
        }
    }

    public static PageMetadata Parse(IHtmlDocument doc)
    {
        string? Meta(string selector) => doc.QuerySelector(selector)?.GetAttribute("content")?.Trim() is { Length: > 0 } v ? v : null;

        var title = Meta("meta[property='og:title']") ?? Meta("meta[name='twitter:title']")
                    ?? doc.QuerySelector("h1")?.TextContent ?? doc.Title;

        var jsonLdDate = JsonLdDateRegex().Match(doc.Source.Text).Groups[1].Value;
        var dateStr = Meta("meta[property='article:published_time']")
                      ?? Meta("meta[itemprop='datePublished']")
                      ?? doc.QuerySelector("[itemprop='datePublished']")?.GetAttribute("datetime")
                      ?? (jsonLdDate.Length > 0 ? jsonLdDate : null)
                      ?? doc.QuerySelector("article time[datetime], time[datetime]")?.GetAttribute("datetime");
        DateTime? published = null;
        if (DateTimeOffset.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dto))
            published = dto.UtcDateTime;

        var container = doc.QuerySelector("[itemprop='articleBody']") ?? MainTextBlock(doc) ?? doc.Body;
        var paragraphs = container?.QuerySelectorAll("p").Select(p => p.TextContent.Trim()).Where(t => t.Length > 0).ToList();
        var text = paragraphs is { Count: > 0 }
            ? string.Join("\n\n", paragraphs)
            : Meta("meta[property='og:description']") ?? Meta("meta[name='description']");
        text = CollectorText.Clean(text);
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];

        var links = container?.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>()
            .Select(a => a.Href).Where(h => h.StartsWith("http")).Distinct().ToList() ?? [];

        return new PageMetadata(title == null ? null : CollectorText.Clean(title), published, text.Length > 0 ? text : null, links);
    }

    /// <summary>
    /// Основной текст статьи — блок, у которого больше всего текста в собственных абзацах.
    /// Так отсекаются меню, боковые ленты «Читайте также» и подвал.
    /// </summary>
    private static IElement? MainTextBlock(IHtmlDocument doc)
    {
        IElement? best = null;
        var bestLength = 0;
        foreach (var el in doc.Body?.QuerySelectorAll("*") ?? Enumerable.Empty<IElement>())
        {
            var length = el.Children.Where(c => c.LocalName == "p").Sum(p => p.TextContent.Trim().Length);
            if (length <= bestLength) continue;
            best = el;
            bestLength = length;
        }
        return bestLength >= 200 ? best : null;
    }

    [GeneratedRegex(@"""datePublished""\s*:\s*""([^""]+)""")]
    private static partial Regex JsonLdDateRegex();
}
