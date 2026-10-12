using System.IO;
using System.Security.Cryptography;
using System.Windows;
using AccountManagement.Models;
using AccountManagement.Services;
using Wpf.Ui.Controls;
using MessageBoxButton = System.Windows.MessageBoxButton;

namespace AccountManagement.Views;

public partial class LoginWindow : FluentWindow
{
    private readonly ApiClient api;
    private readonly SessionStore sessions;
    public event Action<UserRecord>? SignedIn;

    public LoginWindow(ApiClient api, SessionStore sessions, string? endpoint = null, string? username = null, string? notice = null)
    {
        InitializeComponent(); this.api = api; this.sessions = sessions;
        EndpointBox.Text = endpoint ?? api.Endpoint; UsernameBox.Text = username ?? ""; Notice.Text = notice ?? "";
    }

    private void Register_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            api.Configure(EndpointBox.Text);
            var dialog = new RegisterWindow(api) { Owner = this };
            dialog.ShowDialog();
            if (dialog.RegisteredUsername is { } username)
            { UsernameBox.Text = username; PasswordInput.Clear(); Notice.Text = "注册成功，请使用刚设置的密码登录。"; }
        }
        catch (ArgumentException error) { Notice.Text = error.Message; }
    }

    private async void Login_Click(object sender, RoutedEventArgs e)
    {
        if (!Fields.IsEnabled) return;
        if (string.IsNullOrWhiteSpace(UsernameBox.Text) || PasswordInput.Password.Length == 0) { Notice.Text = "请填写用户名和登录密码。"; return; }
        Fields.IsEnabled = false; Notice.Text = "正在登录…";
        try
        {
            api.Configure(EndpointBox.Text);
            var login = await api.LoginAsync(UsernameBox.Text.Trim(), PasswordInput.Password);
            PasswordInput.Clear();
            if (login.MustChangePassword)
            {
                var dialog = new EditorWindow(api, EditorKind.ChangePassword) { Owner = this };
                dialog.ShowDialog();
                if (!dialog.Saved) { try { await api.LogoutAsync(); } catch (ApiException) { } }
                api.ClearSession();
                Notice.Text = dialog.Saved ? "密码已修改，请使用新密码重新登录。" : "请先修改临时密码，才能使用帐号库。";
                return;
            }
            var user = await api.MeAsync();
            try { sessions.Save(new SavedSession(api.Endpoint, user.Username, login.Token, login.ExpiresAt)); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or CryptographicException)
            { System.Windows.MessageBox.Show(this, "本次登录成功，但无法保存登录状态。下次打开需要重新登录。", "登录状态", MessageBoxButton.OK, MessageBoxImage.Information); }
            SignedIn?.Invoke(user);
        }
        catch (Exception error) when (error is ApiException or ArgumentException) { Notice.Text = error.Message; api.ClearSession(); }
        finally { PasswordInput.Clear(); Fields.IsEnabled = true; }
    }
    private void Update_Click(object sender, RoutedEventArgs e)
    { if (Fields.IsEnabled) (Application.Current as App)?.ShowUpdates(this); }
}
