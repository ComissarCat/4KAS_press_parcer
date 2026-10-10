using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;

namespace Press.Web.Services;

/// <summary>Загружает HTML-страницы с учётом кодировки (сайты sudrf.ru отдают windows-1251).</summary>
public partial class HtmlFetcher(IHttpClientFactory httpFactory)
{
    public const string HttpClientName = "web";

    static HtmlFetcher() => EnsureEncodings();

    /// <summary>Регистрирует кодовые страницы (windows-1251 и др.); повторный вызов безопасен.</summary>
    public static void EnsureEncodings() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    public async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        var http = httpFactory.CreateClient(HttpClientName);
        using var resp = await http.GetAsync(url, ct);
        resp.EnsureSuccessStatusCode();
        var bytes = await resp.Content.ReadAsByteArrayAsync(ct);
        return Decode(bytes, resp.Content.Headers.ContentType?.CharSet);
    }

    public async Task<IHtmlDocument> GetDocumentAsync(string url, CancellationToken ct)
    {
        var html = await GetStringAsync(url, ct);
        return Parse(html, url);
    }

    public static IHtmlDocument Parse(string html, string? baseUrl = null)
    {
        var doc = new HtmlParser().ParseDocument(html);
        if (baseUrl != null && doc.Head != null && doc.QuerySelector("base") == null)
        {
            // чтобы относительные ссылки (a.Href) раскрывались в абсолютные
            var b = doc.CreateElement("base");
            b.SetAttribute("href", baseUrl);
            doc.Head.Prepend(b);
        }
        return doc;
    }

    public static string Decode(byte[] bytes, string? headerCharset)
    {
        var enc = TryGetEncoding(headerCharset);
        if (enc == null)
        {
            var head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 4096));
            var m = MetaCharsetRegex().Match(head);
            if (m.Success) enc = TryGetEncoding(m.Groups[1].Value);
        }
        return (enc ?? Encoding.UTF8).GetString(bytes);
    }

    private static Encoding? TryGetEncoding(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        try { return Encoding.GetEncoding(name.Trim('"', '\'', ' ')); }
        catch (ArgumentException) { return null; }
    }

    [GeneratedRegex(@"<meta[^>]+charset\s*=\s*[""']?([\w-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex MetaCharsetRegex();
}
