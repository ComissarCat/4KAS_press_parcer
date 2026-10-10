using Microsoft.EntityFrameworkCore;
using Press.Web.Data;
using Press.Web.Domain;

namespace Press.Web.Services;

/// <summary>Настройки, которые пресс-служба меняет из интерфейса.</summary>
public class SettingsService(IDbContextFactory<AppDbContext> dbFactory)
{
    private const string KeywordsKey = "Monitoring.Keywords";

    /// <summary>Ключевые фразы, по которым публикация считается упоминанием суда.</summary>
    public async Task<IReadOnlyList<string>> GetKeywordsAsync(CancellationToken ct = default) =>
        await GetListAsync(KeywordsKey, ct) ?? KeywordMatcher.Defaults;

    public Task SetKeywordsAsync(IEnumerable<string> phrases, CancellationToken ct = default) =>
        SetListAsync(KeywordsKey, phrases, ct);

    private const string RepostSourcesKey = "Collection.RepostSources";

    public static readonly string[] DefaultRepostSources = ["vsrf.ru", "t.me/sovetsudey/", "max.ru/vsrf"];

    /// <summary>
    /// Источники, ссылка на которые в нашей публикации означает перепечатку чужого материала
    /// (новости Верховного Суда, Совета судей). Такие материалы не участвуют в поиске перепечаток.
    /// </summary>
    public async Task<IReadOnlyList<string>> GetRepostSourcesAsync(CancellationToken ct = default) =>
        await GetListAsync(RepostSourcesKey, ct) ?? DefaultRepostSources;

    public Task SetRepostSourcesAsync(IEnumerable<string> prefixes, CancellationToken ct = default) =>
        SetListAsync(RepostSourcesKey, prefixes, ct);

    private async Task<IReadOnlyList<string>?> GetListAsync(string key, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var value = await db.Settings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);
        return value?.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private async Task SetListAsync(string key, IEnumerable<string> values, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var setting = await db.Settings.FindAsync([key], ct);
        if (setting == null) db.Settings.Add(setting = new AppSetting { Key = key });
        setting.Value = string.Join("\n", values.Select(p => p.Trim()).Where(p => p.Length > 0).Distinct());
        await db.SaveChangesAsync(ct);
    }
}
