using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Press.Web.Collectors;
using Press.Web.Data;
using Press.Web.Domain;

namespace Press.Web.Services;

public sealed record CollectRunResult(bool Success, int Added, int Attached, string Message);

/// <summary>
/// Запускает сборщики и сохраняет найденные публикации: новые ссылки либо привязываются
/// к уже существующему материалу (если пост ссылается на него или наоборот), либо становятся новым материалом.
/// </summary>
public class CollectionService(
    IDbContextFactory<AppDbContext> dbFactory,
    IEnumerable<ICollector> collectors,
    MonitoringService monitoring,
    SettingsService settings,
    ILogger<CollectionService> log)
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    public bool IsRunning => _lock.CurrentCount == 0;

    public static bool IsSupported(CollectorKind kind) => kind != CollectorKind.None;

    public async Task<CollectRunResult> RunAsync(int resourceId, DateTime? since, CancellationToken ct)
    {
        await _lock.WaitAsync(ct);
        try
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var resource = await db.Resources.Include(r => r.DefaultMaterialType)
                .FirstAsync(r => r.Id == resourceId, ct);
            var collector = collectors.FirstOrDefault(c => c.Kind == resource.CollectorKind);
            resource.LastCollectedAt = DateTime.UtcNow;

            CollectRunResult result;
            try
            {
                if (collector == null) throw new InvalidOperationException("Для ресурса не настроен сборщик");
                if (!resource.IsOwn)
                {
                    // сторонний ресурс — ищем публикации на основе наших материалов
                    result = await monitoring.ProcessAsync(db, resource, collector, since, ct);
                    resource.LastCollectSucceededAt = DateTime.UtcNow;
                    resource.LastCollectFailed = false;
                    resource.LastCollectMessage = result.Message;
                    await db.SaveChangesAsync(ct);
                    return result;
                }
                var known = (await db.MaterialLinks.Select(l => l.UrlKey).ToListAsync(ct)).ToHashSet();
                var items = await collector.CollectAsync(new CollectRequest(resource, known, since), ct);
                var repostSources = (await settings.GetRepostSourcesAsync(ct))
                    .Select(ResourceResolver.NormalizePrefix).OfType<string>().ToList();

                int added = 0, attached = 0;
                foreach (var item in items.OrderBy(i => i.PublishedAt))
                {
                    var outcome = await IngestAsync(db, resource, item, ct, repostSources);
                    if (outcome == IngestOutcome.Skipped) continue;
                    added++;
                    if (outcome == IngestOutcome.Attached) attached++;
                }

                resource.LastCollectSucceededAt = DateTime.UtcNow;
                resource.LastCollectFailed = false;
                resource.LastCollectMessage = added == 0
                    ? "Новых публикаций нет"
                    : $"Новых публикаций: {added}, из них привязано к существующим материалам: {attached}";
                result = new CollectRunResult(true, added, attached, resource.LastCollectMessage);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogWarning(ex, "Сбор с ресурса {Resource} завершился ошибкой", resource.Name);
                db.ChangeTracker.Clear();
                db.Attach(resource);
                resource.LastCollectedAt = DateTime.UtcNow;
                resource.LastCollectFailed = true;
                resource.LastCollectMessage = ex.Message;
                result = new CollectRunResult(false, 0, 0, ex.Message);
            }
            await db.SaveChangesAsync(ct);
            return result;
        }
        finally
        {
            _lock.Release();
        }
    }

    public enum IngestOutcome { Skipped, Created, Attached }

    /// <param name="repostSources">
    /// Нормализованные префиксы источников перепечаток: ссылка на такой источник в публикации
    /// означает, что это перепечатка чужого материала (см. <see cref="SettingsService.GetRepostSourcesAsync"/>).
    /// </param>
    public static async Task<IngestOutcome> IngestAsync(AppDbContext db, Resource resource, CollectedItem item,
        CancellationToken ct, IReadOnlyCollection<string>? repostSources = null)
    {
        var key = UrlKey.TryCreate(item.Url);
        if (key == null || await db.MaterialLinks.AnyAsync(l => l.UrlKey == key, ct))
            return IngestOutcome.Skipped;

        var outKeys = item.OutLinks.Select(UrlKey.TryCreate).OfType<string>()
            .Where(k => k != key).Distinct().ToList();
        var repostLink = repostSources == null
            ? null
            : outKeys.FirstOrDefault(k => repostSources.Any(p => ResourceResolver.Matches(k, p)));
        if (repostLink != null && !item.IsRepost)
            item = item with { IsRepost = true, RepostSource = item.RepostSource ?? "https://" + repostLink };

        var publishedAt = item.PublishedAt ?? DateTime.UtcNow;
        var material = await FindRelatedOwnMaterialAsync(db, resource.IsOwn, resource.Id, key, item.Title, outKeys, publishedAt, ct);
        var outcome = material == null ? IngestOutcome.Created : IngestOutcome.Attached;

        if (material == null)
        {
            material = new Material
            {
                Title = item.Title ?? item.Url,
                PublishedAt = publishedAt,
                IsOwn = resource.IsOwn,
                IsRepost = item.IsRepost,
                Notes = item.RepostSource == null ? null : "Источник: " + item.RepostSource,
            };
            if (resource.DefaultMaterialType != null) material.Types.Add(resource.DefaultMaterialType);
            db.Materials.Add(material);
        }
        else
        {
            if (publishedAt < material.PublishedAt) material.PublishedAt = publishedAt;
            material.IsRepost |= item.IsRepost;
            // на сайте полный заголовок, в соцсетях — первая строка поста
            if (resource.Kind == ResourceKind.Website && !material.TitleIsManual && item.Title != null)
                material.Title = item.Title;
        }

        material.Links.Add(new MaterialLink
        {
            Resource = resource,
            Url = item.Url,
            UrlKey = key,
            Title = item.Title,
            Text = item.Text,
            PublishedAt = item.PublishedAt,
            OutLinkKeys = outKeys.Count == 0 ? null : "\n" + string.Join("\n", outKeys) + "\n",
            IsAutoCollected = true,
        });
        await db.SaveChangesAsync(ct);
        return outcome;
    }

    /// <summary>Окно, в котором публикации с общей ссылкой считаются одним материалом.</summary>
    private static readonly TimeSpan SharedLinkWindow = TimeSpan.FromDays(10);

    /// <summary>
    /// Ищет материал, к которому относится публикация:
    /// 1) она ссылается на одну из его ссылок (пост в Telegram → «Подробнее» на сайте);
    /// 2) одна из его ссылок ссылается на неё;
    /// 3) обе ссылаются на одну и ту же конкретную публикацию (новость ВС РФ, пост другого канала)
    ///    и вышли с разницей не более <see cref="SharedLinkWindow"/>;
    /// 4) у них почти одинаковые заголовки (см. <see cref="FindBySimilarTitleAsync"/>).
    /// </summary>
    private static async Task<Material?> FindRelatedOwnMaterialAsync(AppDbContext db, bool isOwn, int resourceId,
        string key, string? title, List<string> outKeys, DateTime publishedAt, CancellationToken ct)
    {
        var links = db.MaterialLinks.Include(l => l.Material).ThenInclude(m => m.Links)
            .Where(l => l.Material.IsOwn == isOwn);

        if (outKeys.Count > 0)
        {
            var byOut = await links.Where(l => outKeys.Contains(l.UrlKey)).Select(l => l.Material).FirstOrDefaultAsync(ct);
            if (byOut != null) return byOut;
        }

        var marker = "\n" + key + "\n";
        var byIn = await links.Where(l => l.OutLinkKeys != null && l.OutLinkKeys.Contains(marker))
            .Select(l => l.Material).FirstOrDefaultAsync(ct);
        if (byIn != null) return byIn;

        var from = publishedAt - SharedLinkWindow;
        var to = publishedAt + SharedLinkWindow;
        foreach (var shared in outKeys.Where(UrlKey.IsSpecific))
        {
            var m = "\n" + shared + "\n";
            var byShared = await links
                .Where(l => l.OutLinkKeys != null && l.OutLinkKeys.Contains(m))
                .Where(l => l.Material.PublishedAt >= from && l.Material.PublishedAt <= to)
                .Select(l => l.Material).FirstOrDefaultAsync(ct);
            if (byShared != null) return byShared;
        }

        return title == null ? null : await FindBySimilarTitleAsync(db, isOwn, resourceId, title, publishedAt, ct);
    }

    /// <summary>Порог похожести заголовков, при котором публикации считаются одним материалом.</summary>
    public const double SameTitleThreshold = 0.6;
    private static readonly TimeSpan SameTitleWindow = TimeSpan.FromDays(3);

    /// <summary>
    /// Материал с почти тем же заголовком, вышедший в пределах <see cref="SameTitleWindow"/>
    /// и ещё не опубликованный на этом же ресурсе (два разных поста одного канала не склеиваем).
    /// </summary>
    private static async Task<Material?> FindBySimilarTitleAsync(AppDbContext db, bool isOwn, int resourceId,
        string title, DateTime publishedAt, CancellationToken ct)
    {
        var from = publishedAt - SameTitleWindow;
        var to = publishedAt + SameTitleWindow;
        var candidates = await db.Materials.Include(m => m.Links)
            .Where(m => m.IsOwn == isOwn && m.PublishedAt >= from && m.PublishedAt <= to)
            .Where(m => !m.Links.Any(l => l.ResourceId == resourceId))
            .ToListAsync(ct);
        return candidates
            .Select(m => (m, score: m.Links.Select(l => l.Title).Append(m.Title)
                .Max(t => TitleSimilarity.Score(t, title))))
            .Where(x => x.score >= SameTitleThreshold)
            .OrderByDescending(x => x.score)
            .Select(x => x.m)
            .FirstOrDefault();
    }
}

