using Microsoft.EntityFrameworkCore;
using Press.Web.Data;
using Press.Web.Domain;

namespace Press.Web.Services;

public sealed record LinkInput(string Url, string? Title, DateTime? PublishedAt, string? Text);

public class MaterialService(IDbContextFactory<AppDbContext> dbFactory)
{
    /// <summary>Создаёт материал по первой ссылке. Возвращает id или текст ошибки.</summary>
    public async Task<(int? Id, string? Error)> CreateAsync(Material material, IEnumerable<int> typeIds,
        LinkInput? firstLink, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        material.Types = await db.MaterialTypes.Where(t => typeIds.Contains(t.Id)).ToListAsync(ct);
        if (firstLink != null)
        {
            var (link, error) = await BuildLinkAsync(db, firstLink, ct);
            if (error != null) return (null, error);
            material.Links.Add(link!);
        }
        db.Materials.Add(material);
        await db.SaveChangesAsync(ct);
        return (material.Id, null);
    }

    public async Task UpdateAsync(Material edited, IEnumerable<int> typeIds, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.Materials.Include(x => x.Types).FirstAsync(x => x.Id == edited.Id, ct);
        if (m.Title != edited.Title) m.TitleIsManual = true;
        m.Title = edited.Title;
        m.PublishedAt = edited.PublishedAt;
        m.IsOwn = edited.IsOwn;
        m.IsRepost = edited.IsRepost;
        m.Notes = edited.Notes;
        var ids = typeIds.ToHashSet();
        m.Types.Clear();
        foreach (var t in await db.MaterialTypes.Where(t => ids.Contains(t.Id)).ToListAsync(ct))
            m.Types.Add(t);
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> AddLinkAsync(int materialId, LinkInput input, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var m = await db.Materials.FirstAsync(x => x.Id == materialId, ct);
        var (link, error) = await BuildLinkAsync(db, input, ct);
        if (error != null) return error;
        m.Links.Add(link!);
        if (input.PublishedAt is { } p && p < m.PublishedAt) m.PublishedAt = p;
        await db.SaveChangesAsync(ct);
        return null;
    }

    public async Task RemoveLinkAsync(int linkId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.MaterialLinks.Where(l => l.Id == linkId).ExecuteDeleteAsync(ct);
    }

    /// <summary>
    /// Добавляет сторонний материал, сделанный на основе собственного. Если такая ссылка уже есть в базе —
    /// привязывает существующий материал (одна перепечатка может основываться на нескольких наших материалах).
    /// </summary>
    public async Task<string?> AddDerivedAsync(int sourceId, LinkInput input, IEnumerable<int> typeIds, string? user,
        CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var key = UrlKey.TryCreate(input.Url);
        if (key == null) return "Некорректная ссылка";
        var source = await db.Materials.FirstOrDefaultAsync(m => m.Id == sourceId, ct);
        if (source == null) return $"Материал №{sourceId} не найден";
        if (!source.IsOwn) return $"Материал №{sourceId} не является собственным";

        var existing = await db.MaterialLinks.Include(l => l.Material)
            .FirstOrDefaultAsync(l => l.UrlKey == key, ct);
        Material derived;
        if (existing != null)
        {
            derived = existing.Material;
            if (derived.Id == sourceId) return "Эта ссылка относится к самому материалу";
            if (derived.IsOwn) return $"Ссылка уже принадлежит собственному материалу №{derived.Id}";
        }
        else
        {
            var (link, error) = await BuildLinkAsync(db, input, ct);
            if (error != null) return error;
            derived = new Material
            {
                Title = string.IsNullOrWhiteSpace(input.Title) ? input.Url : input.Title.Trim(),
                PublishedAt = input.PublishedAt ?? DateTime.UtcNow,
                IsOwn = false,
                Types = await db.MaterialTypes.Where(t => typeIds.Contains(t.Id)).ToListAsync(ct),
            };
            derived.Links.Add(link!);
            db.Materials.Add(derived);
        }

        var relation = derived.Id == 0
            ? null
            : await db.MaterialRelations.FirstOrDefaultAsync(
                r => r.SourceMaterialId == sourceId && r.DerivedMaterialId == derived.Id, ct);
        if (relation == null)
        {
            relation = new MaterialRelation { SourceMaterialId = sourceId, DerivedMaterial = derived, CreatedAt = DateTime.UtcNow };
            db.MaterialRelations.Add(relation);
        }
        relation.Status = RelationStatus.Confirmed;
        relation.ReviewedAt = DateTime.UtcNow;
        relation.ReviewedBy = user;

        // если эту публикацию нашёл мониторинг — отмечаем её как проверенную
        var monitored = await db.MonitoredItems.FirstOrDefaultAsync(i => i.UrlKey == key, ct);
        if (monitored != null)
        {
            relation.Score ??= monitored.Score;
            monitored.Status = MonitoredItemStatus.Confirmed;
            monitored.SuggestedMaterialId = sourceId;
            monitored.DerivedMaterial = derived;
            monitored.ReviewedAt = DateTime.UtcNow;
            monitored.ReviewedBy = user;
        }
        await db.SaveChangesAsync(ct);
        return null;
    }

    /// <summary>Подтверждает публикацию из очереди мониторинга как сделанную на основе материала <paramref name="sourceId"/>.</summary>
    public async Task<string?> ConfirmMonitoredAsync(int itemId, int sourceId, IEnumerable<int> typeIds, string? user,
        CancellationToken ct = default)
    {
        MonitoredItem item;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
            item = await db.MonitoredItems.AsNoTracking().FirstAsync(i => i.Id == itemId, ct);
        return await AddDerivedAsync(sourceId,
            new LinkInput(item.Url, item.Title, item.PublishedAt, item.Text ?? item.Summary), typeIds, user, ct);
    }

    public async Task SetMonitoredStatusAsync(int itemId, MonitoredItemStatus status, string? user, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var item = await db.MonitoredItems.FirstAsync(i => i.Id == itemId, ct);
        item.Status = status;
        item.ReviewedAt = DateTime.UtcNow;
        item.ReviewedBy = user;
        await db.SaveChangesAsync(ct);
    }

    public async Task SetRelationStatusAsync(int relationId, RelationStatus status, string? user, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = await db.MaterialRelations.FirstAsync(x => x.Id == relationId, ct);
        r.Status = status;
        r.ReviewedAt = DateTime.UtcNow;
        r.ReviewedBy = user;
        await db.SaveChangesAsync(ct);
    }

    /// <summary>Удаляет связь; сторонний материал, не связанный больше ни с чем, удаляется вместе с ней.</summary>
    public async Task RemoveRelationAsync(int relationId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var r = await db.MaterialRelations.Include(x => x.DerivedMaterial).FirstOrDefaultAsync(x => x.Id == relationId, ct);
        if (r == null) return;
        db.MaterialRelations.Remove(r);
        // публикация из мониторинга возвращается в очередь проверки
        await db.MonitoredItems
            .Where(i => i.DerivedMaterialId == r.DerivedMaterialId && i.SuggestedMaterialId == r.SourceMaterialId)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.Status, MonitoredItemStatus.Candidate), ct);
        await db.SaveChangesAsync(ct);
        await DeleteOrphansAsync(db, [r.DerivedMaterialId], ct);
    }

    public async Task DeleteAsync(int id, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var derivedIds = await db.MaterialRelations.Where(r => r.SourceMaterialId == id)
            .Select(r => r.DerivedMaterialId).ToListAsync(ct);
        await db.Materials.Where(m => m.Id == id).ExecuteDeleteAsync(ct);
        await DeleteOrphansAsync(db, derivedIds, ct);
    }

    /// <summary>Переносит ссылки, виды и связи материала <paramref name="otherId"/> в <paramref name="targetId"/> и удаляет его.</summary>
    public async Task<string?> MergeAsync(int targetId, int otherId, CancellationToken ct = default)
    {
        if (targetId == otherId) return "Нельзя объединить материал сам с собой";
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var target = await db.Materials.Include(m => m.Types).Include(m => m.Derived).Include(m => m.Sources)
            .FirstAsync(m => m.Id == targetId, ct);
        var other = await db.Materials.Include(m => m.Types).Include(m => m.Links).Include(m => m.Derived).Include(m => m.Sources)
            .FirstOrDefaultAsync(m => m.Id == otherId, ct);
        if (other == null) return $"Материал №{otherId} не найден";
        if (other.IsOwn != target.IsOwn) return "Нельзя объединить собственный материал со сторонним";

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        foreach (var link in other.Links.ToList()) link.MaterialId = target.Id;
        foreach (var t in other.Types.Where(t => target.Types.All(x => x.Id != t.Id)).ToList()) target.Types.Add(t);
        foreach (var r in other.Derived.ToList())
        {
            if (r.DerivedMaterialId == target.Id || target.Derived.Any(x => x.DerivedMaterialId == r.DerivedMaterialId))
                db.MaterialRelations.Remove(r);
            else r.SourceMaterialId = target.Id;
        }
        foreach (var r in other.Sources.ToList())
        {
            if (r.SourceMaterialId == target.Id || target.Sources.Any(x => x.SourceMaterialId == r.SourceMaterialId))
                db.MaterialRelations.Remove(r);
            else r.DerivedMaterialId = target.Id;
        }
        if (other.PublishedAt < target.PublishedAt) target.PublishedAt = other.PublishedAt;
        if (!string.IsNullOrWhiteSpace(other.Notes))
            target.Notes = string.IsNullOrWhiteSpace(target.Notes) ? other.Notes : target.Notes + "\n" + other.Notes;
        await db.SaveChangesAsync(ct);
        db.Materials.Remove(other);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return null;
    }

    private static async Task<(MaterialLink? Link, string? Error)> BuildLinkAsync(AppDbContext db, LinkInput input,
        CancellationToken ct)
    {
        var url = input.Url.Trim();
        if (!url.Contains("://")) url = "https://" + url;
        var key = UrlKey.TryCreate(url);
        if (key == null) return (null, "Некорректная ссылка");
        var dup = await db.MaterialLinks.Where(l => l.UrlKey == key).Select(l => (int?)l.MaterialId).FirstOrDefaultAsync(ct);
        if (dup != null) return (null, $"Эта ссылка уже есть в базе (материал №{dup})");
        return (new MaterialLink
        {
            Url = url,
            UrlKey = key,
            Title = string.IsNullOrWhiteSpace(input.Title) ? null : input.Title.Trim(),
            Text = input.Text,
            PublishedAt = input.PublishedAt,
            Resource = await ResourceResolver.FindOrCreateAsync(db, url, ct),
        }, null);
    }

    private static async Task DeleteOrphansAsync(AppDbContext db, IReadOnlyCollection<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return;
        await db.Materials
            .Where(m => ids.Contains(m.Id) && !m.IsOwn && !m.Sources.Any() && !m.Derived.Any())
            .ExecuteDeleteAsync(ct);
    }
}
