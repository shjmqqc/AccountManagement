using System.Collections.ObjectModel;
using System.Windows;
using AccountManagement.Models;
using AccountManagement.Services;
using Wpf.Ui.Controls;

namespace AccountManagement.Views;

public partial class BatchShareWindow : FluentWindow
{
    private readonly ApiClient api;
    private readonly AccountRecord[] accounts;
    internal ObservableCollection<RecipientRecord> Recipients { get; } = [];
    internal ObservableCollection<BatchShareResult> Results { get; } = [];
    private bool busy;
    public bool Submitted { get; private set; }
    public bool SessionExpired { get; private set; }

    public BatchShareWindow(ApiClient api, IEnumerable<AccountRecord> accounts)
    {
        InitializeComponent(); this.api = api; this.accounts = accounts.DistinctBy(a => a.Id).ToArray();
        Heading.Text = $"批量分享 · {this.accounts.Length} 个帐号";
        AccountList.ItemsSource = this.accounts; RecipientList.ItemsSource = Recipients; ResultList.ItemsSource = Results;
        Closing += (_, e) => e.Cancel = busy;
    }
    private void SetBusy(bool value)
    {
        busy = value;
        SearchFields.IsEnabled = RemoveButton.IsEnabled = SubmitButton.IsEnabled = !value && !Submitted;
        CloseButton.IsEnabled = !value;
    }
    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Submitted) return;
        if (string.IsNullOrWhiteSpace(UsernameBox.Text)) { Notice.Text = "请输入完整用户名。"; return; }
        SetBusy(true); RecipientPicker.ItemsSource = null;
        try
        {
            var users = await api.FindRecipientsAsync(UsernameBox.Text);
            RecipientPicker.ItemsSource = users; RecipientPicker.SelectedIndex = 0;
            Notice.Text = users.Count == 0 ? "未找到启用的匹配用户。" : "请选择并添加接收人。";
        }
        catch (ApiException error)
        {
            Notice.Text = error.Message;
            SessionExpired = error.Status == 401 || error.Code == "password_change_required";
        }
        finally { SetBusy(false); }
        if (SessionExpired) Close();
    }
    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Submitted || RecipientPicker.SelectedItem is not RecipientRecord user) return;
        if (Recipients.All(u => u.Id != user.Id)) Recipients.Add(user);
        Notice.Text = $"已选择 {accounts.Length} 个帐号、{Recipients.Count} 位接收人。";
    }
    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (!busy && !Submitted && RecipientList.SelectedItem is RecipientRecord user) Recipients.Remove(user);
    }
    private async void Submit_Click(object sender, RoutedEventArgs e)
    {
        if (busy || Submitted) return;
        if (Recipients.Count == 0) { Notice.Text = "请至少添加一位接收人。"; return; }
        if (System.Windows.MessageBox.Show(this, $"将清单中的 {accounts.Length} 个帐号分享给以下 {Recipients.Count} 位用户：\n\n{string.Join("、", Recipients.Select(u => u.Username))}\n\n已有授权将跳过，其他接收人的授权保持不变。确认提交？", "确认批量分享", System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Question, System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;
        await SubmitAsync();
    }
    internal async Task SubmitAsync()
    {
        if (busy || Submitted || Recipients.Count == 0 || accounts.Length == 0) return;
        Submitted = true; SetBusy(true);
        try
        {
            SessionExpired = await BatchSharing.RunAsync(api, accounts, Recipients.ToArray(), result =>
            {
                Results.Add(result); Notice.Text = $"已处理 {Results.Count} / {accounts.Length * Recipients.Count} 项授权";
            });
            Notice.Text = string.Join("；", Results.GroupBy(r => r.State).Select(g => $"{g.Key} {g.Count()} 项"))
                + "。" + (SessionExpired ? "登录已失效，关闭后重新登录。" : "可在分享管理中核对授权；本窗口不会自动重试。");
        }
        finally { SetBusy(false); }
    }
    private void Close_Click(object sender, RoutedEventArgs e) { if (!busy) Close(); }
}
