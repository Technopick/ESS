using System.Globalization;
using System.Text;
using ClosedXML.Excel;
using MigraDoc.DocumentObjectModel;
using MigraDoc.DocumentObjectModel.Tables;
using MigraDoc.Rendering;
using PdfSharp.Fonts;

namespace Ess.Api.Reporting;

public enum ExportFormat { Csv, Xlsx, Pdf }

/// <summary>Writes a <see cref="ReportTable"/> as CSV, Excel or PDF. The three formats carry the same rows, filters and generation stamp.</summary>
public static class ReportExporter
{
    private static readonly CultureInfo Ke = CultureInfo.GetCultureInfo("en-KE");

    public static (byte[] Content, string ContentType, string Extension) Export(ReportTable t, ExportFormat format, string generatedBy, DateTime generatedAtLocal) =>
        ExportMany(new[] { t }, t.Title, format, generatedBy, generatedAtLocal);

    /// <summary>Several tables in one file: one sheet each in Excel, one section each in PDF, one after the other in CSV.</summary>
    public static (byte[] Content, string ContentType, string Extension) ExportMany(IReadOnlyList<ReportTable> tables, string title, ExportFormat format, string generatedBy, DateTime generatedAtLocal) => format switch
    {
        ExportFormat.Csv => (tables.Count == 1 ? Csv(tables[0]) : tables.SelectMany((t, i) => (i == 0 ? Array.Empty<byte>() : Encoding.UTF8.GetBytes($"\r\n{Cell(t.Title)}\r\n"))
            .Concat(i == 0 ? Csv(t) : Csv(t).Skip(Encoding.UTF8.GetPreamble().Length))).ToArray(), "text/csv; charset=utf-8", "csv"),
        ExportFormat.Xlsx => (Xlsx(tables, generatedBy, generatedAtLocal), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "xlsx"),
        ExportFormat.Pdf => (Pdf(tables, title, generatedBy, generatedAtLocal), "application/pdf", "pdf"),
        _ => throw new ArgumentOutOfRangeException(nameof(format))
    };

    public static string Display(object? v, ReportColumnType type) => v switch
    {
        null => "",
        DateOnly d => d.ToString("d MMM yyyy", Ke),
        DateTime dt => dt.ToString(type == ReportColumnType.Date ? "d MMM yyyy" : "d MMM yyyy HH:mm", Ke),
        decimal m when type == ReportColumnType.Money => m.ToString("N2", Ke),
        decimal p when type == ReportColumnType.Percent => (p / 100m).ToString("P1", Ke),
        decimal x => x.ToString("0.##", Ke),
        double x => x.ToString("0.##", Ke),
        int i => i.ToString("N0", Ke),
        long l => l.ToString("N0", Ke),
        bool b => b ? "Yes" : "No",
        _ => v.ToString() ?? ""
    };

    // ------------------------------------------------------------------ CSV

