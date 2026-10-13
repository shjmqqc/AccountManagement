using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using AccountManagement.Models;
using AccountManagement.Services;
using AccountManagement.Views;
using ClosedXML.Excel;

internal static class ImportChecks
{
    internal static async Task Run(ApiClient api, Action<bool, string> check, Action<Window, string, int, int> capture)
    {
        Directory.CreateDirectory("artifacts/import-tests");
        var path = Path.GetFullPath("artifacts/import-tests/accounts.xlsx");
        ExcelImport.SaveTemplate(path);
        try { ExcelImport.Read(path); throw new Exception("Empty workbook accepted"); } catch (ArgumentException) { check(true, "Empty template rejected"); }
        using (var book = new XLWorkbook(path))
        {
            var sheet = book.Worksheet(1);
            string[][] values = [
                ["导入集成平台", "https://example.com", "生产帐号", "00123", " 空格密码🔐 ", "第一行\n第二行", "导入标签；正式"],
                ["导入集成平台", "https://changed.example", "生产帐号", "00123", "不得覆盖", "覆盖备注", "不应创建"],
                ["导入集成平台", "", "测试帐号", "00123", "", "", "导入标签"],
                ["", "", "缺平台", "user", "", "", ""],
                ["公式平台", "", "不导入", "user", "", "", ""]];
            for (var r = 0; r < values.Length; r++) for (var c = 0; c < 7; c++) sheet.Cell(r + 2, c + 1).Value = values[r][c];
            sheet.Cell(6, 5).FormulaA1 = "1+1";
            book.Save();
        }
        var rows = ExcelImport.Read(path);
        check(rows.Count == 5 && rows.Count(r => r.Valid) == 3, "Excel required fields and formulas validated per row");
        check(rows[0].Login == "00123" && rows[0].Password == " 空格密码🔐 " && rows[0].Notes.Contains('\n'), "Text leading zeros, Unicode and spaces preserved");
        check(rows[1].State == "待查重" && rows[1].RowNumber == 3, "File duplicates identified with physical row number");
        check(rows[0].TagNames.Length == 2, "Semicolon separated tags parsed");
        var existing = await api.CreateTagAsync("导入标签", "red");
        var dialog = new ImportWindow(api); await dialog.LoadAsync(path);
        capture(dialog, "excel-import-preview", 1040, 700);
        await dialog.SubmitAsync();
        var preview = (DataGrid)dialog.FindName("Preview");
        var results = preview.Items.Cast<ImportRow>().ToArray();
        check(results.Count(r => r.State == "成功") == 2 && results.Count(r => r.State == "重复跳过") == 1 && results.Count(r => r.State == "校验失败") == 2, "Import per-row summary");
        check(results.All(r => r.Password.Length == 0), "Import clears plaintext after processing");
        await dialog.SubmitAsync();
        check((await api.AccountsAsync("", null)).Count == 2, "Double submit prevented");
        capture(dialog, "excel-import-results", 1040, 700); dialog.Close();
        var records = await api.AccountsAsync("", null);
        check(await api.PasswordAsync(records.Single(r => r.Name == "生产帐号").Id) == " 空格密码🔐 ", "Duplicate preserves original password");
        var tags = await api.TagsAsync();
        check(tags.Single(t => t.Id == existing.Id).Color == "red" && tags.Single(t => t.Id == existing.Id).AccountCount == 2, "Existing tag color reused and usage counted");
        check(tags.All(t => t.Name != "不应创建"), "Skipped row creates no tag");
        var manager = new TagManagerWindow(api, false); await manager.RefreshAsync();
        ((ListBox)manager.FindName("TagList")).SelectedIndex = 0;
        capture(manager, "tag-manager", 760, 560); manager.Close();
        await api.UpdateTagAsync(existing.Id, "导入已改名", "green");
        check((await api.AccountsAsync("", null)).All(r => r.Tags.Any(t => t.Name == "导入已改名" && t.Color == "green")), "Tag updates propagate to accounts");
        await api.DeleteTagAsync(existing.Id);
        check((await api.AccountsAsync("", null)).Count == 2 && (await api.AccountsAsync("", null)).All(r => r.Tags.All(t => t.Id != existing.Id)), "Delete tag retains accounts");
        foreach (var row in records) await api.DeleteAccountAsync(row.Id);
        foreach (var platform in await api.PlatformsAsync()) await api.DeletePlatformAsync(platform.Id);
        foreach (var tag in await api.TagsAsync()) await api.DeleteTagAsync(tag.Id);

        foreach (var status in new[] { 400, 401, 503, 0 })
        {
            var handler = new Failure(status); using var fake = new ApiClient(handler); fake.Configure("http://127.0.0.1:18083");
            ImportRow[] batch = [new() { Platform = "A", Login = "1", Password = "test" }, new() { Platform = "B", Login = "2", Password = "test" }];
            bool expired = await AccountImport.RunAsync(fake, batch, () => { }, () => false);
            check(expired == (status == 401), "Import session expiry propagated");
            check(handler.Count == (status == 400 ? 2 : 1), "Only validation failures continue import");
            check(batch.All(r => r.Password.Length == 0), "Failure clears password values");
            if (status is 0 or 503) check(batch[0].State == "结果待确认" && batch[1].State == "未导入", "Unknown writes stop with honest status");
        }
        using (var book = new XLWorkbook(path)) { book.Worksheet(1).Cell(1002, 1).Value = "too many"; book.Save(); }
        try { ExcelImport.Read(path); throw new Exception("Large row count accepted"); } catch (ArgumentException) { check(true, "1000-row limit enforced"); }
    }
    private sealed class Failure(int status) : HttpMessageHandler
    {
        internal int Count;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Count++;
            if (status == 0) throw new HttpRequestException();
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("{\"error\":\"invalid_length\"}", Encoding.UTF8, "application/json") });
        }
    }
}
