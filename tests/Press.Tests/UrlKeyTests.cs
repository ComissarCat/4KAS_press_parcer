using Press.Web.Services;

namespace Press.Tests;

public class UrlKeyTests
{
    [Theory]
    [InlineData("https://4kas.sudrf.ru/modules.php?name=press_dep&op=1&did=403", "4kas.sudrf.ru/modules.php?did=403&name=press_dep&op=1")]
    [InlineData("http://4kas.sudrf.ru/modules.php?name=press_dep&amp;amp;op=1&amp;amp;did=403", "4kas.sudrf.ru/modules.php?did=403&name=press_dep&op=1")]
    [InlineData("https://www.Example.ru/news/123/?utm_source=tg&utm_medium=x", "example.ru/news/123")]
    [InlineData("https://t.me/s/sud_kas_4/891", "t.me/sud_kas_4/891")]
    [InlineData("https://t.me/sud_kas_4/891?single", "t.me/sud_kas_4/891")]
    [InlineData("https://telegram.me/sud_kas_4/891", "t.me/sud_kas_4/891")]
    [InlineData("https://vk.ru/wall-217666653_10", "vk.com/wall-217666653_10")]
    [InlineData("https://m.vk.com/wall-217666653_10", "vk.com/wall-217666653_10")]
    [InlineData("https://vk.com/4kassud?w=wall-217666653_10", "vk.com/wall-217666653_10")]
    [InlineData("example.ru/a", "example.ru/a")]
    [InlineData("https://kubnews.ru/amp/obshchestvo/2026/10/09/delo/", "kubnews.ru/obshchestvo/2026/10/09/delo")]
    [InlineData("https://rg.ru/amp/2026/10/09/reg-ufo/delo.html", "rg.ru/2026/10/09/reg-ufo/delo.html")]
    [InlineData("https://example.ru/news/1/amp", "example.ru/news/1")]
    public void Normalizes(string url, string expected) => Assert.Equal(expected, UrlKey.TryCreate(url));

    [Theory]
    [InlineData("")]
    [InlineData("not a url at all")]
    [InlineData("ftp://example.ru/file")]
    public void RejectsInvalid(string url) => Assert.Null(UrlKey.TryCreate(url));

    [Fact]
    public void FindsUrlsInText()
    {
        var urls = UrlKey.FindUrls("Подробнее: https://4kas.sudrf.ru/modules.php?name=press_dep&op=1&did=5. И «https://t.me/x/1»").ToList();
        Assert.Equal(["https://4kas.sudrf.ru/modules.php?name=press_dep&op=1&did=5", "https://t.me/x/1"], urls);
    }

    [Theory]
    [InlineData("4kas.sudrf.ru/modules.php?did=1", "4kas.sudrf.ru", true)]
    [InlineData("4kas.sudrf.ru.evil.com/x", "4kas.sudrf.ru", false)]
    [InlineData("t.me/sud_kas_4/891", "t.me/sud_kas_4/", true)]
    [InlineData("t.me/sud_kas_44/891", "t.me/sud_kas_4/", false)]
    [InlineData("vk.com/wall-217666653_10", "vk.com/wall-217666653_", true)]
    public void MatchesResourcePrefix(string key, string prefix, bool expected) =>
        Assert.Equal(expected, ResourceResolver.Matches(key, ResourceResolver.NormalizePrefix(prefix)!));

    [Theory]
    [InlineData("t.me/rian_ru/123", "t.me/rian_ru/")]
    [InlineData("vk.com/wall-1_5", "vk.com/wall-1_")]
    [InlineData("rg.ru/2026/10/09/news.html", "rg.ru")]
    public void GuessesResourcePrefix(string key, string prefix) =>
        Assert.Equal(prefix, ResourceResolver.Guess(key).Prefix);
}
