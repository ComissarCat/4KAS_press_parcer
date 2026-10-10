using ClosedXML.Excel;
using Microsoft.EntityFrameworkCore;
using Press.Web.Data;
using Press.Web.Domain;

namespace Press.Web.Services;

public sealed record ReportOptions(DateTime FromMsk, DateTime ToMskInclusive, bool IncludeReposts);

public sealed record ReportDerived(int Id, string Title, DateTime PublishedAt, string Resource, string Url);

public sealed record ReportMaterial(int Id, string Title, DateTime PublishedAt, bool IsRepost, IReadOnlyList<string> Types,
    IReadOnlyList<(string Resource, string Url)> Links, IReadOnlyList<ReportDerived> Derived);

public sealed record ReportCount(string Name, int Count);

public sealed record ReportData(
    ReportOptions Options,
    IReadOnlyList<ReportMaterial> Materials,
    IReadOnlyList<ReportCount> ByType,
    IReadOnlyList<ReportCount> ByOwnResource,
    IReadOnlyList<ReportCount> DerivedByResource)
{
    public int OwnCount => Materials.Count;
    public int DerivedCount => Materials.SelectMany(m => m.Derived).Select(d => d.Id).Distinct().Count();
    public int WithDerivedCount => Materials.Count(m => m.Derived.Count > 0);
}

