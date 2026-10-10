using Microsoft.EntityFrameworkCore;
using Press.Web.Data;
using Press.Web.Domain;

namespace Press.Web.Services;

/// <summary>Определяет, к какому ресурсу относится ссылка (по полю <see cref="Resource.UrlPrefix"/>).</summary>
public static class ResourceResolver
{
    public static async Task<Resource?> FindAsync(AppDbContext db, string url, CancellationToken ct = default)
    {
        var key = UrlKey.TryCreate(url);
        if (key == null) return null;
        var resources = await db.Resources.Where(r => r.UrlPrefix != null).ToListAsync(ct);
        return resources
            .Select(r => (r, prefix: NormalizePrefix(r.UrlPrefix)))
            .Where(x => x.prefix != null && Matches(key, x.prefix))
            .OrderByDescending(x => x.prefix!.Length)
            .Select(x => x.r)
            .FirstOrDefault();
    }

    /// <summary>Находит ресурс или заводит новый сторонний ресурс по домену / каналу.</summary>
    public static async Task<Resource?> FindOrCreateAsync(AppDbContext db, string url, CancellationToken ct = default)
    {
        var found = await FindAsync(db, url, ct);
        if (found != null) return found;
        var key = UrlKey.TryCreate(url);
        if (key == null) return null;

        var (name, prefix, kind) = Guess(key);
        var r = new Resource { Name = name, UrlPrefix = prefix, Kind = kind, IsOwn = false, HomeUrl = "https://" + prefix.TrimEnd('/', '_') };
        db.Resources.Add(r);
        return r;
    }

    public static (string Name, string Prefix, ResourceKind Kind) Guess(string key)
    {
        var parts = key.Split('/');
        var host = parts[0].Split('?')[0];
        if (host == "t.me" && parts.Length > 1)
            return ("Telegram: " + parts[1], $"t.me/{parts[1]}/", ResourceKind.Telegram);
        if (host == "vk.com" && parts.Length > 1 && parts[1].StartsWith("wall"))
        {
            var owner = parts[1].Split('_')[0];
            return ("ВКонтакте: " + owner[4..], $"vk.com/{owner}_", ResourceKind.Vk);
        }
        if (host == "max.ru" && parts.Length > 1)
            return ("MAX: " + parts[1], $"max.ru/{parts[1]}", ResourceKind.Max);
        return (host, host, ResourceKind.Website);
    }

    public static string? NormalizePrefix(string? prefix)
    {
        if (string.IsNullOrWhiteSpace(prefix)) return null;
        var p = prefix.Trim();
        var trailingSlash = p.EndsWith('/');
        var key = UrlKey.TryCreate(p);
        if (key == null) return null;
        // UrlKey срезает завершающий «/», а для «t.me/канал/» он важен: иначе «t.me/канал» совпадёт с «t.me/канал2»
        return trailingSlash ? key + "/" : key;
    }

    public static bool Matches(string key, string prefix)
    {
        if (!key.StartsWith(prefix, StringComparison.Ordinal)) return false;
        if (key.Length == prefix.Length) return true;
        var last = prefix[^1];
        if (last is '/' or '_' or '?') return true;
        // «4kas.sudrf.ru» не должен совпадать с «4kas.sudrf.ru.evil.com»
        return key[prefix.Length] is '/' or '?';
    }
}
