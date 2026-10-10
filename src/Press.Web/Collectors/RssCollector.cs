using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Press.Web.Domain;
using Press.Web.Services;

namespace Press.Web.Collectors;

/// <summary>RSS 2.0 / Atom лента. В лентах обычно только анонс — полный текст загружается отдельно.</summary>
public partial class RssCollector(IHttpClientFactory httpFactory) : ICollector
{
    public CollectorKind Kind => CollectorKind.Rss;

    public async Task<IReadOnlyList<CollectedItem>> CollectAsync(CollectRequest request, CancellationToken ct)
    {
        var url = request.Resource.CollectorTarget ?? throw new InvalidOperationException("Не задан адрес RSS-ленты");
        var http = httpFactory.CreateClient(HtmlFetcher.HttpClientName);
        await using var stream = await http.GetStreamAsync(url, ct);
        var items = Parse(stream);
        return items
            .Where(i => !request.KnownKeys.Contains(UrlKey.Create(i.Url)))
            .Where(i => request.Since is not { } s || i.PublishedAt == null || i.PublishedAt >= s)
            .ToList();
    }

    public static IReadOnlyList<CollectedItem> Parse(Stream stream)
    {
        HtmlFetcher.EnsureEncodings(); // ленты бывают в windows-1251
        using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        var doc = XDocument.Load(reader);
        var result = new List<CollectedItem>();

        foreach (var el in doc.Descendants().Where(e => e.Name.LocalName is "item" or "entry"))
        {
            string? Child(params string[] names) =>
                el.Elements().FirstOrDefault(c => names.Contains(c.Name.LocalName) && !string.IsNullOrWhiteSpace(c.Value))?.Value.Trim();

            var link = Child("link");
            if (string.IsNullOrEmpty(link))
            {
                // Atom: <link rel="alternate" href="..."/>
                link = el.Elements().Where(c => c.Name.LocalName == "link")
                    .OrderBy(c => (string?)c.Attribute("rel") is null or "alternate" ? 0 : 1)
                    .Select(c => (string?)c.Attribute("href")).FirstOrDefault(h => !string.IsNullOrEmpty(h));
            }
            if (string.IsNullOrEmpty(link) || UrlKey.TryCreate(link) == null) continue;

            var summary = HtmlToText(Child("full-text", "encoded", "description", "summary", "content"));
            result.Add(new CollectedItem
            {
                Url = link,
                Title = CollectorText.Clean(HtmlToText(Child("title"))),
                Text = summary,
                PublishedAt = ParseDate(Child("pubDate", "published", "updated", "date")),
                OutLinks = [],
                IsSummaryOnly = true,
            });
        }
        return result;
    }

    private static string HtmlToText(string? html)
    {
        if (string.IsNullOrWhiteSpace(html)) return "";
        if (!html.Contains('<') && !html.Contains('&')) return CollectorText.Clean(html);
        var doc = HtmlFetcher.Parse("<body>" + html + "</body>");
        return CollectorText.Clean(doc.Body?.TextContent);
    }

    public static DateTime? ParseDate(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        // «+0300» → «+03:00», чтобы разобрать RFC 822 стандартными средствами
        var normalized = TzRegex().Replace(s.Trim(), "$1:$2");
        string[] formats =
        [
            "ddd, d MMM yyyy HH:mm:ss zzz", "ddd, d MMM yy HH:mm:ss zzz", "d MMM yyyy HH:mm:ss zzz",
            "ddd, d MMM yyyy HH:mm zzz", "ddd, d MMM yyyy HH:mm:ss 'GMT'", "ddd, d MMM yyyy HH:mm:ss 'UTC'",
        ];
        if (DateTimeOffset.TryParseExact(normalized, formats, CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal, out var dto) ||
            DateTimeOffset.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out dto))
            return dto.UtcDateTime;
        return null;
    }

    [GeneratedRegex(@"([+-]\d{2})(\d{2})$")]
    private static partial Regex TzRegex();
}
