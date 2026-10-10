using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Press.Web.Collectors;
using Press.Web.Data;
using Press.Web.Domain;

namespace Press.Web.Services;

public class MonitoringOptions
{
    /// <summary>С какими собственными материалами сравнивать (за сколько последних дней).</summary>
    public int LookbackDays { get; set; } = 60;
    /// <summary>
    /// Предварительный отбор по заголовку и анонсу: если такая доля «веса» анонса приходится на слова
    /// нашего материала — загружаем полный текст.
    /// </summary>
    public double PrefilterThreshold { get; set; } = 0.3;
    /// <summary>Похожесть полного текста, начиная с которой публикация предлагается как перепечатка.</summary>
    public double CandidateThreshold { get; set; } = 0.3;
    /// <summary>
    /// Для коротких публикаций (посты в Telegram): кандидат, если такая доля «веса» публикации приходится
    /// на слова нашего материала и общих слов не меньше <see cref="ShortItemMinCommon"/>.
    /// </summary>
    public double ShortItemCoverage { get; set; } = 0.5;
    public int ShortItemMinCommon { get; set; } = 8;
    /// <summary>Сколько последних дней публикаций СМИ учитывать при оценке редкости слов.</summary>
    public int BackgroundDays { get; set; } = 30;
    /// <summary>Сколько полных текстов загружать за один проход по ресурсу.</summary>
    public int MaxFetchesPerRun { get; set; } = 60;
    /// <summary>Через сколько дней удалять записи о нерелевантных публикациях.</summary>
    public int KeepIrrelevantDays { get; set; } = 90;
}

