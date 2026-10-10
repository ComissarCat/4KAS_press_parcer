using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Press.Web.Collectors;
using Press.Web.Data;
using Press.Web.Domain;
using Press.Web.Services;

namespace Press.Tests;

public sealed class IngestionTests : IDisposable
{
    private const string SiteUrl = "https://4kas.sudrf.ru/modules.php?name=press_dep&op=1&did=420";
    private readonly SqliteConnection _conn = new("Data Source=:memory:");
    private readonly TestDbFactory _factory;
    private readonly Resource _site, _tg;

    public IngestionTests()
    {
        _conn.Open();
        _factory = new TestDbFactory(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_conn).Options);
        using var db = _factory.CreateDbContext();
        db.Database.EnsureCreated();
        var news = new MaterialType { Name = "Новость" };
        _site = new Resource { Name = "Сайт", Kind = ResourceKind.Website, IsOwn = true, UrlPrefix = "4kas.sudrf.ru", DefaultMaterialType = news };
        _tg = new Resource { Name = "TG", Kind = ResourceKind.Telegram, IsOwn = true, UrlPrefix = "t.me/sud_kas_4/" };
        db.Resources.AddRange(_site, _tg);
        db.SaveChanges();
    }

    public void Dispose() => _conn.Dispose();

    private static CollectedItem SiteItem() => new()
    {
        Url = SiteUrl,
        Title = "Четвертый кассационный суд вернул на пересмотр уголовное дело о похищении кубанского предпринимателя",
        Text = "Полный текст",
        PublishedAt = new DateTime(2026, 10, 9, 6, 0, 0, DateTimeKind.Utc),
    };

    private static CollectedItem TgItem() => new()
    {
        Url = "https://t.me/sud_kas_4/891",
        Title = "Четвертый кассационный суд вернул на пересмотр уголовное дело",
        Text = "Короткий текст",
        PublishedAt = new DateTime(2026, 10, 9, 6, 53, 0, DateTimeKind.Utc),
        OutLinks = ["https://4kas.sudrf.ru/modules.php?name=press_dep&amp;amp;op=1&amp;amp;did=420"],
    };

    private static CollectedItem UnrelatedTgItem() => TgItem() with
    {
        Title = "1 октября суд отмечает годовщину начала работы",
        OutLinks = [],
    };

    private async Task<CollectionService.IngestOutcome> Ingest(Resource r, CollectedItem item)
    {
        await using var db = _factory.CreateDbContext();
        var resource = await db.Resources.Include(x => x.DefaultMaterialType).FirstAsync(x => x.Id == r.Id);
        return await CollectionService.IngestAsync(db, resource, item, CancellationToken.None);
    }

    [Fact]
    public async Task TelegramPostAttachesToSiteMaterial()
    {
        Assert.Equal(CollectionService.IngestOutcome.Created, await Ingest(_site, SiteItem()));
        Assert.Equal(CollectionService.IngestOutcome.Attached, await Ingest(_tg, TgItem()));
        Assert.Equal(CollectionService.IngestOutcome.Skipped, await Ingest(_tg, TgItem()));

        await using var db = _factory.CreateDbContext();
        var m = await db.Materials.Include(x => x.Links).Include(x => x.Types).SingleAsync();
        Assert.Equal(2, m.Links.Count);
        Assert.Equal(SiteItem().Title, m.Title);
        Assert.Equal("Новость", Assert.Single(m.Types).Name);
        Assert.True(m.IsOwn);
    }

    [Fact]
    public async Task SitePostArrivingLaterTakesOverTitle()
    {
        Assert.Equal(CollectionService.IngestOutcome.Created, await Ingest(_tg, TgItem()));
        Assert.Equal(CollectionService.IngestOutcome.Attached, await Ingest(_site, SiteItem()));

        await using var db = _factory.CreateDbContext();
        var m = await db.Materials.Include(x => x.Links).SingleAsync();
        Assert.Equal(2, m.Links.Count);
        Assert.Equal(SiteItem().Title, m.Title);
        Assert.Equal(SiteItem().PublishedAt, m.PublishedAt);
    }

    [Fact]
    public async Task PostsSharingSourceLinkAreOneMaterial()
    {
        // перепечатка новости ВС на сайте и пост о ней в Telegram ссылаются на одну новость vsrf.ru
        var site = SiteItem() with { OutLinks = ["https://www.vsrf.ru/news/37888"], IsRepost = true };
        var tg = TgItem() with { OutLinks = ["https://vsrf.ru/news/37888", "https://t.me/sud_kas_4"] };
        var other = TgItem() with
        {
            Url = "https://t.me/sud_kas_4/999",
            OutLinks = ["https://t.me/sud_kas_4"], // ссылка на сам канал не считается общей
        };
        var late = TgItem() with
        {
            Url = "https://t.me/sud_kas_4/1000",
            PublishedAt = TgItem().PublishedAt!.Value.AddDays(30), // слишком поздно
            OutLinks = ["https://vsrf.ru/news/37888"],
        };

        Assert.Equal(CollectionService.IngestOutcome.Created, await Ingest(_site, site));
        Assert.Equal(CollectionService.IngestOutcome.Attached, await Ingest(_tg, tg));
        Assert.Equal(CollectionService.IngestOutcome.Created, await Ingest(_tg, other));
        Assert.Equal(CollectionService.IngestOutcome.Created, await Ingest(_tg, late));

        await using var db = _factory.CreateDbContext();
        Assert.Equal(3, await db.Materials.CountAsync());
        Assert.True(await db.Materials.Where(m => m.Links.Count == 2).Select(m => m.IsRepost).SingleAsync());
    }

    [Fact]
    public async Task LinkToRepostSourceMarksRepost()
    {
        var item = TgItem() with { OutLinks = ["https://t.me/sovetsudey/1407"] };
        await using var db = _factory.CreateDbContext();
        var tg = await db.Resources.FirstAsync(r => r.Id == _tg.Id);
        string[] sources = ["t.me/sovetsudey/", "vsrf.ru"];
        await CollectionService.IngestAsync(db, tg, item, CancellationToken.None, sources);

        var m = await db.Materials.SingleAsync();
        Assert.True(m.IsRepost);
        Assert.Equal("Источник: https://t.me/sovetsudey/1407", m.Notes);
    }

    [Fact]
    public async Task SimilarTitleIsOneMaterial()
    {
        var site = SiteItem() with
        {
            Title = "Председатель Верховного Суда обозначил роль судебного диалога в сближении правоприменительных подходов России и Казахстана",
        };
        var tg = TgItem() with
        {
            Title = "Председатель Верховного Суда РФ обозначил роль судебного диалога в сближении правоприменительных подходов России и Казахстана",
            OutLinks = ["https://t.me/sovetsudey/1398"],
        };
        // другой пост того же канала с похожим заголовком — отдельный материал
        var tg2 = tg with { Url = "https://t.me/sud_kas_4/1001", OutLinks = [] };

        await Ingest(_site, site);
        Assert.Equal(CollectionService.IngestOutcome.Attached, await Ingest(_tg, tg));
        Assert.Equal(CollectionService.IngestOutcome.Created, await Ingest(_tg, tg2));
    }

    [Theory]
    [InlineData("Кассационный суд подтвердил законность возврата сельскохозяйственных земель",
        "Кассационный суд подтвердил законность возврата краснодарских сельскохозяйственных земель", true)]
    [InlineData("Четвертый кассационный суд вернул на пересмотр уголовное дело о похищении предпринимателя",
        "Четвертый кассационный суд оставил в силе приговор по делу о взятке", false)]
    public void TitleSimilarityThreshold(string a, string b, bool same) =>
        Assert.Equal(same, TitleSimilarity.Score(a, b) >= CollectionService.SameTitleThreshold);

    [Fact]
    public async Task UnrelatedPostsBecomeSeparateMaterials()
    {
        await Ingest(_site, SiteItem());
        await Ingest(_tg, UnrelatedTgItem());
        await using var db = _factory.CreateDbContext();
        Assert.Equal(2, await db.Materials.CountAsync());
    }

    [Fact]
    public async Task MergeAndDerived()
    {
        await Ingest(_site, SiteItem());
        await Ingest(_tg, UnrelatedTgItem());
        var svc = new MaterialService(_factory);
        int siteId, tgId;
        await using (var db = _factory.CreateDbContext())
        {
            siteId = await db.MaterialLinks.Where(l => l.Url == SiteUrl).Select(l => l.MaterialId).SingleAsync();
            tgId = await db.Materials.Where(m => m.Id != siteId).Select(m => m.Id).SingleAsync();
        }

        Assert.Null(await svc.AddDerivedAsync(siteId,
            new LinkInput("https://www.kuban.kp.ru/online/news/123/?utm_source=tg", "Суд вернул дело", null, null), [], "тест"));
        // та же перепечатка повторно — не дублируется
        Assert.Null(await svc.AddDerivedAsync(siteId, new LinkInput("https://kuban.kp.ru/online/news/123", null, null, null), [], "тест"));
        Assert.Null(await svc.MergeAsync(siteId, tgId));

        await using (var db = _factory.CreateDbContext())
        {
            var own = await db.Materials.Include(m => m.Links).Include(m => m.Derived).SingleAsync(m => m.IsOwn);
            Assert.Equal(siteId, own.Id);
            Assert.Equal(2, own.Links.Count);
            var rel = Assert.Single(own.Derived);
            Assert.Equal(RelationStatus.Confirmed, rel.Status);
            var res = await db.Resources.SingleAsync(r => r.UrlPrefix == "kuban.kp.ru");
            Assert.False(res.IsOwn);
        }

        // удаление собственного материала удаляет и «осиротевшую» перепечатку
        await svc.DeleteAsync(siteId);
        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(0, await db.Materials.CountAsync());
            Assert.Equal(0, await db.MaterialLinks.CountAsync());
        }
    }

    [Fact]
    public async Task ConfirmMonitoredItem()
    {
        await Ingest(_site, SiteItem());
        int ownId, itemId;
        await using (var db = _factory.CreateDbContext())
        {
            ownId = await db.Materials.Select(m => m.Id).SingleAsync();
            var rapsi = new Resource { Name = "РАПСИ", Kind = ResourceKind.Website, UrlPrefix = "rapsinews.ru" };
            var item = new MonitoredItem
            {
                Resource = rapsi, Url = "https://www.rapsinews.ru/judicial_news/20261009/1.html",
                UrlKey = "rapsinews.ru/judicial_news/20261009/1.html", Title = "Кассация вернула дело",
                Text = "Текст", PublishedAt = new DateTime(2026, 10, 9, 12, 0, 0, DateTimeKind.Utc),
                FoundAt = DateTime.UtcNow, Status = MonitoredItemStatus.Candidate, SuggestedMaterialId = ownId, Score = 0.65,
            };
            db.MonitoredItems.Add(item);
            await db.SaveChangesAsync();
            itemId = item.Id;
        }

        var svc = new MaterialService(_factory);
        Assert.Null(await svc.ConfirmMonitoredAsync(itemId, ownId, [], "тест"));

        int relationId;
        await using (var db = _factory.CreateDbContext())
        {
            var item = await db.MonitoredItems.SingleAsync();
            Assert.Equal(MonitoredItemStatus.Confirmed, item.Status);
            var derived = await db.Materials.Include(m => m.Links).ThenInclude(l => l.Resource).SingleAsync(m => !m.IsOwn);
            Assert.Equal(derived.Id, item.DerivedMaterialId);
            Assert.Equal("Кассация вернула дело", derived.Title);
            Assert.Equal("РАПСИ", derived.Links.Single().Resource!.Name); // ресурс найден по префиксу, а не создан заново
            var rel = await db.MaterialRelations.SingleAsync();
            Assert.Equal(0.65, rel.Score);
            relationId = rel.Id;
        }

        // удаление связи возвращает публикацию в очередь
        await svc.RemoveRelationAsync(relationId);
        await using (var db = _factory.CreateDbContext())
        {
            Assert.Equal(MonitoredItemStatus.Candidate, (await db.MonitoredItems.SingleAsync()).Status);
            Assert.False(await db.Materials.AnyAsync(m => !m.IsOwn));
        }
    }

    private sealed class TestDbFactory(DbContextOptions<AppDbContext> options) : IDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext() => new(options);
    }
}
