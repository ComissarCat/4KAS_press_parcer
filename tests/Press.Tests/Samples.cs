using AngleSharp.Html.Dom;
using Press.Web.Services;

namespace Press.Tests;

/// <summary>Сохранённые копии реальных страниц (октябрь 2026).</summary>
internal static class Samples
{
    public static IHtmlDocument Load(string name, string baseUrl)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Samples", name));
        return HtmlFetcher.Parse(HtmlFetcher.Decode(bytes, null), baseUrl);
    }
}