    public static byte[] Csv(ReportTable t)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', t.Columns.Select(c => Cell(c.Label))));
        foreach (var row in t.Rows.Concat(t.Totals is null ? Array.Empty<object?[]>() : new[] { t.Totals }))
            sb.AppendLine(string.Join(',', t.Columns.Select((c, i) => Cell(Raw(row[i], c.Type)))));
        return Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(sb.ToString())).ToArray();
    }

    private static string Raw(object? v, ReportColumnType type) => v switch
    {
        null => "",
        DateOnly d => d.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
        DateTime dt => dt.ToString(type == ReportColumnType.Date ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        bool b => b ? "Yes" : "No",
        _ => v.ToString() ?? ""
    };

    /// <summary>Quotes a CSV cell and neutralises spreadsheet formulas (CSV injection), since names and comments are user-entered.</summary>
    public static string Cell(string s)
    {
        if (s.Length > 0 && "=+-@\t\r".Contains(s[0]) && !decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out _)) s = "'" + s;
        return s.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0 || s.StartsWith('\'') ? $"\"{s.Replace("\"", "\"\"")}\"" : s;
    }

    // ------------------------------------------------------------------ Excel

    private static byte[] Xlsx(IReadOnlyList<ReportTable> tables, string generatedBy, DateTime at)
    {
        using var wb = new XLWorkbook();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var t in tables)
        {
            var name = SheetName(t.Title);
            for (var n = 2; !used.Add(name); n++) name = SheetName(t.Title)[..Math.Min(28, SheetName(t.Title).Length)] + " " + n;
            Sheet(wb.Worksheets.Add(name), t, generatedBy, at);
        }
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return ms.ToArray();
    }

    private static void Sheet(IXLWorksheet ws, ReportTable t, string generatedBy, DateTime at)
    {
        ws.Cell(1, 1).Value = t.Title;
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 14;
        var r = 2;
        foreach (var f in t.Filters) { ws.Cell(r, 1).Value = f.Key; ws.Cell(r, 2).Value = f.Value; r++; }
        ws.Cell(r, 1).Value = "Generated"; ws.Cell(r, 2).Value = $"{at:d MMM yyyy HH:mm} by {generatedBy}"; r++;
        if (t.Note is not null) { ws.Cell(r, 1).Value = t.Note; ws.Cell(r, 1).Style.Font.Italic = true; r++; }
        r++;
        var header = r;
        for (var c = 0; c < t.Columns.Count; c++) ws.Cell(r, c + 1).Value = t.Columns[c].Label;
        foreach (var row in t.Rows) { r++; Write(ws, r, t, row); }
        if (t.Totals is not null) { r++; Write(ws, r, t, t.Totals); ws.Row(r).Style.Font.Bold = true; }
        var range = ws.Range(header, 1, Math.Max(r, header), t.Columns.Count);
        if (t.Rows.Count > 0) range.CreateTable().Theme = XLTableTheme.TableStyleLight9;
        else ws.Row(header).Style.Font.Bold = true;
        ws.SheetView.FreezeRows(header);
        ws.Columns().AdjustToContents(header, Math.Min(r, header + 500), 8, 60);
    }

    private static void Write(IXLWorksheet ws, int r, ReportTable t, object?[] row)
    {
        for (var c = 0; c < t.Columns.Count; c++)
        {
            var cell = ws.Cell(r, c + 1);
            var type = t.Columns[c].Type;
            switch (row[c])
            {
                case null: break;
                case DateOnly d: cell.Value = d.ToDateTime(TimeOnly.MinValue); cell.Style.DateFormat.Format = "d mmm yyyy"; break;
                case DateTime dt: cell.Value = dt; cell.Style.DateFormat.Format = type == ReportColumnType.Date ? "d mmm yyyy" : "d mmm yyyy hh:mm"; break;
                case decimal m: cell.Value = type == ReportColumnType.Percent ? m / 100m : m; cell.Style.NumberFormat.Format = type switch { ReportColumnType.Money => "#,##0.00", ReportColumnType.Percent => "0.0%", _ => "0.##" }; break;
                case int i: cell.Value = i; break;
                case long l: cell.Value = l; break;
                case double x: cell.Value = x; break;
                case bool b: cell.Value = b ? "Yes" : "No"; break;
                default: cell.Value = row[c]!.ToString(); break; // stored as text: never evaluated as a formula
            }
        }
    }

    private static string SheetName(string title)
    {
        var s = new string(title.Where(ch => !"[]:*?/\\".Contains(ch)).ToArray());
        return s.Length > 31 ? s[..31] : s.Length == 0 ? "Report" : s;
    }

    // ------------------------------------------------------------------ PDF

    private static byte[] Pdf(IReadOnlyList<ReportTable> tables, string title, string generatedBy, DateTime at)
    {
        PortalFontResolver.EnsureRegistered();
        var doc = new Document();
        doc.Info.Title = title;
        var normal = doc.Styles[StyleNames.Normal]!;
        normal.Font.Name = PortalFontResolver.Family;
        normal.Font.Size = 8;
        foreach (var t in tables) PdfSection(doc, t, generatedBy, at);
        var renderer = new PdfDocumentRenderer { Document = doc };
        renderer.RenderDocument();
        using var ms = new MemoryStream();
        renderer.PdfDocument.Save(ms, false);
        return ms.ToArray();
    }

    private static void PdfSection(Document doc, ReportTable t, string generatedBy, DateTime at)
    {
        var section = doc.AddSection();
        section.PageSetup = doc.DefaultPageSetup.Clone();
        section.PageSetup.PageFormat = PageFormat.A4;
        section.PageSetup.Orientation = t.Columns.Count > 5 ? Orientation.Landscape : Orientation.Portrait;
        section.PageSetup.LeftMargin = section.PageSetup.RightMargin = Unit.FromCentimeter(1.5);
        section.PageSetup.TopMargin = section.PageSetup.BottomMargin = Unit.FromCentimeter(1.5);

        var title = section.AddParagraph(t.Title);
        title.Format.Font.Size = 14;
        title.Format.Font.Bold = true;
        title.Format.SpaceAfter = Unit.FromPoint(4);
        foreach (var f in t.Filters) section.AddParagraph($"{f.Key}: {f.Value}");
        section.AddParagraph($"Generated {at:d MMM yyyy HH:mm} by {generatedBy}").Format.Font.Color = Colors.Gray;
        if (t.Note is not null) section.AddParagraph(t.Note).Format.Font.Italic = true;
        section.AddParagraph().Format.SpaceAfter = Unit.FromPoint(4);

        var pageWidth = (section.PageSetup.Orientation == Orientation.Landscape ? 29.7 : 21.0) - 3.0;
        var table = section.AddTable();
        table.Borders.Width = 0.25;
        table.Borders.Color = Colors.LightGray;
        table.Format.Font.Size = t.Columns.Count > 9 ? 6.5 : 7.5;
        var weights = t.Columns.Select(c => c.Type is ReportColumnType.Text ? 2.0 : 1.0).ToArray();
        foreach (var w in weights) table.AddColumn(Unit.FromCentimeter(pageWidth * w / weights.Sum()));
        var head = table.AddRow();
        head.HeadingFormat = true;
        head.Shading.Color = new Color(0xEE, 0xF1, 0xF5);
        head.Format.Font.Bold = true;
        for (var c = 0; c < t.Columns.Count; c++) Fill(head.Cells[c], t.Columns[c].Label, t.Columns[c].Type);
        foreach (var row in t.Rows)
        {
            var tr = table.AddRow();
            for (var c = 0; c < t.Columns.Count; c++) Fill(tr.Cells[c], Display(row[c], t.Columns[c].Type), t.Columns[c].Type);
        }
        if (t.Totals is not null)
        {
            var tr = table.AddRow();
            tr.Format.Font.Bold = true;
            for (var c = 0; c < t.Columns.Count; c++) Fill(tr.Cells[c], Display(t.Totals[c], t.Columns[c].Type), t.Columns[c].Type);
        }
        if (t.Rows.Count == 0) section.AddParagraph("No records match these filters.");

        var footer = section.Footers.Primary.AddParagraph();
        footer.Format.Font.Size = 7;
        footer.Format.Font.Color = Colors.Gray;
        footer.AddText("Confidential. Page ");
        footer.AddPageField();
        footer.AddText(" of ");
        footer.AddNumPagesField();
    }

    private static void Fill(Cell cell, string text, ReportColumnType type)
    {
        cell.AddParagraph(text);
        cell.Format.Alignment = type is ReportColumnType.Text or ReportColumnType.Date or ReportColumnType.DateTime ? ParagraphAlignment.Left : ParagraphAlignment.Right;
    }
}

