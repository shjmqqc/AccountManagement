using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using AccountManagement;
using AccountManagement.Models;
using AccountManagement.Services;
using AccountManagement.Views;

internal static class LifecycleChecks
{
    public static int Run(string scenario)
    {
        var sessions = new SessionStore(Path.GetFullPath("artifacts/lifecycle-" + scenario));
        sessions.Clear();
        if (scenario is not ("login" or "login-transition")) sessions.Save(new SavedSession("http://127.0.0.1:18083", "lifecycle-test",
            new string('x', 43), DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds()));
        using var handler = new Handler(scenario == "pending");
        var api = new ApiClient(handler);
        var app = new App(api, sessions);
        app.InitializeComponent();
        // Real windows and dispatcher, kept offscreen and out of the taskbar.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((sender, e) =>
        {
            if (sender is Window window && ReferenceEquals(e.OriginalSource, window))
            { window.ShowInTaskbar = false; window.Left = -10000; window.Top = -10000; }
        }));
        bool requestedClose = false, transitioned = false;
        var probe = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        // Allow WPF startup/transition separately; actual close must still exit within five seconds.
        var watchdog = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        watchdog.Tick += (_, _) => {
            Console.Error.WriteLine($"FAIL lifecycle {scenario}: close={requestedClose}, transition={transitioned}, window={app.MainWindow?.GetType().Name}, busy={(app.MainWindow as MainWindow)?.ViewModel.IsBusy}, status={(app.MainWindow as MainWindow)?.ViewModel.Status}");
            app.Shutdown(91);
        };
        probe.Tick += (_, _) =>
        {
            if (app.MainWindow is not { IsVisible: true } window) return;
            if (scenario == "login-transition" && !transitioned)
            {
                if (window is not LoginWindow login) return;
                transitioned = true;
                ((System.Windows.Controls.TextBox)login.FindName("UsernameBox")).Text = "lifecycle-test";
                ((System.Windows.Controls.PasswordBox)login.FindName("PasswordInput")).Password = "fake-password";
                ((System.Windows.Controls.Button)login.FindName("LoginButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Button.ClickEvent));
                return;
            }
            if (scenario == "login-transition" && window is not MainWindow) return;
            if (scenario == "logout" && !transitioned)
            {
                if (window is not MainWindow main || main.ViewModel.IsBusy) return;
                transitioned = true;
                typeof(MainWindow).GetMethod("Logout_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
                    .Invoke(main, [main, new RoutedEventArgs()]);
                return;
            }
            if (scenario == "logout" && window is not LoginWindow) return;
            if (scenario == "pending" && !handler.RequestPending) return;
            if (scenario == "owned")
            {
                var child = new AdminWindow(api, false) { Owner = window, ShowInTaskbar = false, Left = -10000, Top = -10000, WindowStartupLocation = WindowStartupLocation.Manual };
                child.Show();
            }
            requestedClose = true;
            probe.Stop();
            watchdog.Stop(); watchdog.Interval = TimeSpan.FromSeconds(5); watchdog.Start();
            window.Close();
        };
        probe.Start(); watchdog.Start();
        int exit = app.Run();
        probe.Stop(); watchdog.Stop(); sessions.Clear();
        if (exit != 0 || !requestedClose) return 1;
        Console.WriteLine("PASS process exit: " + scenario);
        return 0;
    }

    private sealed class Handler(bool pending) : HttpMessageHandler
    {
        public bool RequestPending { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var path = request.RequestUri!.AbsolutePath;
            if (pending && path == "/api/platforms")
            { RequestPending = true; await Task.Delay(Timeout.Infinite, token); }
            string json = path switch
            {
                "/api/me" => "{\"id\":\"test-user\",\"username\":\"lifecycle-test\",\"role\":\"admin\"}",
                "/api/login" => "{\"token\":\"fake-token\",\"expires_at\":4102444800000,\"must_change_password\":false}",
                "/api/logout" => "{\"ok\":true}",
                _ => "[]"
            };
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
