using Press.Web.Collectors;
using Press.Web.Domain;
using Press.Web.Services;

namespace Press.Tests;

public class MonitoringTests
{
    private static Stream Sample(string name) =>
        File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Samples", name));

    [Fact]
    public void RssRapsi()
    {
        using var s = Sample("rss_rapsi.xml");
        var items = RssCollector.Parse(s);

        Assert.Equal(74, items.Count);
        var first = items[0];
        Assert.Equal("https://www.rapsinews.ru/judicial_analyst/20261010/312276235.html", first.Url);
        Assert.Equal("Конфликт у клуба, магнитные рамки и дрессировка собак: дайджест ВС РФ", first.Title);
        Assert.Equal(new DateTime(2026, 10, 10, 7, 0, 0, DateTimeKind.Utc), first.PublishedAt);
        Assert.StartsWith("В каких случаях установка магнитных рамок", first.Text);
        Assert.True(first.IsSummaryOnly);
    }

    [Fact]
    public void RssKubnewsTwoDigitYear()
    {
        using var s = Sample("rss_kubnews.xml");
        var items = RssCollector.Parse(s);

        Assert.Equal(30, items.Count);
        // «Sat, 10 Oct 26 13:00:36 +0300» — год из двух цифр
        Assert.Equal(new DateTime(2026, 10, 10, 10, 0, 36, DateTimeKind.Utc), items[0].PublishedAt);
        Assert.All(items, i => Assert.StartsWith("https://kubnews.ru/", i.Url));
    }

    [Theory]
    [InlineData("Sat, 10 Oct 2026 10:00:00 +0300", "2026-10-10T07:00:00Z")]
    [InlineData("Sat, 10 Oct 26 13:00:36 +0300", "2026-10-10T10:00:36Z")]
    [InlineData("Fri, 9 Oct 2026 19:05:27 GMT", "2026-10-09T19:05:27Z")]
    [InlineData("2026-10-09T12:00:00+03:00", "2026-10-09T09:00:00Z")]
    public void RssDates(string input, string expectedUtc) =>
        Assert.Equal(DateTime.Parse(expectedUtc).ToUniversalTime(), RssCollector.ParseDate(input));

    [Fact]
    public void HtmlListBfm()
    {
        var doc = Samples.Load("bfm_list.html", "https://kuban.bfm.ru/");
        var items = HtmlListCollector.ParseList(doc, "https://kuban.bfm.ru/", "a[href*='/news/']");

        Assert.NotEmpty(items);
        // ссылки на федеральный www.bfm.ru не относятся к региональной ленте
        Assert.All(items, i => Assert.Equal("kuban.bfm.ru", UrlKey.Host(i.Url)));
        Assert.Contains(items, i => i.Url == "https://kuban.bfm.ru/news/63286");
        Assert.Equal(items.Count, items.Select(i => UrlKey.Create(i.Url)).Distinct().Count());
    }

    [Theory]
    [InlineData("Решение Четвертого кассационного суда общей юрисдикции вступило в силу", "Четвертый кассационный суд")]
    [InlineData("Как сообщили в 4КСОЮ, жалоба отклонена", "4КСОЮ")]
    [InlineData("Третий кассационный суд оставил приговор в силе", null)]
    public void Keywords(string text, string? expected) =>
        Assert.Equal(expected, new KeywordMatcher(KeywordMatcher.Defaults).FindIn(text));

    private const string OwnText = """
        Четвертый кассационный суд вернул на пересмотр уголовное дело о похищении кубанского предпринимателя.
        В ноябре 2023 года в Армавире группа мужчин заблокировала автомобиль предпринимателя и, угрожая ножом,
        вывезла за пределы города. Его избили, отобрали телефоны, ноутбук и паспорт, а затем потребовали 12 млн рублей
        в счёт долга в 3 млн рублей. Армавирский городской суд признал подсудимых виновными в похищении человека,
        самоуправстве и разбое и назначил им 6 лет колонии строгого режима.
        """;

    private const string RewriteText = """
        Кассация повторно вернула в апелляцию дело о похищении кубанского бизнесмена. По данным суда, в ноябре 2023 года
        в Армавире злоумышленники заблокировали машину бизнесмена, угрожая ножом, вывезли его за город, избили,
        забрали ноутбук, паспорт и телефоны и потребовали 12 млн рублей при долге в 3 млн рублей.
        Армавирский суд назначил фигурантам по 6 лет колонии.
        """;

