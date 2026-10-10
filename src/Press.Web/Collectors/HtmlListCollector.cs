using AngleSharp.Html.Dom;
using Press.Web.Domain;
using Press.Web.Services;

namespace Press.Web.Collectors;

/// <summary>
/// HTML-страница со списком публикаций для сайтов без RSS. Ссылки выбираются CSS-селектором
/// (например, «a[href*='/news/']»); берутся только ссылки того же сайта на конкретные страницы.
/// Дата и текст появляются после загрузки самой публикации.
/// </summary>
public class HtmlListCollector(HtmlFetcher fetcher) : ICollector
{
    private const int MaxLinks = 100;

    public CollectorKind Kind => CollectorKind.HtmlList;

    public async Task<IReadOnlyList<CollectedItem>> CollectAsync(CollectRequest request, CancellationToken ct)
    {
        var url = request.Resource.CollectorTarget ?? throw new InvalidOperationException("Не задан адрес страницы со списком");
        var doc = await fetcher.GetDocumentAsync(url, ct);
        return ParseList(doc, url, request.Resource.CollectorSelector)
            .Where(i => !request.KnownKeys.Contains(UrlKey.Create(i.Url)))
            .ToList();
    }

    public static IReadOnlyList<CollectedItem> ParseList(IHtmlDocument doc, string pageUrl, string? selector)
    {
        var host = UrlKey.Host(pageUrl);
        var pageKey = UrlKey.TryCreate(pageUrl);
        var byKey = new Dictionary<string, CollectedItem>();
        foreach (var a in doc.QuerySelectorAll(string.IsNullOrWhiteSpace(selector) ? "a[href]" : selector)
                     .OfType<IHtmlAnchorElement>())
        {
            var key = UrlKey.TryCreate(a.Href);
            if (key == null || key == pageKey || UrlKey.Host(a.Href) != host || !UrlKey.IsSpecific(key)) continue;
            var title = CollectorText.Clean(a.TextContent);
            // одна и та же публикация часто встречается дважды: картинка и заголовок — берём самый длинный текст
            if (byKey.TryGetValue(key, out var existing) && (existing.Title?.Length ?? 0) >= title.Length) continue;
            byKey[key] = new CollectedItem { Url = a.Href, Title = title.Length > 0 ? title : null, IsSummaryOnly = true };
            if (byKey.Count >= MaxLinks) break;
        }
        return byKey.Values.ToList();
    }
}
