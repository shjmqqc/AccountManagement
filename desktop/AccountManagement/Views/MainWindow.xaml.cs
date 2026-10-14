using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using AccountManagement.Models;
using AccountManagement.Services;
using AccountManagement.ViewModels;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace AccountManagement.Views;

public partial class MainWindow : FluentWindow
{
    private readonly ApiClient api;
    private bool ended;
    private double detailWidth = 300;
    public MainViewModel ViewModel { get; }
    public event Action<string>? SignedOut;

    public MainWindow(ApiClient api, UserRecord user, bool loadOnOpen = true)
    {
        InitializeComponent(); this.api = api;
        ViewModel = new MainViewModel(api, user); DataContext = ViewModel;
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MainViewModel.CanEdit)) BatchSelectColumn.Visibility = ViewModel.CanEdit ? Visibility.Visible : Visibility.Collapsed;
        };
        ViewModel.SessionExpired += () => EndSession("登录已失效，请重新登录。");
        if (loadOnOpen) Loaded += async (_, _) => await ViewModel.RefreshCommand.ExecuteAsync(null);
        Deactivated += (_, _) => ViewModel.HidePassword();
        Closed += (_, _) => { ended = true; ViewModel.ClearData(); };
    }

    private void EndSession(string notice) { if (ended) return; ended = true; SignedOut?.Invoke(notice); }
    private void Update_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy) return;
        ViewModel.HidePassword(); (Application.Current as App)?.ShowUpdates(this);
    }
    private async void Admin_Click(object sender, RoutedEventArgs e)
    {
        if (!ViewModel.IsAdmin || ViewModel.IsBusy) return;
        ViewModel.HidePassword();
        var dialog = new AdminWindow(api) { Owner = this };
        dialog.ShowDialog();
        if (dialog.SessionExpired) { EndSession("登录已失效，请重新登录。"); return; }
        if (dialog.ViewUser is { } user) await ViewModel.SwitchUserAsync(user.Id == ViewModel.User.Id ? null : user);
    }
    private async void ReturnOwn_Click(object sender, RoutedEventArgs e) => await ViewModel.SwitchUserAsync(null);
    private async void Shared_Click(object sender, RoutedEventArgs e) => await ViewModel.SwitchSharedAsync();
    private async void Outgoing_Click(object sender, RoutedEventArgs e) => await ViewModel.SwitchOutgoingAsync();
    private async void Share_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || ViewModel.IsReadOnly || ViewModel.SelectedAccount is not { } account) return;
        ViewModel.HidePassword();
        var dialog = new ShareWindow(api, account) { Owner = this };
        dialog.ShowDialog();
        if (dialog.SessionExpired) EndSession("登录已失效，请重新登录。");
        else await ViewModel.RefreshCommand.ExecuteAsync(null);
    }
    private void AccountCheck_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AccountRecord account }) ViewModel.ToggleCheck(account);
    }
    private void CheckAll_Click(object sender, RoutedEventArgs e) => ViewModel.CheckAll();
    private void ClearChecks_Click(object sender, RoutedEventArgs e) => ViewModel.ClearChecks();
    private async void BatchShare_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || !ViewModel.CanBatchShare) return;
        var accounts = ViewModel.Accounts.Where(a => ViewModel.CheckedAccountIds.Contains(a.Id)).ToArray();
        if (accounts.Length == 0) return;
        ViewModel.HidePassword();
        var dialog = new BatchShareWindow(api, accounts) { Owner = this };
        dialog.ShowDialog();
        if (dialog.SessionExpired) EndSession("登录已失效，请重新登录。");
        else if (dialog.Submitted) { ViewModel.ClearChecks(); await ViewModel.RefreshCommand.ExecuteAsync(null); }
    }
    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || !ViewModel.CanEdit) return;
        ViewModel.HidePassword();
        var dialog = new ImportWindow(api) { Owner = this }; dialog.ShowDialog();
        if (dialog.SessionExpired) EndSession("登录已失效，请重新登录。");
        else if (dialog.Submitted) await ViewModel.SwitchUserAsync(null);
    }
    private async void ManageTags_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || !ViewModel.CanEdit) return;
        ViewModel.HidePassword();
        var dialog = new TagManagerWindow(api) { Owner = this }; dialog.ShowDialog();
        if (dialog.SessionExpired) EndSession("登录已失效，请重新登录。");
        else await ViewModel.RefreshCommand.ExecuteAsync(null);
    }
    private async void Platform_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ViewModel is null || ViewModel.IsBusy || ViewModel.SelectedPlatform is null) return;
        await ViewModel.SearchCommand.ExecuteAsync(null);
    }
    private void UserMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { ContextMenu: { } menu } element)
        { menu.PlacementTarget = element; menu.Placement = PlacementMode.Bottom; menu.IsOpen = true; }
    }

    private async Task EditAsync(EditorKind kind, PlatformRecord? platform = null, AccountRecord? account = null)
    {
        if (ViewModel.IsBusy || ViewModel.IsReadOnly && kind != EditorKind.ChangePassword) return;
        ViewModel.HidePassword();
        var dialog = new EditorWindow(api, kind, ViewModel.Platforms, platform, account) { Owner = this };
        dialog.ShowDialog();
        if (dialog.SessionExpired) { EndSession("登录已失效，请重新登录。"); return; }
        if (!dialog.Saved) return;
        if (kind == EditorKind.ChangePassword) { EndSession("登录密码已修改，请重新登录。"); return; }
        if (kind == EditorKind.Account) ViewModel.SearchText = "";
        await ViewModel.RunAsync(async () =>
        {
            // Show saved records even when an edit moves an account to a different platform.
            await ViewModel.ReloadAsync(kind == EditorKind.Account ? dialog.SavedId : null,
                kind == EditorKind.Account ? "" : dialog.SavedId);
        }, "已保存并同步");
    }

    private async void AddPlatform_Click(object sender, RoutedEventArgs e) => await EditAsync(EditorKind.Platform);
    private async void EditPlatform_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedPlatform is not { Id.Length: > 0 } platform) { ViewModel.Status = "请先选择一个具体平台。"; return; }
        await EditAsync(EditorKind.Platform, platform);
    }
    private async void AddAccount_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.Platforms.All(p => p.Id.Length == 0)) { ViewModel.Status = "请先新增一个平台，再添加帐号。"; return; }
        await EditAsync(EditorKind.Account, ViewModel.SelectedPlatform?.Id.Length > 0 ? ViewModel.SelectedPlatform : null);
    }
    private async void EditAccount_Click(object sender, RoutedEventArgs e)
    { if (ViewModel.SelectedAccount is { } account) await EditAsync(EditorKind.Account, account: account); }
    private async void ChangePassword_Click(object sender, RoutedEventArgs e) => await EditAsync(EditorKind.ChangePassword);

    private async void DeletePlatform_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsReadOnly || ViewModel.IsBusy) return;
        if (ViewModel.SelectedPlatform is not { Id.Length: > 0 } platform) { ViewModel.Status = "请先选择一个具体平台。"; return; }
        if (platform.AccountCount > 0) { ViewModel.Status = "平台内还有帐号，请先删除其中的帐号。"; return; }
        if (System.Windows.MessageBox.Show(this, $"删除平台“{platform.Name}”？此操作无法撤销。", "删除平台", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await ViewModel.RunAsync(async () => { await api.DeletePlatformAsync(platform.Id); await ViewModel.ReloadAsync(selectPlatformId: ""); }, "平台已删除");
    }
    private async void DeleteAccount_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsReadOnly || ViewModel.IsBusy) return;
        if (ViewModel.SelectedAccount is not { } account) return;
        ViewModel.HidePassword();
        if (System.Windows.MessageBox.Show(this, $"删除帐号“{account.DisplayName}”？此操作无法撤销。", "删除帐号", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes) return;
        await ViewModel.RunAsync(async () => { await api.DeleteAccountAsync(account.Id); await ViewModel.ReloadAsync(); }, "帐号已删除");
    }
    private async void CopyLogin_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.SelectedAccount is not { } account) return;
        await ViewModel.RunAsync(() => { CopyText(account.Login); return Task.CompletedTask; }, "登录帐号已复制");
    }
    private async void CopyPassword_Click(object sender, RoutedEventArgs e) => await ViewModel.CopyPasswordAsync(CopyText);
    private async void CopyAll_Click(object sender, RoutedEventArgs e) => await ViewModel.CopyAllAsync(CopyText);
    private void ToggleDetails_Click(object sender, RoutedEventArgs e) => SetDetailsVisible(DetailPanel.Visibility != Visibility.Visible);
    internal void SetDetailsVisible(bool visible)
    {
        if (!visible)
        {
            detailWidth = Math.Max(280, DetailColumn.ActualWidth);
            ViewModel.HidePassword();
        }
        DetailPanel.Visibility = DetailSplitter.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        DetailColumn.MinWidth = visible ? 280 : 0;
        DetailColumn.Width = new GridLength(visible ? detailWidth : 0);
        DetailGap.Width = new GridLength(visible ? 8 : 0);
        AccountColumn.Width = new GridLength(1, GridUnitType.Star);
        ToggleDetailsButton.Content = visible ? "收起详情" : "展开详情";
    }
    private async void CopyRowLogin_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AccountRecord account }) await CopyRowAsync(account, false, CopyText);
    }
    private async void CopyRowPassword_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: AccountRecord account }) await CopyRowAsync(account, true, CopyText);
    }
    internal async Task CopyRowAsync(AccountRecord account, bool password, Action<string> copy)
    {
        if (ViewModel.IsBusy || !ViewModel.Accounts.Contains(account)) return;
        ViewModel.SelectedAccount = account;
        ViewModel.HidePassword();
        if (password) await ViewModel.CopyPasswordAsync(copy);
        else await ViewModel.RunAsync(() => { copy(account.Login); return Task.CompletedTask; }, "登录帐号已复制");
    }
    private static void CopyText(string value)
    {
        if (value.Length == 0) Clipboard.Clear();
        else Clipboard.SetText(value);
    }
    private void OpenUrl_Click(object sender, RoutedEventArgs e)
    {
        if (!Uri.TryCreate(ViewModel.DetailUrl, UriKind.Absolute, out var url) || !(url.Scheme is "http" or "https"))
        { ViewModel.Status = "该平台尚未设置有效网址。"; return; }
        try { Process.Start(new ProcessStartInfo(url.AbsoluteUri) { UseShellExecute = true }); }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception or InvalidOperationException)
        { ViewModel.Status = "无法打开默认浏览器。"; }
    }
    private async void Logout_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.HidePassword();
        var revoked = await ViewModel.RunAsync(() => api.LogoutAsync());
        EndSession(revoked ? "已退出登录。" : "已退出本机登录；服务连接失败，服务器上的会话可能要到期才失效。");
    }
}
