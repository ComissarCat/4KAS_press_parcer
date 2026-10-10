using System.Globalization;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using Press.Web.Domain;
using Press.Web.Services;

namespace Press.Web.Collectors;

/// <summary>
/// Раздел «Пресс-служба» (modules.php?name=press_dep) сайтов судов на платформе sudrf.ru.
/// Список: дата + ссылка вида ...&amp;op=1&amp;did=N; архив по месяцам: op=12&amp;arc_list=YYYY-MM.
/// </summary>
public partial class SudrfPressCollector(HtmlFetcher fetcher, ILogger<SudrfPressCollector> log) : ICollector
{
    public CollectorKind Kind => CollectorKind.SudrfPress;

    public sealed record ListEntry(string Url, string Title, DateTime? Date);

    public sealed record Detail(string? Title, string Text, DateTime? PublishedAt, IReadOnlyList<string> OutLinks,
        bool IsRepost, string? RepostSource);

    public async Task<IReadOnlyList<CollectedItem>> CollectAsync(CollectRequest request, CancellationToken ct)
    {
        var baseUrl = (request.Resource.CollectorTarget ?? request.Resource.HomeUrl
            ?? throw new InvalidOperationException("Не задан адрес сайта суда")).TrimEnd('/');
        if (!baseUrl.Contains("://")) baseUrl = "https://" + baseUrl;

        var listUrls = new List<string> { $"{baseUrl}/modules.php?name=press_dep" };
        if (request.Since is { } since)
        {
            var month = new DateTime(since.ToMsk().Year, since.ToMsk().Month, 1);
            for (var m = month; m <= Msk.Now; m = m.AddMonths(1))
                listUrls.Add($"{baseUrl}/modules.php?name=press_dep&op=12&arc_list={m:yyyy-MM}");
        }

        var entries = new Dictionary<string, ListEntry>();
        foreach (var url in listUrls)
        {
            var doc = await fetcher.GetDocumentAsync(url, ct);
            foreach (var e in ParseList(doc))
                entries.TryAdd(UrlKey.Create(e.Url), e);
        }

        var result = new List<CollectedItem>();
        foreach (var (key, entry) in entries.OrderBy(e => e.Value.Date))
        {
            if (request.KnownKeys.Contains(key)) continue;
            if (request.Since is { } s && entry.Date < s.ToMsk().Date) continue;
            try
            {
                var doc = await fetcher.GetDocumentAsync(entry.Url, ct);
                var d = ParseDetail(doc, baseUrl);
                result.Add(new CollectedItem
                {
                    Url = entry.Url,
                    Title = d.Title ?? entry.Title,
                    Text = d.Text,
                    PublishedAt = d.PublishedAt ?? (entry.Date is { } dt ? Msk.ToUtcFromMsk(dt) : null),
                    OutLinks = d.OutLinks,
                    IsRepost = d.IsRepost,
                    RepostSource = d.RepostSource,
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Не удалось загрузить {Url}", entry.Url);
            }
            await Task.Delay(300, ct); // не нагружаем сайт суда
        }
        return result;
    }

    public static IReadOnlyList<ListEntry> ParseList(IHtmlDocument doc)
    {
        var list = new List<ListEntry>();
        foreach (var a in doc.QuerySelectorAll("a[href*='name=press_dep'][href*='did=']").OfType<IHtmlAnchorElement>())
        {
            var title = CollectorText.Clean(a.TextContent);
            if (title.Length == 0) continue;
            DateTime? date = null;
            var dateCell = a.Closest("tr")?.QuerySelector("td")?.TextContent.Trim();
            if (dateCell != null && DateTime.TryParseExact(dateCell, "dd.MM.yyyy", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out var d))
                date = d;
            list.Add(new ListEntry(a.Href, title, date));
        }
        return list;
    }

    public static Detail ParseDetail(IHtmlDocument doc, string ownBaseUrl)
    {
        var title = doc.QuerySelector("#tdNewsDetailedTitle, .printVersionTitle")?.TextContent;
        var body = doc.QuerySelector(".printVersionBody");
        var text = body == null ? "" : ExtractText(body);

        DateTime? published = null;
        var info = doc.QuerySelector("[id^='divNewsPublishInfo']")?.TextContent ?? "";
        var m = PublishedRegex().Match(info);
        if (m.Success && DateTime.TryParseExact(m.Groups[1].Value, "dd.MM.yyyy HH:mm", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out var p))
            published = Msk.ToUtcFromMsk(p);

        var links = body?.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>()
            .Select(a => a.Href).Where(h => h.StartsWith("http")).Distinct().ToList() ?? [];

        // «Источник: <a href="https://www.vsrf.ru/...">Верховный Суд РФ</a>» — перепечатка
        var ownHost = UrlKey.Host(ownBaseUrl);
        string? repostSource = null;
        var src = SourceRegex().Match(text);
        if (src.Success)
        {
            var srcLink = body!.QuerySelectorAll("a[href]").OfType<IHtmlAnchorElement>()
                .LastOrDefault(a => UrlKey.Host(a.Href) is { } h && h != ownHost);
            repostSource = srcLink?.Href ?? src.Groups[1].Value.Trim();
        }

        return new Detail(title == null ? null : CollectorText.Clean(title), text, published, links,
            repostSource != null, repostSource);
    }

    private static string ExtractText(IElement body)
    {
        foreach (var br in body.QuerySelectorAll("br").ToList())
            br.Replace(body.Owner!.CreateTextNode("\n"));
        var blocks = body.QuerySelectorAll("p, li, h1, h2, h3, h4");
        var text = blocks.Length > 0
            ? string.Join("\n\n", blocks.Where(b => b.QuerySelector("p, li") == null).Select(b => b.TextContent))
            : body.TextContent;
        return CollectorText.Clean(text);
    }

    [GeneratedRegex(@"опубликовано\s+(\d{2}\.\d{2}\.\d{4}\s+\d{2}:\d{2})", RegexOptions.IgnoreCase)]
    private static partial Regex PublishedRegex();

    [GeneratedRegex(@"Источник\s*:\s*(.+)$", RegexOptions.IgnoreCase | RegexOptions.Multiline)]
    private static partial Regex SourceRegex();
}
