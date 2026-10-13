using System.IO;
using System.Windows;
using AccountManagement.Models;
using AccountManagement.Services;
using Microsoft.Win32;
using Wpf.Ui.Controls;

namespace AccountManagement.Views;

public partial class ImportWindow : FluentWindow
{
    private readonly ApiClient api;
    private List<ImportRow> rows = [];
    private bool busy, stop, submitted;
    public bool Submitted { get; private set; }
    public bool SessionExpired { get; private set; }
    public ImportWindow(ApiClient api)
    {
        InitializeComponent(); this.api = api;
        Closing += (_, e) => e.Cancel = busy;
        Closed += (_, _) => ClearRows();
    }
    private void ClearRows() { foreach (var row in rows) row.Password = ""; rows.Clear(); Preview.ItemsSource = null; }
    private void SetBusy(bool value)
    {
        busy = value; FileActions.IsEnabled = CloseButton.IsEnabled = !value;
        SubmitButton.IsEnabled = !value && !submitted && rows.Any(r => r.Valid);
        StopButton.Visibility = value && submitted ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = !stop;
    }
    private async void Template_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new SaveFileDialog { Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", FileName = "帐号导入模板.xlsx" };
        if (dialog.ShowDialog(this) != true) return;
        SetBusy(true);
        try { await Task.Run(() => ExcelImport.SaveTemplate(dialog.FileName)); Notice.Text = "模板已保存。请按文本填写帐号和密码，不要使用公式。"; }
        catch (Exception) { Notice.Text = "模板保存失败，请检查目标文件是否被占用或目录是否可写。"; }
        finally { SetBusy(false); }
    }
    private async void Choose_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        var dialog = new OpenFileDialog { Filter = "Excel 工作簿 (*.xlsx)|*.xlsx", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) await LoadAsync(dialog.FileName);
    }
    internal async Task LoadAsync(string path)
    {
        ClearRows(); submitted = false; stop = false; SetBusy(true); FileLabel.Text = Path.GetFileName(path);
        try
        {
            rows = await Task.Run(() => ExcelImport.Read(path)); Preview.ItemsSource = rows;
            Notice.Text = $"共 {rows.Count} 行，可提交 {rows.Count(r => r.Valid)} 行，校验失败 {rows.Count(r => !r.Valid)} 行。确认后逐行导入，重复记录自动跳过。";
        }
        catch (ArgumentException error) { Notice.Text = error.Message; }
        catch (Exception) { Notice.Text = "无法读取 Excel，请确认文件未损坏、未加密，并按模板填写。"; }
        finally { SetBusy(false); }
    }
    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (busy || submitted || !rows.Any(r => r.Valid)) return;
        if (System.Windows.MessageBox.Show(this, $"将提交 {rows.Count(r => r.Valid)} 行到本人帐号库。\n重复帐号跳过，不覆盖任何已有内容。确认导入？", "确认导入", System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question, System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;
        await SubmitAsync();
    }
    internal async Task SubmitAsync()
    {
        if (busy || submitted || !rows.Any(r => r.Valid)) return;
        submitted = Submitted = true; stop = false; SetBusy(true);
        try
        {
            SessionExpired = await AccountImport.RunAsync(api, rows, () => Notice.Text = Summary(), () => stop);
            Notice.Text = Summary() + (SessionExpired ? " 登录已失效，请关闭后重新登录。" : " 请查看逐行结果；修正后可重新选择文件导入，已成功记录会跳过。");
        }
        finally { foreach (var row in rows) row.Password = ""; SetBusy(false); }
    }
    private string Summary() => string.Join("；", rows.GroupBy(r => r.State).Select(g => $"{g.Key} {g.Count()} 行")) + "。";
    private void Stop_Click(object sender, RoutedEventArgs e) { stop = true; StopButton.IsEnabled = false; Notice.Text = "等待当前行返回后停止，不再提交后续行。"; }
    private void Close_Click(object sender, RoutedEventArgs e) { if (!busy) Close(); }
}