/// <summary>Периодически запускает сбор с собственных ресурсов и мониторинг сторонних.</summary>
public class CollectionScheduler(
    IDbContextFactory<AppDbContext> dbFactory,
    CollectionService collection,
    IOptionsMonitor<MonitoringOptions> options,
    IConfiguration config,
    ILogger<CollectionScheduler> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!config.GetValue("Collection:Enabled", true))
        {
            log.LogInformation("Автоматический сбор отключён (Collection:Enabled = false)");
            return;
        }
        await Task.Delay(TimeSpan.FromSeconds(15), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await RunDueAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                log.LogError(ex, "Ошибка планировщика сбора");
            }
        } while (await timer.WaitForNextTickAsync(ct));
    }

    private async Task RunDueAsync(CancellationToken ct)
    {
        List<Resource> resources;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            resources = await db.Resources.AsNoTracking()
                .Where(r => r.IsCollectorEnabled && r.CollectorKind != CollectorKind.None)
                .OrderByDescending(r => r.IsOwn) // сначала свои — чтобы было с чем сравнивать
                .ToListAsync(ct);
        }
        var now = DateTime.UtcNow;
        foreach (var r in resources)
        {
            var interval = TimeSpan.FromMinutes(Math.Max(5, r.CollectIntervalMinutes));
            if (r.LastCollectedAt is { } last && last + interval > now) continue;
            // при первом запуске загружаем архив собственных публикаций — чтобы мониторингу было с чем сравнивать
            DateTime? since = r.IsOwn && r.LastCollectSucceededAt == null
                ? DateTime.UtcNow.AddDays(-options.CurrentValue.LookbackDays)
                : null;
            var result = await collection.RunAsync(r.Id, since, ct);
            log.LogInformation("Сбор {Resource}: {Message}", r.Name, result.Message);
        }
    }
}
