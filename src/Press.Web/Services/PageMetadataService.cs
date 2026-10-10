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
        catch (Exception ex) when (!ct.IsCancellationRequested)
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

        var articleBody = doc.QuerySelector("[itemprop='articleBody']");
        var paragraphs = articleBody != null
            ? articleBody.QuerySelectorAll("*").Where(IsParagraph).ToList()
            : MainParagraphs(doc);
        var text = CollectorText.Clean(string.Join("\n\n", paragraphs.Select(p => p.TextContent.Trim())));
        // некоторые сайты (например, «Коммерсантъ») кладут весь текст статьи в описание страницы
        var description = CollectorText.Clean(Meta("meta[property='og:description']") ?? Meta("meta[name='description']"));
        if (description.Length > text.Length) text = description;
        if (text.Length > MaxTextLength) text = text[..MaxTextLength];

        var links = paragraphs.SelectMany(p => p.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>())
            .Select(a => a.Href).Where(h => h.StartsWith("http")).Distinct().ToList();

        return new PageMetadata(title == null ? null : CollectorText.Clean(title), published, text.Length > 0 ? text : null, links);
    }

    /// <summary>
    /// Абзац связного текста: &lt;p&gt; или блок без вложенных блоков (так верстают некоторые сайты).
    /// Меню, ленты «Читайте также» и подвал отсекаются по доле текста в ссылках, а блоки — ещё и по отсутствию
    /// предложений.
    /// </summary>
    private static bool IsParagraph(IElement el)
    {
        bool isDiv;
        if (el.LocalName == "p") isDiv = false;
        else if (el.LocalName == "div" && el.Closest("p") == null && !el.Children.Any(c => c.LocalName is "div" or "p")) isDiv = true;
        else return false;

        // абзацы внутри ссылок (ленты новостей), меню, шапки и подвала — не текст статьи
        if (el.Closest("a, nav, header, footer, aside") != null) return false;
        var text = el.TextContent.Trim();
        if (text.Length == 0 || isDiv && (text.Length < 60 || !text.Contains(". ") && !text.EndsWith('.'))) return false;
        var linkText = el.QuerySelectorAll("a").Sum(a => a.TextContent.Trim().Length);
        return linkText < text.Length * 0.3;
    }

    /// <summary>
    /// Абзацы основного текста статьи. Абзацы группируются по «адресу» в разметке — тегу и классам
    /// родителя и «деда»: абзацы статьи свёрстаны одинаково (даже если каждый обёрнут в свои блоки, как
    /// у «Коммерсанта»), а меню, ленты и подвал — иначе. Побеждает группа с наибольшим объёмом текста.
    /// </summary>
    private static List<IElement> MainParagraphs(IHtmlDocument doc)
    {
        static string Node(IElement? e) =>
            e == null ? "" : e.LocalName + "." + string.Join(".", e.ClassList.OrderBy(c => c, StringComparer.Ordinal));

        var all = doc.Body?.QuerySelectorAll("*").Where(IsParagraph).ToList() ?? [];
        var best = all
            .GroupBy(p => p.LocalName + "<" + Node(p.ParentElement) + "<" + Node(p.ParentElement?.ParentElement))
            .Select(g => (Paragraphs: g.ToList(), Length: g.Sum(p => p.TextContent.Trim().Length)))
            .OrderByDescending(g => g.Length)
            .FirstOrDefault();
        // совсем короткая страница — берём все абзацы
        return best.Length >= 200 ? best.Paragraphs : all;
    }

    [GeneratedRegex(@"""datePublished""\s*:\s*""([^""]+)""")]
    private static partial Regex JsonLdDateRegex();
}
