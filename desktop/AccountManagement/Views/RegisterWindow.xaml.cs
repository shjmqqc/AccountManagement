using System.Windows;
using AccountManagement.Services;
using Wpf.Ui.Controls;

namespace AccountManagement.Views;

public partial class RegisterWindow : FluentWindow
{
    private readonly ApiClient api;
    private bool busy;
    public string? RegisteredUsername { get; private set; }
    public RegisterWindow(ApiClient api)
    {
        InitializeComponent(); this.api = api;
        Closing += (_, e) => e.Cancel = busy;
        Closed += (_, _) => { PasswordInput.Clear(); ConfirmInput.Clear(); InviteBox.Clear(); };
    }
    private async void Register_Click(object sender, RoutedEventArgs e)
    {
        if (busy) return;
        if (InviteBox.Text.Trim().Length != 43 || string.IsNullOrWhiteSpace(UsernameBox.Text))
        { Notice.Text = "请填写完整邀请码和用户名。"; return; }
        if (PasswordInput.Password.EnumerateRunes().Count() < 6 || PasswordInput.Password != ConfirmInput.Password)
        { Notice.Text = "密码至少 6 位，两次输入必须一致。"; return; }
        busy = true; Fields.IsEnabled = false; Notice.Text = "正在注册…";
        try
        {
            await api.RegisterAsync(InviteBox.Text.Trim(), UsernameBox.Text.Trim(), PasswordInput.Password);
            RegisteredUsername = UsernameBox.Text.Trim();
        }
        catch (ApiException error)
        {
            Notice.Text = error.Code == "timeout"
                ? "注册结果暂未确认。请先返回登录页尝试登录；若未注册成功，再使用邀请码重试。" : error.Message;
        }
        finally { busy = false; Fields.IsEnabled = true; PasswordInput.Clear(); ConfirmInput.Clear(); }
        if (RegisteredUsername is not null) Close();
    }
}
