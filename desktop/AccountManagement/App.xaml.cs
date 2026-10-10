using System.IO;
using System.Windows;
using AccountManagement.Models;
using AccountManagement.Services;
using AccountManagement.Views;

namespace AccountManagement;

public partial class App : Application
{
    private readonly ApiClient api;
    private readonly SessionStore sessions;
    public App() : this(new ApiClient(), new SessionStore()) { }
    internal App(ApiClient api, SessionStore sessions) { this.api = api; this.sessions = sessions; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var saved = sessions.Load();
        if (saved is not null)
        {
            try
            {
                api.Configure(saved.Endpoint, saved.Token);
                var user = await api.MeAsync();
                OpenMain(user);
                return;
            }
            catch (Exception error) when (error is ApiException or ArgumentException) { api.ClearSession(); }
        }
        OpenLogin(saved?.Endpoint, saved?.Username);
    }

    private void OpenLogin(string? endpoint = null, string? username = null, string? notice = null)
    {
        var login = new LoginWindow(api, sessions, endpoint, username, notice);
        MainWindow = login;
        login.SignedIn += user => { OpenMain(user); login.Close(); };
        login.Show();
    }

    private void OpenMain(UserRecord user)
    {
        var window = new MainWindow(api, user);
        MainWindow = window;
        window.SignedOut += notice =>
        {
            api.ClearSession();
            try { sessions.Clear(); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            { notice += " 本机登录状态文件未能删除，请检查文件权限。"; }
            window.ViewModel.ClearData();
            OpenLogin(api.Endpoint, user.Username, notice);
            window.Close();
        };
        window.Show();
    }
    protected override void OnExit(ExitEventArgs e) { api.Dispose(); base.OnExit(e); }
}