/// <summary>
/// Отчёт за период: собственные материалы, опубликованные в периоде, и подтверждённые
/// материалы других ресурсов, сделанные на их основе (независимо от даты перепечатки).
/// </summary>
public class ReportService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<ReportData> BuildAsync(ReportOptions o, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var from = Msk.ToUtcFromMsk(o.FromMsk.Date);
        var to = Msk.ToUtcFromMsk(o.ToMskInclusive.Date.AddDays(1));

        var query = db.Materials.AsNoTracking().AsSplitQuery()
            .Where(m => m.IsOwn && m.PublishedAt >= from && m.PublishedAt < to);
        if (!o.IncludeReposts) query = query.Where(m => !m.IsRepost);

        var list = await query
            .Include(m => m.Types)
            .Include(m => m.Links).ThenInclude(l => l.Resource)
            .Include(m => m.Derived.Where(r => r.Status == RelationStatus.Confirmed))
                .ThenInclude(r => r.DerivedMaterial).ThenInclude(d => d.Links).ThenInclude(l => l.Resource)
            .OrderBy(m => m.PublishedAt)
            .ToListAsync(ct);

        var materials = list.Select(m => new ReportMaterial(
            m.Id, m.Title, m.PublishedAt, m.IsRepost,
            m.Types.OrderBy(t => t.SortOrder).Select(t => t.Name).ToList(),
            m.Links.OrderBy(l => l.PublishedAt).Select(l => (ResourceName(l), l.Url)).ToList(),
            m.Derived.Select(r => r.DerivedMaterial)
                .OrderBy(d => d.PublishedAt)
                .Select(d =>
                {
                    var link = d.Links.OrderBy(l => l.PublishedAt).FirstOrDefault();
                    return new ReportDerived(d.Id, d.Title, d.PublishedAt, link == null ? "" : ResourceName(link), link?.Url ?? "");
                }).ToList()
        )).ToList();

        var byType = materials.SelectMany(m => m.Types.Count == 0 ? ["(вид не указан)"] : m.Types)
            .GroupBy(t => t).Select(g => new ReportCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ToList();
        var byOwnResource = materials.SelectMany(m => m.Links.Select(l => l.Resource).Distinct())
            .GroupBy(r => r).Select(g => new ReportCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ToList();
        var derivedByResource = materials.SelectMany(m => m.Derived).DistinctBy(d => d.Id)
            .GroupBy(d => d.Resource).Select(g => new ReportCount(g.Key, g.Count()))
            .OrderByDescending(c => c.Count).ToList();

        return new ReportData(o, materials, byType, byOwnResource, derivedByResource);
    }

    private static string ResourceName(MaterialLink l) => l.Resource?.Name ?? UrlKey.Host(l.Url) ?? l.Url;

    public static byte[] ToExcel(ReportData r)
    {
        using var wb = new XLWorkbook();
        var period = $"{r.Options.FromMsk:dd.MM.yyyy} – {r.Options.ToMskInclusive:dd.MM.yyyy}";

        var s = wb.AddWorksheet("Сводка");
        s.Cell(1, 1).Value = $"Отчёт о публикациях пресс-службы за период {period}";
        s.Cell(1, 1).Style.Font.Bold = true;
        s.Cell(1, 1).Style.Font.FontSize = 14;
        var row = 3;
        void Pair(string name, object value)
        {
            s.Cell(row, 1).Value = name;
            s.Cell(row, 2).Value = XLCellValue.FromObject(value);
            row++;
        }
        Pair("Собственных материалов", r.OwnCount);
        Pair("Материалов других ресурсов на их основе", r.DerivedCount);
        Pair("Собственных материалов, получивших перепечатки", r.WithDerivedCount);
        if (!r.Options.IncludeReposts) Pair("Перепечатки чужих материалов", "не учитывались");
        row++;
        void Table(string title, IReadOnlyList<ReportCount> rows)
        {
            s.Cell(row, 1).Value = title;
            s.Cell(row, 1).Style.Font.Bold = true;
            row++;
            foreach (var c in rows) Pair(c.Name, c.Count);
            row++;
        }
        Table("По видам материалов", r.ByType);
        Table("По собственным ресурсам", r.ByOwnResource);
        Table("Перепечатки по ресурсам", r.DerivedByResource);
        s.Column(1).Width = 55;
        s.Column(2).Width = 15;

        var m = wb.AddWorksheet("Материалы");
        string[] headers = ["№", "Дата", "Заголовок", "Вид", "Где опубликован", "Ссылки", "Перепечаток"];
        for (var i = 0; i < headers.Length; i++) m.Cell(1, i + 1).Value = headers[i];
        row = 2;
        foreach (var x in r.Materials)
        {
            m.Cell(row, 1).Value = x.Id;
            m.Cell(row, 2).Value = x.PublishedAt.ToMsk();
            m.Cell(row, 2).Style.DateFormat.Format = "dd.MM.yyyy";
            m.Cell(row, 3).Value = x.Title + (x.IsRepost ? " (перепечатка)" : "");
            m.Cell(row, 4).Value = string.Join(", ", x.Types);
            m.Cell(row, 5).Value = string.Join(", ", x.Links.Select(l => l.Resource).Distinct());
            m.Cell(row, 6).Value = string.Join("\n", x.Links.Select(l => l.Url));
            m.Cell(row, 7).Value = x.Derived.Count;
            row++;
        }
        FormatTable(m, headers.Length, row - 1, [6, 12, 70, 18, 30, 50, 12]);

        var d = wb.AddWorksheet("Перепечатки");
        string[] dh = ["Наш материал", "Дата", "Заголовок", "Ресурс", "Ссылка"];
        for (var i = 0; i < dh.Length; i++) d.Cell(1, i + 1).Value = dh[i];
        row = 2;
        foreach (var x in r.Materials)
        foreach (var y in x.Derived)
        {
            d.Cell(row, 1).Value = x.Title;
            d.Cell(row, 2).Value = y.PublishedAt.ToMsk();
            d.Cell(row, 2).Style.DateFormat.Format = "dd.MM.yyyy";
            d.Cell(row, 3).Value = y.Title;
            d.Cell(row, 4).Value = y.Resource;
            d.Cell(row, 5).Value = y.Url;
            if (Uri.TryCreate(y.Url, UriKind.Absolute, out var uri)) d.Cell(row, 5).SetHyperlink(new XLHyperlink(uri));
            row++;
        }
        FormatTable(d, dh.Length, row - 1, [50, 12, 60, 30, 50]);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void FormatTable(IXLWorksheet ws, int cols, int lastRow, int[] widths)
    {
        var header = ws.Range(1, 1, 1, cols);
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDE6F0");
        for (var i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];
        if (lastRow >= 2)
        {
            var body = ws.Range(2, 1, lastRow, cols);
            body.Style.Alignment.WrapText = true;
            body.Style.Alignment.Vertical = XLAlignmentVerticalValues.Top;
        }
        ws.SheetView.FreezeRows(1);
    }
}
