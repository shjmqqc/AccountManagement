using System.Windows;
using AccountManagement.Models;
using AccountManagement.Services;
using AccountManagement.ViewModels;
using Wpf.Ui.Controls;

namespace AccountManagement.Views;

public partial class AdminWindow : FluentWindow
{
    public AdminViewModel ViewModel { get; }
    public ManagedUser? ViewUser { get; private set; }
    public bool SessionExpired { get; private set; }
    public AdminWindow(ApiClient api, bool loadOnOpen = true)
    {
        InitializeComponent(); DataContext = ViewModel = new AdminViewModel(api);
        ViewModel.SecretReady += (title, secret, instruction) => new SecretWindow(title, secret, instruction) { Owner = this }.ShowDialog();
        ViewModel.SessionExpired += () => SessionExpired = true;
        ViewModel.PropertyChanged += (_, e) => { if (e.PropertyName == nameof(ViewModel.IsBusy) && !ViewModel.IsBusy && SessionExpired) Close(); };
        if (loadOnOpen) Loaded += async (_, _) => await ViewModel.RefreshCommand.ExecuteAsync(null);
        Closing += (_, e) => e.Cancel = ViewModel.IsBusy;
        Closed += (_, _) => { ViewModel.Users.Clear(); ViewModel.Invitations.Clear(); };
    }
    private bool Confirm(string message) => System.Windows.MessageBox.Show(this, message, "确认操作",
        System.Windows.MessageBoxButton.YesNo, MessageBoxImage.Warning, System.Windows.MessageBoxResult.No) == System.Windows.MessageBoxResult.Yes;
    private void View_Click(object sender, RoutedEventArgs e)
    { if (ViewModel.IsIdle && ViewModel.SelectedUser is { } user) { ViewUser = user; Close(); } }
    private async void Toggle_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || ViewModel.SelectedUser is not { Role: "user" } user) return;
        if (Confirm(user.Active == 0 ? $"恢复“{user.Username}”的登录权限？" : $"停用“{user.Username}”？该用户所有设备将立即退出，数据仍保留。"))
            await ViewModel.ToggleUserAsync();
    }
    private async void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsBusy || ViewModel.SelectedUser is not { Role: "user" } user) return;
        if (Confirm($"重置“{user.Username}”的登录密码？所有设备将立即退出，并生成一次性展示的临时密码。平台帐号和密码会保留。"))
            await ViewModel.ResetPasswordAsync();
    }
    private async void Revoke_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel.IsIdle && ViewModel.CanRevoke && Confirm("撤销所选邀请？撤销后将无法用于注册。")) await ViewModel.RevokeInviteAsync();
    }
}
