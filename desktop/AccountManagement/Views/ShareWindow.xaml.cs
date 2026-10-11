using System.Windows;
using AccountManagement.Models;
using AccountManagement.Services;
using Wpf.Ui.Controls;

namespace AccountManagement.Views;

public partial class ShareWindow : FluentWindow
{
    private readonly ApiClient api;
    private readonly AccountRecord account;
    private bool busy;
    public bool SessionExpired { get; private set; }
    public ShareWindow(ApiClient api, AccountRecord account, bool loadOnOpen = true)
    {
        InitializeComponent(); this.api = api; this.account = account;
        Heading.Text = "分享 · " + account.DisplayName;
        if (loadOnOpen) Loaded += async (_, _) => await LoadAsync();
        Closing += (_, e) => e.Cancel = busy;
    }
    private async Task RunAsync(Func<Task> action, string success)
    {
        if (busy) return;
        busy = true; Fields.IsEnabled = false; RefreshButton.IsEnabled = false; Notice.Text = "正在处理…";
        try { await action(); Notice.Text = success; }
        catch (ApiException e) { Notice.Text = e.Message; SessionExpired = e.Status == 401 || e.Code == "password_change_required"; }
        finally { busy = false; Fields.IsEnabled = true; RefreshButton.IsEnabled = true; }
        if (SessionExpired) Close();
    }
    public Task LoadAsync() => RunAsync(async () => GrantList.ItemsSource = await api.SharesAsync(account.Id), "已刷新授权列表");
    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAsync();
    private async void Search_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(UsernameBox.Text)) { Notice.Text = "请输入完整用户名，不提供全体用户列表。"; return; }
        RecipientPicker.ItemsSource = null;
        await RunAsync(async () => { RecipientPicker.ItemsSource = await api.FindRecipientsAsync(UsernameBox.Text); RecipientPicker.SelectedIndex = 0; }, "查找完成；仅显示启用的匹配用户");
    }
    private async void Grant_Click(object sender, RoutedEventArgs e)
    {
        if (RecipientPicker.SelectedItem is not RecipientRecord user) { Notice.Text = "请先查找并选择接收人。"; return; }
        await RunAsync(async () => { await api.GrantAsync(account.Id, user.Id); GrantList.ItemsSource = await api.SharesAsync(account.Id); }, "已授予只读权限");
    }
    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (GrantList.SelectedItem is not RecipientRecord user) { Notice.Text = "请先选择已授权用户。"; return; }
        if (System.Windows.MessageBox.Show(this, $"撤销“{user.Username}”对此帐号的访问？已复制的密码无法收回。", "撤销分享",
            System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) != System.Windows.MessageBoxResult.Yes) return;
        await RunAsync(async () => { await api.RevokeShareAsync(account.Id, user.Id); GrantList.ItemsSource = await api.SharesAsync(account.Id); }, "授权已撤销");
    }
}
