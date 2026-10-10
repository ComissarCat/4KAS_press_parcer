using Press.Web.Collectors;
using Press.Web.Services;

namespace Press.Tests;

public class CollectorParsingTests
{
    private const string Site = "https://4kas.sudrf.ru";

    [Fact]
    public void SudrfList()
    {
        var doc = Samples.Load("sudrf_list.html", Site + "/modules.php?name=press_dep");
        var list = SudrfPressCollector.ParseList(doc);

        Assert.Equal(30, list.Count);
        var first = list[0];
        Assert.Equal(Site + "/modules.php?name=press_dep&op=1&did=421", first.Url);
        Assert.Equal(new DateTime(2026, 10, 9), first.Date);
        Assert.StartsWith("Председатель Верховного Суда обозначил роль судебного диалога", first.Title);
        Assert.Contains(list, e => e.Url.EndsWith("did=420") && e.Title.StartsWith("Четвертый кассационный суд вернул на пересмотр"));
    }

    [Fact]
    public void SudrfArchive()
    {
        var doc = Samples.Load("sudrf_archive.html", Site + "/modules.php?name=press_dep&op=12&arc_list=2025-12");
        var list = SudrfPressCollector.ParseList(doc);
        Assert.NotEmpty(list);
        Assert.All(list, e => Assert.Equal(new DateTime(2025, 12, 1).Month, e.Date!.Value.Month));
    }

    [Fact]
    public void SudrfDetail()
    {
        var doc = Samples.Load("sudrf_item.html", Site + "/modules.php?name=press_dep&op=1&did=421");
        var d = SudrfPressCollector.ParseDetail(doc, Site);

        Assert.StartsWith("Председатель Верховного Суда обозначил роль", d.Title);
        Assert.Equal(new DateTime(2026, 10, 9, 15, 32, 0, DateTimeKind.Utc), d.PublishedAt); // 18:32 МСК
        Assert.Contains("Игорь Краснов", d.Text);
        Assert.DoesNotContain("версия для печати", d.Text);
        // новость перепечатана с сайта Верховного Суда
        Assert.True(d.IsRepost);
        Assert.Equal("https://www.vsrf.ru/news/37903", d.RepostSource);
    }

    [Fact]
    public void TelegramChannelPage()
    {
        var doc = Samples.Load("tg_channel.html", "https://t.me/s/sud_kas_4");
        var posts = TelegramPublicCollector.ParsePage(doc);

        Assert.Equal(20, posts.Count);
        Assert.All(posts, p => Assert.NotNull(p.Item.PublishedAt));

        var p891 = posts.Single(p => p.Id == 891).Item;
        Assert.Equal("https://t.me/sud_kas_4/891", p891.Url);
        Assert.StartsWith("Четвертый кассационный суд вернул на пересмотр уголовное дело", p891.Title);
        Assert.Contains("\n", p891.Text); // переносы строк сохранены
        Assert.False(p891.IsRepost);

        // посты со ссылкой «Подробнее» на сайт суда
        var withSite = posts.Where(p => p.Item.OutLinks.Any(l => UrlKey.TryCreate(l)!.Contains("name=press_dep"))).ToList();
        Assert.NotEmpty(withSite);
        Assert.Contains(withSite, p => p.Item.OutLinks.Any(l => UrlKey.TryCreate(l) == "4kas.sudrf.ru/modules.php?did=403&name=press_dep&op=1"));

        // пересланный пост из канала Совета судей
        var fwd = posts.Single(p => p.Item.IsRepost).Item;
        Assert.StartsWith("https://t.me/sovetsudey/", fwd.RepostSource);
    }

    [Fact]
    public void TelegramEmbed()
    {
        var doc = Samples.Load("tg_embed.html", "https://t.me/sud_kas_4/891?embed=1");
        var post = Assert.Single(TelegramPublicCollector.ParsePage(doc));
        Assert.Equal(891, post.Id);
        Assert.StartsWith("Четвертый кассационный суд вернул", post.Item.Title);
    }

    [Theory]
    [InlineData("sud_kas_4", "sud_kas_4")]
    [InlineData("@sud_kas_4", "sud_kas_4")]
    [InlineData("https://t.me/sud_kas_4", "sud_kas_4")]
    [InlineData("https://t.me/s/sud_kas_4", "sud_kas_4")]
    public void TelegramChannelName(string target, string expected) =>
        Assert.Equal(expected, TelegramPublicCollector.ChannelName(target));