/// <summary>
/// PDFsharp needs font files on Linux and in containers. Looks for DejaVu Sans, Liberation Sans or Arial in the usual
/// system folders (install fonts-dejavu-core in the Docker image); Reporting:FontPath can point at another TrueType file.
/// </summary>
public sealed class PortalFontResolver : IFontResolver
{
    public const string Family = "PortalSans";
    private static readonly object Gate = new();
    private static bool _registered;
    private readonly Dictionary<string, byte[]> _faces = new();

    public static string? ConfiguredRegularPath { get; set; }
    public static string? ConfiguredBoldPath { get; set; }

    public static void EnsureRegistered()
    {
        if (_registered) return;
        lock (Gate)
        {
            if (_registered) return;
            if (GlobalFontSettings.FontResolver is null) GlobalFontSettings.FontResolver = new PortalFontResolver();
            _registered = true;
        }
    }

    private static readonly string[] Dirs =
    {
        "/usr/share/fonts/truetype/dejavu", "/usr/share/fonts/dejavu", "/usr/share/fonts/TTF", "/usr/share/fonts/truetype/liberation",
        "/usr/share/fonts/liberation", "/usr/share/fonts/truetype/liberation2", Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "/Library/Fonts", "/System/Library/Fonts/Supplemental"
    };
    private static readonly (string Regular, string Bold, string Italic, string BoldItalic)[] Candidates =
    {
        ("DejaVuSans.ttf", "DejaVuSans-Bold.ttf", "DejaVuSans-Oblique.ttf", "DejaVuSans-BoldOblique.ttf"),
        ("LiberationSans-Regular.ttf", "LiberationSans-Bold.ttf", "LiberationSans-Italic.ttf", "LiberationSans-BoldItalic.ttf"),
        ("arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf"),
        ("Arial.ttf", "Arial Bold.ttf", "Arial Italic.ttf", "Arial Bold Italic.ttf"),
    };

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(bold && italic ? "bi" : bold ? "b" : italic ? "i" : "r");

    public byte[]? GetFont(string faceName)
    {
        lock (_faces)
        {
            if (_faces.TryGetValue(faceName, out var cached)) return cached;
            var bytes = Load(faceName) ?? Load("r") ?? throw new InvalidOperationException(
                "No TrueType font found for PDF export. Install DejaVu or Liberation fonts, or set Reporting:FontPath.");
            _faces[faceName] = bytes;
            return bytes;
        }
    }

    private static byte[]? Load(string face)
    {
        var configured = face is "b" or "bi" ? ConfiguredBoldPath ?? ConfiguredRegularPath : ConfiguredRegularPath;
        if (configured is not null && File.Exists(configured)) return File.ReadAllBytes(configured);
        foreach (var dir in Dirs.Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d)))
            foreach (var c in Candidates)
            {
                var file = Path.Combine(dir, face switch { "b" => c.Bold, "i" => c.Italic, "bi" => c.BoldItalic, _ => c.Regular });
                if (File.Exists(file)) return File.ReadAllBytes(file);
            }
        return null;
    }
}
