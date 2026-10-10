using System.Net;
using System.Text.RegularExpressions;

namespace Press.Web.Services;

/// <summary>
/// Приводит ссылку к каноническому виду, чтобы одна и та же публикация,
/// записанная по-разному (http/https, www, m., vk.ru/vk.com, utm-метки, &amp;amp;), давала один ключ.
/// </summary>
public static partial class UrlKey
{
    private static readonly HashSet<string> JunkParams = new(StringComparer.OrdinalIgnoreCase)
    {
        "fbclid", "yclid", "gclid", "ysclid", "from", "ref", "erid", "single",
    };

    public static string? TryCreate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        var s = url.Trim();
        // В веб-версии Telegram ссылки бывают экранированы дважды: &amp;amp;
        for (var i = 0; i < 3 && s.Contains('&') && s.Contains(';'); i++)
            s = WebUtility.HtmlDecode(s);
        if (!s.Contains("://")) s = "https://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) return null;

        var host = uri.IdnHost.ToLowerInvariant();
        foreach (var prefix in new[] { "www.", "m.", "mobile." })
            if (host.StartsWith(prefix)) host = host[prefix.Length..];
        host = host switch
        {
            "vk.ru" or "vkontakte.ru" => "vk.com",
            "telegram.me" => "t.me",
            _ => host,
        };

        var path = Uri.UnescapeDataString(uri.AbsolutePath).TrimEnd('/').ToLowerInvariant();
        var query = ParseQuery(uri.Query);

        if (host == "t.me")
        {
            // t.me/s/канал/123 → t.me/канал/123; параметры (?single, ?embed) не важны
            if (path.StartsWith("/s/")) path = path[2..];
            query.Clear();
        }
        else if (host == "vk.com")
        {
            // vk.com/club123?w=wall-123_45 → vk.com/wall-123_45
            var w = query.FirstOrDefault(p => p.Key == "w" && p.Value.StartsWith("wall"));
            if (w.Key != null)
            {
                path = "/" + w.Value;
                query.Clear();
            }
        }

        var q = query
            .Where(p => !p.Key.StartsWith("utm_") && !JunkParams.Contains(p.Key))
            .OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => p.Value.Length == 0 ? p.Key : p.Key + "=" + p.Value);
        var qs = string.Join("&", q);
        return host + path + (qs.Length > 0 ? "?" + qs : "");
    }

    public static string Create(string url) =>
        TryCreate(url) ?? throw new ArgumentException($"Некорректная ссылка: {url}", nameof(url));

    /// <summary>Хост из ключа или ссылки (без www и т.п.).</summary>
    public static string? Host(string? url)
    {
        var key = TryCreate(url);
        if (key == null) return null;
        var slash = key.IndexOfAny(['/', '?']);
        return slash < 0 ? key : key[..slash];
    }

    /// <summary>
    /// Ссылка на конкретную публикацию, а не на главную страницу сайта или канала
    /// (vsrf.ru/news/37888, t.me/канал/123 — да; t.me/канал, max.ru/vsrf — нет).
    /// </summary>
    public static bool IsSpecific(string key)
    {
        var q = key.IndexOf('?');
        var path = q < 0 ? key : key[..q];
        return q >= 0 || path.Count(c => c == '/') >= 2;
    }

    /// <summary>Находит в тексте все http(s)-ссылки.</summary>
    public static IEnumerable<string> FindUrls(string? text) =>
        string.IsNullOrEmpty(text)
            ? []
            : UrlRegex().Matches(text).Select(m => m.Value.TrimEnd('.', ',', ')', ';', ':', '!', '?', '»', '"'));

    private static List<KeyValuePair<string, string>> ParseQuery(string query) =>
        query.TrimStart('?')
            .Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(part =>
            {
                var eq = part.IndexOf('=');
                var k = eq < 0 ? part : part[..eq];
                var v = eq < 0 ? "" : part[(eq + 1)..];
                return new KeyValuePair<string, string>(
                    Uri.UnescapeDataString(k).ToLowerInvariant(),
                    Uri.UnescapeDataString(v.Replace('+', ' ')).ToLowerInvariant());
            })
            .ToList();

    [GeneratedRegex(@"https?://[^\s<>""'«»]+", RegexOptions.IgnoreCase)]
    private static partial Regex UrlRegex();
}