    [Theory]
    [InlineData("4kassud", "domain=4kassud")]
    [InlineData("https://vk.ru/4kassud", "domain=4kassud")]
    [InlineData("-217666653", "owner_id=-217666653")]
    [InlineData("public217666653", "owner_id=-217666653")]
    [InlineData("club217666653", "owner_id=-217666653")]
    public void VkOwner(string target, string expected) =>
        Assert.Equal(expected, VkWallCollector.OwnerParam(target));

    [Fact]
    public void VkResponse()
    {
        const string json = """
        {"response":{"count":2,"items":[
          {"id":10,"owner_id":-217666653,"date":1791590400,
           "text":"Кассация отменила приговор\n\nПодробнее: https://4kas.sudrf.ru/modules.php?name=press_dep&op=1&did=420",
           "attachments":[{"type":"link","link":{"url":"https://4kas.sudrf.ru/modules.php?name=press_dep&op=1&did=420","title":"x"}}]},
          {"id":9,"owner_id":-217666653,"date":1791500000,"text":"",
           "copy_history":[{"id":55,"owner_id":-1,"text":"Новость Верховного Суда"}]}
        ]}}
        """;
        var items = VkWallCollector.ParseResponse(json, out var total);

        Assert.Equal(2, total);
        Assert.Equal("https://vk.com/wall-217666653_10", items[0].Url);
        Assert.Equal("Кассация отменила приговор", items[0].Title);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1791590400).UtcDateTime, items[0].PublishedAt);
        Assert.Single(items[0].OutLinks);
        Assert.True(items[1].IsRepost);
        Assert.Equal("Новость Верховного Суда", items[1].Title);
        Assert.Equal("https://vk.com/wall-1_55", items[1].RepostSource);
    }

    [Fact]
    public void VkError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            VkWallCollector.ParseResponse("""{"error":{"error_code":5,"error_msg":"User authorization failed"}}""", out _));
        Assert.Contains("User authorization failed", ex.Message);
    }

    private const string Para1 = "Четвертый кассационный суд отменил апелляционное определение по делу о похищении предпринимателя. Материалы вернутся в краевой суд.";
    private const string Para2 = "Жалобы защиты двух осужденных рассмотрела судебная коллегия по уголовным делам. Адвокаты добивались отмены приговора.";
    private const string Footer = "© АО «Издательский дом». 127006, Москва, переулок д. 41. Сетевое издание зарегистрировано Федеральной службой по надзору в сфере связи.";

    [Fact]
    public void MainTextWhenEachParagraphIsWrappedInBlocks()
    {
        // как у «Коммерсанта»: каждый абзац — div в трёх своих обёртках, подвал длиннее любого абзаца
        static string Block(string p) =>
            $"""<div class="text-block"><div class="block"><div class="paragraphs"><div class="paragraph">{p}</div></div></div></div>""";
        var doc = HtmlFetcher.Parse($"""
            <html><body>
              <div class="article">{Block(Para1)}{Block(Para2)}</div>
              <div class="footer-info"><div class="copy">{Footer}</div></div>
            </body></html>
            """);
        var meta = PageMetadataService.Parse(doc);
        Assert.Equal(Para1 + "\n\n" + Para2, meta.Text);
    }

    [Fact]
    public void MainTextIgnoresNewsFeedLinks()
    {
        // как у Business FM: боковая лента новостей из <a><p>…</p></a>
        var feed = string.Concat(Enumerable.Range(1, 30).Select(i =>
            $"""<div class="py-2"><a href="/news/{i}"><p><span>09:{i:00}</span> <span>Новость номер {i} о событиях в регионе за день.</span></p></a></div>"""));
        var doc = HtmlFetcher.Parse($"""
            <html><body>
              <div class="publication"><div class="feed">{feed}</div>
              <div class="article_text"><p>{Para1}</p><p>{Para2}</p></div></div>
            </body></html>
            """);
        var meta = PageMetadataService.Parse(doc);
        Assert.Equal(Para1 + "\n\n" + Para2, meta.Text);
    }

    [Fact]
    public void GenericMetadata()
    {
        var doc = Press.Web.Services.HtmlFetcher.Parse("""
            <html><head>
              <meta property="og:title" content="Суд вернул дело на пересмотр">
              <meta property="article:published_time" content="2026-10-09T12:30:00+03:00">
            </head><body><article><p>Первый абзац.</p><p>Второй абзац.</p></article></body></html>
            """);
        var meta = PageMetadataService.Parse(doc);
        Assert.Equal("Суд вернул дело на пересмотр", meta.Title);
        Assert.Equal(new DateTime(2026, 10, 9, 9, 30, 0, DateTimeKind.Utc), meta.PublishedAt);
        Assert.Equal("Первый абзац.\n\nВторой абзац.", meta.Text);
    }
}
