using System.IO;
using System.Windows;
using AccountManagement.Models;
using AccountManagement.Services;
using AccountManagement.Views;
using AccountManagement.ViewModels;
using Velopack;

namespace AccountManagement;

public partial class App : Application
{
    [STAThread]
    public static void Main(string[] args)
    {
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        using var instance = new SingleInstance();
        if (!instance.IsOwner) { instance.RequestActivation(); return; }
        var app = new App(); app.InitializeComponent();
        var activationTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        activationTimer.Tick += (_, _) =>
        {
            if (app.MainWindow is not { IsVisible: true } main || !instance.TakeActivation()) return;
            if (main.WindowState == WindowState.Minimized) main.WindowState = WindowState.Normal;
            var target = main.OwnedWindows.Cast<Window>().LastOrDefault(window => window.IsVisible) ?? main;
            target.Activate();
        };
        activationTimer.Start();
        try { app.Run(); }
        finally { activationTimer.Stop(); }
    }
    public UpdateViewModel? Updates { get; private set; }
    private readonly ApiClient api;
    private readonly SessionStore sessions;
    public App() : this(new ApiClient(), new SessionStore())
    { Updates = new UpdateViewModel(new DesktopUpdater()); }
    internal App(ApiClient api, SessionStore sessions) { this.api = api; this.sessions = sessions; }
    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (Updates is not null) _ = Updates.CheckAsync();
        var endpoint = sessions.LoadEndpoint();
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
        OpenLogin(saved?.Endpoint ?? endpoint, saved?.Username);
    }

    public void ShowUpdates(Window owner)
    {
        if (Updates is null || owner.OwnedWindows.Count > 0) return;
        new UpdateWindow(Updates) { Owner = owner }.ShowDialog();
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