/// <summary>
/// Мониторинг сторонних ресурсов: отбирает публикации, похожие на наши материалы
/// или упоминающие суд, и ставит их в очередь на проверку.
/// </summary>
public class MonitoringService(
    PageMetadataService metadata,
    SettingsService settings,
    IOptionsMonitor<MonitoringOptions> options,
    ILogger<MonitoringService> log)
{
    public async Task<CollectRunResult> ProcessAsync(AppDbContext db, Resource resource, ICollector collector,
        DateTime? since, CancellationToken ct)
    {
        var opt = options.CurrentValue;
        var known = (await db.MonitoredItems.Select(i => i.UrlKey).ToListAsync(ct))
            .Concat(await db.MaterialLinks.Select(l => l.UrlKey).ToListAsync(ct))
            .ToHashSet();
        var items = await collector.CollectAsync(new CollectRequest(resource, known, since), ct);

        // свежие публикации тоже участвуют в оценке редкости слов — важно при первом запуске, когда фона ещё нет
        var index = await BuildIndexAsync(db, opt, items.Select(i => i.Title + "\n" + i.Text), ct);
        var keywords = new KeywordMatcher(await settings.GetKeywordsAsync(ct));
        var ownPrefixes = await OwnPrefixesAsync(db, ct);

        int candidates = 0, mentions = 0, fetches = 0;
        foreach (var item in items)
        {
            var key = UrlKey.TryCreate(item.Url);
            if (key == null || known.Contains(key)) continue;
            known.Add(key);

            var mi = new MonitoredItem
            {
                Resource = resource,
                Url = item.Url,
                UrlKey = key,
                Title = item.Title,
                Summary = item.IsSummaryOnly ? item.Text : null,
                Text = item.IsSummaryOnly ? null : item.Text,
                PublishedAt = item.PublishedAt,
                FoundAt = DateTime.UtcNow,
            };
            var links = item.OutLinks.Append(item.RepostSource).OfType<string>().ToList();

            if (item.IsSummaryOnly && fetches < opt.MaxFetchesPerRun)
            {
                var pre = index.Best(item.Title, item.Text, item.PublishedAt);
                var worthFetching = resource.FetchFullText
                                    || keywords.FindIn(item.Title + "\n" + item.Text) != null
                                    || pre?.ItemCoverage >= opt.PrefilterThreshold;
                if (worthFetching)
                {
                    fetches++;
                    var page = await metadata.FetchAsync(item.Url, ct);
                    // в HTML-списке текст ссылки бывает с датой и мусором — заголовок страницы надёжнее
                    if (resource.CollectorKind == CollectorKind.HtmlList && page.Title != null) mi.Title = page.Title;
                    mi.Title ??= page.Title;
                    mi.PublishedAt ??= page.PublishedAt;
                    mi.Text = page.Text;
                    links.AddRange(page.OutLinks ?? []);
                    await Task.Delay(300, ct);
                }
            }

            Evaluate(mi, links, index, keywords, ownPrefixes, opt);
            if (mi.Status == MonitoredItemStatus.Candidate) candidates++;
            if (mi.Status == MonitoredItemStatus.Mention) mentions++;
            // у нерелевантных не храним полный текст: заголовок и анонс нужны, чтобы не обрабатывать
            // публикацию повторно и для оценки редкости слов
            if (mi.Status == MonitoredItemStatus.Irrelevant)
            {
                mi.Summary ??= mi.Text is { Length: > 500 } t ? t[..500] : mi.Text;
                mi.Text = null;
            }
            db.MonitoredItems.Add(mi);
            await db.SaveChangesAsync(ct);
        }

        var cutoff = DateTime.UtcNow.AddDays(-opt.KeepIrrelevantDays);
        await db.MonitoredItems.Where(i => i.Status == MonitoredItemStatus.Irrelevant && i.FoundAt < cutoff)
            .ExecuteDeleteAsync(ct);

        var message = items.Count == 0
            ? "Новых публикаций нет"
            : $"Новых публикаций: {items.Count}, похожих на наши материалы: {candidates}, упоминаний суда: {mentions}";
        log.LogInformation("Мониторинг {Resource}: {Message} (загружено текстов: {Fetches})", resource.Name, message, fetches);
        return new CollectRunResult(true, candidates + mentions, 0, message);
    }

    public static void Evaluate(MonitoredItem mi, IEnumerable<string> links, MaterialIndex index, KeywordMatcher keywords,
        IReadOnlyList<string> ownPrefixes, MonitoringOptions opt)
    {
        var linkKeys = links.Select(UrlKey.TryCreate).OfType<string>().ToHashSet();
        var text = mi.Text ?? mi.Summary;
        var match = index.Best(mi.Title, text, mi.PublishedAt, linkKeys);
        var keyword = keywords.FindIn(mi.Title + "\n" + text);
        var linksToOwn = linkKeys.FirstOrDefault(k => ownPrefixes.Any(p => ResourceResolver.Matches(k, p)));

        mi.Score = match?.Score;
        mi.SuggestedMaterialId = match?.Material.Id;
        var isCandidate = match != null &&
                          (match.Score >= opt.CandidateThreshold ||
                           match.ItemCoverage >= opt.ShortItemCoverage && match.CommonCount >= opt.ShortItemMinCommon);
        if (isCandidate)
        {
            mi.Status = MonitoredItemStatus.Candidate;
            mi.MatchInfo = match!.Info;
        }
        else if (keyword != null || linksToOwn != null)
        {
            mi.Status = MonitoredItemStatus.Mention;
            mi.MatchInfo = keyword != null ? $"упоминание: «{keyword}»" : $"ссылка на наш ресурс: {linksToOwn}";
            if (match != null) mi.MatchInfo += $"; ближайший материал — {match.Info}";
        }
        else
        {
            mi.Status = MonitoredItemStatus.Irrelevant;
            mi.SuggestedMaterialId = null;
        }
    }

    /// <summary>
    /// Индекс собственных материалов за последние дни. Перепечатки чужих материалов (новости ВС РФ и т.п.)
    /// не учитываются: публикации СМИ о них сделаны не на основе наших материалов.
    /// </summary>
    public static async Task<MaterialIndex> BuildIndexAsync(AppDbContext db, MonitoringOptions opt,
        IEnumerable<string> extraBackground, CancellationToken ct)
    {
        var since = DateTime.UtcNow.AddDays(-opt.LookbackDays);
        var backgroundSince = DateTime.UtcNow.AddDays(-opt.BackgroundDays);
        var background = (await db.MonitoredItems.AsNoTracking()
                .Where(i => i.FoundAt >= backgroundSince)
                .Select(i => new { i.Title, i.Summary, i.Text })
                .ToListAsync(ct))
            .Select(i => i.Title + "\n" + i.Summary + "\n" + i.Text)
            .Concat(extraBackground)
            .Select(Stemmer.Set);
        var rows = await db.Materials.AsNoTracking()
            .Where(m => m.IsOwn && !m.IsRepost && m.PublishedAt >= since)
            .Select(m => new
            {
                m.Id, m.Title, m.PublishedAt,
                Links = m.Links.Select(l => new { l.UrlKey, l.Title, l.Text }).ToList(),
            })
            .ToListAsync(ct);
        return new MaterialIndex(rows.Select(r => new IndexedMaterial(
            r.Id, r.Title, r.PublishedAt,
            Stemmer.Set(r.Title + "\n" + string.Join("\n", r.Links.Select(l => l.Title + "\n" + l.Text))),
            r.Links.Select(l => l.UrlKey).ToHashSet())), background);
    }

    private static async Task<List<string>> OwnPrefixesAsync(AppDbContext db, CancellationToken ct) =>
        (await db.Resources.Where(r => r.IsOwn && r.UrlPrefix != null).Select(r => r.UrlPrefix).ToListAsync(ct))
        .Select(ResourceResolver.NormalizePrefix).OfType<string>().ToList();
}