    private const string UnrelatedText = """
        Краснодарский краевой суд признал законным приговор организатору джип-тура, получившему 3 года колонии
        за гибель туристов во время экскурсии на автомобиле. Суд отклонил жалобу защиты.
        """;

    private static MaterialIndex Index(params string[] background)
    {
        var own = new IndexedMaterial(1, "Четвертый кассационный суд вернул на пересмотр уголовное дело",
            new DateTime(2026, 10, 9, 6, 0, 0, DateTimeKind.Utc), Stemmer.Set(OwnText),
            ["4kas.sudrf.ru/modules.php?did=420&name=press_dep&op=1"]);
        var other = new IndexedMaterial(2, "Суд взыскал с муниципалитета компенсацию за пляж в Судаке",
            new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc),
            Stemmer.Set("Кассационный суд подтвердил обязанность муниципалитета обеспечить пляж в Судаке спасателями"), []);
        return new MaterialIndex([own, other], background.Select(Stemmer.Set));
    }

    private static readonly string[] Background =
    [
        "Суд в Краснодаре признал виновным водителя в ДТП, приговор вступил в силу",
        "В Армавире открыли новую школу, губернатор посетил церемонию",
        "Кассационный суд оставил в силе решение по делу о банкротстве",
        "Краснодарский краевой суд отклонил жалобу на приговор",
        "Полиция Кубани задержала мужчину, угрожавшего ножом прохожему",
    ];

    [Fact]
    public void RewriteScoresHigh()
    {
        var m = Index(Background).Best("Кассация вернула в апелляцию дело о похищении кубанского бизнесмена", RewriteText,
            new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc));
        Assert.NotNull(m);
        Assert.Equal(1, m.Material.Id);
        Assert.True(m.Score >= new MonitoringOptions().CandidateThreshold, $"score {m.Score}");
    }

    [Fact]
    public void UnrelatedScoresLow()
    {
        var m = Index(Background).Best("Организаторы джип-тура не смогли оспорить приговор", UnrelatedText,
            new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc));
        Assert.True(m == null || m.Score < new MonitoringOptions().CandidateThreshold, $"score {m?.Score}");
    }

    [Fact]
    public void PublicationBeforeOwnMaterialIsIgnored()
    {
        var m = Index(Background).Best("Кассация вернула дело", RewriteText, new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc));
        Assert.True(m == null || m.Material.Id != 1);
    }

    [Fact]
    public void LinkToOwnMaterialIsCertain()
    {
        var m = Index().Best("Новость", "коротко", null,
            [UrlKey.Create("https://4kas.sudrf.ru/modules.php?name=press_dep&op=1&did=420")]);
        Assert.Equal(1.0, m!.Score);
    }

    [Fact]
    public void EvaluateAssignsStatuses()
    {
        var index = Index(Background);
        var keywords = new KeywordMatcher(KeywordMatcher.Defaults);
        var opt = new MonitoringOptions();
        string[] own = ["4kas.sudrf.ru", "t.me/sud_kas_4/"];

        var rewrite = new MonitoredItem { Title = "Кассация вернула дело о похищении бизнесмена", Text = RewriteText,
            PublishedAt = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc) };
        MonitoringService.Evaluate(rewrite, [], index, keywords, own, opt);
        Assert.Equal(MonitoredItemStatus.Candidate, rewrite.Status);
        Assert.Equal(1, rewrite.SuggestedMaterialId);

        var mention = new MonitoredItem { Title = "Обзор недели", Text = "Четвертый кассационный суд рассмотрел 500 жалоб" };
        MonitoringService.Evaluate(mention, [], index, keywords, own, opt);
        Assert.Equal(MonitoredItemStatus.Mention, mention.Status);

        var linksToCaseCard = new MonitoredItem { Title = "Суд рассмотрит дело", Text = "Подробности" };
        MonitoringService.Evaluate(linksToCaseCard, ["https://4kas.sudrf.ru/modules.php?name=sud_delo&case_id=1"], index, keywords, own, opt);
        Assert.Equal(MonitoredItemStatus.Mention, linksToCaseCard.Status);

        var unrelated = new MonitoredItem { Title = "Джип-тур", Text = UnrelatedText };
        MonitoringService.Evaluate(unrelated, [], index, keywords, own, opt);
        Assert.Equal(MonitoredItemStatus.Irrelevant, unrelated.Status);
        Assert.Null(unrelated.SuggestedMaterialId);
    }
}
