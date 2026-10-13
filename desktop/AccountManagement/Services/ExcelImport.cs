using System.Globalization;
using System.IO;
using System.IO.Compression;
using ClosedXML.Excel;
using AccountManagement.Models;

namespace AccountManagement.Services;

public static class ExcelImport
{
    internal static readonly string[] Headers = ["平台名称", "平台地址", "帐号名称", "登录帐号", "密码", "备注", "标签"];
    public static void SaveTemplate(string path)
    {
        using var book = new XLWorkbook();
        var sheet = book.AddWorksheet("帐号导入");
        for (var i = 0; i < Headers.Length; i++) sheet.Cell(1, i + 1).Value = Headers[i];
        sheet.Range("A1:G1").Style.Fill.BackgroundColor = XLColor.FromHtml("#1765A6");
        sheet.Range("A1:G1").Style.Font.FontColor = XLColor.White;
        sheet.Range("A1:G1").Style.Font.Bold = true;
        sheet.Columns(1, 7).Width = 24;
        sheet.Range("A2:G1001").Style.NumberFormat.Format = "@";
        sheet.SheetView.FreezeRows(1);
        var help = book.AddWorksheet("填写说明");
        string[] notes = ["在第一个工作表填写数据，首行标题不要更改；最多 1000 条。",
            "平台名称和登录帐号必填；其他字段可空，密码允许为空。",
            "帐号、密码按文本填写，保留前导零。Excel 已丢失的前导零或长数字无法恢复。请勿使用公式。",
            "同平台、同登录帐号、同帐号名称重复时跳过，不覆盖密码、备注或标签。",
            "已有平台保留原地址；不存在的平台按本行地址创建，同名平台后续行复用。",
            "标签用中文分号分隔，如：测试帐号；测试平台。已有标签复用原色，新标签默认蓝色。",
            "标签每个最多 40 字符、每帐号最多 20 个；平台和帐号名称最多 120 字符。",
            "登录帐号最多 160 字符、密码最多 1024 字符、备注最多 2000 字符、地址最多 1000 字符。",
            "文件含明文密码，导入后请妥善保管或删除；导入不会自动分享帐号。"];
        for (var i = 0; i < notes.Length; i++) help.Cell(i + 1, 1).Value = notes[i];
        help.Column(1).Width = 100; help.Column(1).Style.Alignment.WrapText = true;
        book.SaveAs(path);
    }

    public static List<ImportRow> Read(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".xlsx", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException("只支持 .xlsx 文件，请使用导入模板。");
        using var input = File.OpenRead(path);
        if (input.Length > 10 * 1024 * 1024) throw new ArgumentException("Excel 文件不能超过 10 MB。");
        using (var zip = new ZipArchive(input, ZipArchiveMode.Read, true))
            if (zip.Entries.Count > 2000 || zip.Entries.Sum(e => e.Length) > 64 * 1024 * 1024)
                throw new ArgumentException("Excel 文件解压后过大，请只保留需要导入的数据。");
        input.Position = 0;
        using var book = new XLWorkbook(input);
        var sheet = book.Worksheet(1);
        for (var i = 0; i < Headers.Length; i++)
            if (sheet.Cell(1, i + 1).HasFormula || sheet.Cell(1, i + 1).GetString().Trim() != Headers[i])
                throw new ArgumentException("首行列标题与模板不一致，请下载模板后填写。");
        var last = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 1;
        if (last > 1001) throw new ArgumentException("第一版最多导入 1000 行，请拆分文件。");
        List<ImportRow> rows = [];
        HashSet<(string, string, string)> seen = [];
        for (var n = 2; n <= last; n++)
        {
            var cells = Enumerable.Range(1, 7).Select(i => sheet.Cell(n, i)).ToArray();
            if (cells.All(c => c.IsEmpty())) continue;
            bool invalidCell = cells.Any(c => c.HasFormula || c.DataType == XLDataType.Error);
            var values = cells.Select(c => c.HasFormula || c.DataType == XLDataType.Error ? "" : c.GetFormattedString(CultureInfo.InvariantCulture)).ToArray();
            var row = new ImportRow { RowNumber = n, Platform = values[0].Trim(), Url = values[1].Trim(), Name = values[2],
                Login = values[3].Trim(), Password = values[4], PasswordSummary = values[4].Length == 0 ? "空密码" : "已填写", Notes = values[5], Tags = values[6] };
            row.Detail = invalidCell ? "包含公式或错误单元格，请改为文本值。" : Validate(row);
            row.Valid = row.Detail.Length == 0;
            if (!row.Valid) row.State = "校验失败";
            else if (!seen.Add((row.Platform, row.Login, row.Name))) { row.State = "待查重"; row.Detail = "文件中有相同记录，导入时由服务器确认并跳过重复。"; }
            rows.Add(row);
        }
        if (rows.Count == 0) throw new ArgumentException("文件中没有可预览的数据，请从第 2 行填写。");
        return rows;
    }

    private static string Validate(ImportRow row)
    {
        if (row.Platform.Length == 0 || row.Login.Length == 0) return "平台名称和登录帐号必填。";
        foreach (var (name, value, max) in new[] { ("平台名称", row.Platform, 120), ("平台地址", row.Url, 1000), ("帐号名称", row.Name, 120),
                     ("登录帐号", row.Login, 160), ("密码", row.Password, 1024), ("备注", row.Notes, 2000) })
            if (value.Contains('\0') || value.EnumerateRunes().Count() > max) return $"{name}包含无效字符或超过 {max} 字符。";
        if (row.Url.Length > 0 && (!Uri.TryCreate(row.Url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.Host.Length == 0 || uri.UserInfo.Length > 0))
            return "平台地址须为完整的 http:// 或 https:// 地址。";
        if (row.TagNames.Length > 20 || row.TagNames.Any(t => t.Contains('\0') || t.EnumerateRunes().Count() > 40)) return "最多 20 个标签，每个最多 40 字符。";
        return "";
    }
}
