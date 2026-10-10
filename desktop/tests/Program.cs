using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using AccountManagement;
using AccountManagement.Models;
using AccountManagement.Services;
using AccountManagement.ViewModels;
using AccountManagement.Views;

internal static class Program
{
    private static int checks;
    [STAThread]
    private static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--lifecycle") return LifecycleChecks.Run(args[1]);
        // Load styles without running production startup or reading the user's saved session.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AccountManagement;component/Styles.xaml") });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        int code = 0;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { await Run(args[0]); Console.WriteLine($"PASS {checks} desktop checks; previews in artifacts/previews"); }
            catch (Exception error) { Console.Error.WriteLine(error); code = 1; }
            finally { Dispatcher.ExitAllFrames(); }
        });
        Dispatcher.Run();
        return code;
    }

    private static void Check(bool condition, string label)
    { if (!condition) throw new Exception("FAIL " + label); checks++; }

    private static async Task ExpectApi(int status, Func<Task> action)
    {
        try { await action(); throw new Exception("Expected API rejection"); }
        catch (ApiException error) { Check(error.Status == status, "API rejection " + status); }
    }

    private static async Task Run(string endpoint)
    {
        foreach (var bad in new[] { "https://user:password@example.com", "https://example.com/api", "ftp://localhost", "https://example.com/?token=x" })
        {
            try { ApiClient.ValidateEndpoint(bad); throw new Exception("Unsafe endpoint accepted"); }
            catch (ArgumentException) { checks++; }
        }
        Check(ApiClient.ValidateEndpoint("http://127.0.0.1:18083") == "http://127.0.0.1:18083/", "Loopback allowed");
        Check(ApiClient.ValidateEndpoint("http://example.com:18082") == "http://example.com:18082/", "Remote HTTP allowed");
        Check(ApiClient.ValidateEndpoint("https://example.com") == "https://example.com/", "HTTPS allowed");

        string sessionDir = Path.GetFullPath("artifacts/test-session");
        var store = new SessionStore(sessionDir);
        var saved = new SavedSession(endpoint, "test", "secret-test-token", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds());
        store.Save(saved);
        Check(store.Load() == saved, "DPAPI session round-trip");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(sessionDir, "session.bin"))).Contains(saved.Token), "No plaintext token on disk");
        store.Save(saved with { ExpiresAt = 1 }); Check(store.Load() is null, "Expired saved session rejected");
        File.WriteAllText(Path.Combine(sessionDir, "session.bin"), "corrupted"); Check(store.Load() is null, "Corrupted session ignored");
        store.Clear(); Check(store.Load() is null, "Session removed");

        using var api = new ApiClient(); api.Configure(endpoint);
        await ExpectApi(401, () => api.LoginAsync("desktop-test", "incorrect"));
        var login = await api.LoginAsync("desktop-test", "test123456");
        var me = await api.MeAsync(); Check(me.Username == "desktop-test", "Login and identity");
        using var restored = new ApiClient(); restored.Configure(endpoint, login.Token); Check((await restored.MeAsync()).Id == me.Id, "Session restore against server");
        string platformId = await api.SavePlatformAsync(null, "阿里云", "https://www.aliyun.com");
        await ExpectApi(409, () => api.SavePlatformAsync(null, "阿里云", ""));
        var platform = (await api.PlatformsAsync()).Single();
        await api.SavePlatformAsync(platform, "阿里云", "https://www.aliyun.com");
        string accountId = await api.SaveAccountAsync(null, platformId, "公司生产环境", "team@example.com", "正式业务 · 每月核对账单", "测试密码🔐");
        string secondId = await api.SaveAccountAsync(null, platformId, "个人测试帐号", "personal@example.com", "仅用于测试", "");
        string emailPlatform = await api.SavePlatformAsync(null, "企业邮箱", "https://mail.example.com");
        await api.SaveAccountAsync(null, emailPlatform, "工作邮箱", "work@example.com", "日常办公", "example");
        string githubPlatform = await api.SavePlatformAsync(null, "GitHub", "https://github.com");
        await api.SaveAccountAsync(null, githubPlatform, "开发帐号", "developer", "项目协作", "example");
        Check((await api.AccountsAsync("正式", emailPlatform)).Single().Id == accountId, "Search ignores selected platform");
        Check((await api.AccountsAsync("测试密码", null)).Count == 0, "No password search");
        await ExpectApi(409, () => api.DeletePlatformAsync(platformId));
        var account = (await api.AccountsAsync("", platformId)).Single(a => a.Id == accountId);
        await api.SaveAccountAsync(account, platformId, account.Name, account.Login, "备注已更新", null);
        Check(await api.PasswordAsync(accountId) == "测试密码🔐", "Omitted password retained");
        await api.SaveAccountAsync(account, platformId, account.Name, account.Login, account.Notes, "");
        Check(await api.PasswordAsync(accountId) == "", "Explicit empty password clears");
        await api.SaveAccountAsync(account, platformId, account.Name, account.Login, account.Notes, "测试密码🔐");
        using var other = new ApiClient(); other.Configure(endpoint); await other.LoginAsync("other-test", "test123456");
        await ExpectApi(404, () => other.PasswordAsync(accountId)); Check((await other.AccountsAsync("", null)).Count == 0, "User isolation");

        var window = new MainWindow(api, me, false);
        var vm = window.ViewModel;
        Check(await vm.RunAsync(() => vm.ReloadAsync()), "View model initial load");
        Check(vm.Accounts.Count == 4 && vm.Platforms.Count == 4 && vm.SelectedAccount is null, "Three-panel initial state");
        vm.SelectedAccount = vm.Accounts.Single(a => a.Id == accountId);
        await vm.RevealPasswordCommand.ExecuteAsync(null); Check(vm.PasswordVisible && vm.PasswordDisplay == "测试密码🔐", "Reveal password");
        vm.SelectedAccount = vm.Accounts.Single(a => a.Id == secondId); Check(!vm.PasswordVisible && vm.PasswordDisplay == "••••••••", "Selection hides password");
        vm.SelectedAccount = vm.Accounts.Single(a => a.Id == accountId);
        string? copied = null; await vm.CopyPasswordAsync(value => copied = value);
        Check(copied == "测试密码🔐" && !vm.PasswordVisible, "Copy without revealing; no real clipboard modified in tests");
        vm.SearchText = "工作"; await vm.SearchCommand.ExecuteAsync(null); Check(vm.Accounts.Count == 1, "View model search");
        vm.SearchText = ""; await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedAccount = vm.Accounts.Single(a => a.Id == accountId);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        Check(vm.SearchCommand.CanExecute(null) && vm.RefreshCommand.CanExecute(null), "Commands re-enabled after async completion");
        await Task.Delay(250); // Allow WPF UI state transitions to finish before rendering.
        Check(((UIElement)window.FindName("SearchButton")).IsEnabled, "Search button enabled after refresh");
        Capture(window, "main", 1320, 820);
        Capture(window, "main-small", 1060, 660);
        var loginWindow = new LoginWindow(api, store); Capture(loginWindow, "login", 820, 620); loginWindow.Close();
        var editor = new EditorWindow(api, EditorKind.Account, vm.Platforms, account: account); Capture(editor, "edit-account", 530, 680); editor.Close();
        var platformEditor = new EditorWindow(api, EditorKind.Platform, platform: platform); Capture(platformEditor, "edit-platform", 530, 460); platformEditor.Close();
        var passwordEditor = new EditorWindow(api, EditorKind.ChangePassword); Capture(passwordEditor, "change-password", 530, 540); passwordEditor.Close();
        await AdminFlow(api, me, other, endpoint, window);
        window.Close();

        // A late password response must not populate another selection.
        var handler = new DelayedHandler(); using var slow = new ApiClient(handler); slow.Configure(endpoint, login.Token);
        var slowVm = new MainViewModel(slow, me) { SelectedAccount = account };
        var pending = slowVm.RevealPasswordCommand.ExecuteAsync(null);
        slowVm.SelectedAccount = account with { Id = secondId };
        handler.Complete(); await pending;
        Check(!slowVm.PasswordVisible && slowVm.PasswordDisplay == "••••••••", "Late secret response discarded");

        await api.ChangePasswordAsync("test123456", "new123456");
        await ExpectApi(401, () => restored.MeAsync());
        bool expired = false; var expiryVm = new MainViewModel(api, me); expiryVm.SessionExpired += () => expired = true;
        await expiryVm.RefreshCommand.ExecuteAsync(null); Check(expired && expiryVm.Accounts.Count == 0, "Expired session clears UI");
        await api.LoginAsync("desktop-test", "new123456"); Check(await api.PasswordAsync(accountId) == "测试密码🔐", "Vault survives login password change");
        foreach (var record in await api.AccountsAsync("", null)) await api.DeleteAccountAsync(record.Id);
        foreach (var record in await api.PlatformsAsync()) await api.DeletePlatformAsync(record.Id);
        Check((await api.PlatformsAsync()).Count == 0, "Delete flow and cleanup");
        await api.LogoutAsync(); await ExpectApi(401, () => api.MeAsync());
    }

    private static async Task AdminFlow(ApiClient api, UserRecord me, ApiClient other, string endpoint, MainWindow window)
    {
        var management = new AdminViewModel(api);
        string secret = "";
        management.SecretReady += (_, value, _) => secret = value;
        await management.RefreshCommand.ExecuteAsync(null);
        Check(management.Users.Count == 2 && management.IsIdle, "Admin user list loaded");
        management.SelectedUser = management.Users.Single(u => u.Id == me.Id);
        Check(!management.CanManageUser, "Administrator reset and disable unavailable");
        await management.CreateInviteCommand.ExecuteAsync(null);
        Check(secret.Length == 43 && management.Invitations.Count == 1, "Create invitation and refresh list");
        string invite = secret;
        using var member = new ApiClient(); member.Configure(endpoint);
        await member.RegisterAsync(invite, "invited-test", "member123");
        await ExpectApi(400, () => member.RegisterAsync(invite, "used-invite", "member123"));
        await member.LoginAsync("invited-test", "member123");
        await ExpectApi(403, () => member.UsersAsync());
        await ExpectApi(403, () => member.CreateInvitationAsync());
        var ordinary = new MainWindow(member, await member.MeAsync(), false);
        Capture(ordinary, "ordinary-user", 1060, 660);
        Check(((UIElement)ordinary.FindName("AdminButton")).Visibility == Visibility.Collapsed, "Ordinary user cannot see admin entry");
        ordinary.Close();
        await management.RefreshCommand.ExecuteAsync(null);
        Check(management.Invitations.Single().Status == "used", "Used invitation state refreshed");
        await management.CreateInviteCommand.ExecuteAsync(null);
        string revoked = secret;
        management.SelectedInvitation = management.Invitations.Single(i => i.Status == "available");
        await management.RevokeInviteAsync();
        Check(management.Invitations.Any(i => i.Status == "revoked"), "Revoke invitation updates state");
        await ExpectApi(400, () => member.RegisterAsync(revoked, "revoked-invite", "member123"));

        string platformId = await other.SavePlatformAsync(null, "同事的云平台", "https://example.com");
        string accountId = await other.SaveAccountAsync(null, platformId, "开发环境", "colleague@example.com", "管理员仅可查看", "other-secret");
        await management.RefreshCommand.ExecuteAsync(null);
        var target = management.Users.Single(u => u.Username == "other-test");
        var vm = window.ViewModel;
        await vm.SwitchUserAsync(target);
        Check(vm.IsReadOnly && !vm.CanEdit && vm.Accounts.Single().Id == accountId && vm.LibraryTitle.Contains(target.Username), "Switch to correct read-only owner");
        vm.SelectedAccount = vm.Accounts.Single();
        await vm.RevealPasswordCommand.ExecuteAsync(null);
        Check(vm.PasswordDisplay == "other-secret", "Admin view password route");
        string? copied = null; await vm.CopyPasswordAsync(value => copied = value);
        Check(copied == "other-secret", "Admin copy password route");
        vm.HidePassword();
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        Capture(window, "admin-readonly", 1320, 820);
        Capture(window, "admin-readonly-small", 1060, 660);
        Check(((UIElement)window.FindName("EditAccountButton")).Visibility == Visibility.Collapsed
            && ((UIElement)window.FindName("DeleteAccountButton")).Visibility == Visibility.Collapsed, "Read-only UI hides account mutation controls");
        await vm.SwitchUserAsync(null);
        Check(!vm.IsReadOnly && vm.CanEdit && vm.Accounts.Count == 4 && vm.SelectedAccount is null && !vm.PasswordVisible, "Return to own data clears foreign selection and secret");
        var ordinaryVm = new MainViewModel(other, await other.MeAsync());
        Check(!await ordinaryVm.SwitchUserAsync(target) && !ordinaryVm.IsReadOnly, "View model rejects non-admin inspection");

        management.SelectedUser = management.Users.Single(u => u.Username == "invited-test");
        await management.ToggleUserAsync();
        Check(management.SelectedUser?.Active == 0, "Disable updates selected user");
        await ExpectApi(401, () => member.MeAsync());
        await management.ToggleUserAsync();
        Check(management.SelectedUser?.Active == 1, "Enable updates selected user");
        await member.LoginAsync("invited-test", "member123");
        await management.ResetPasswordAsync();
        Check(secret.Length == 20 && management.SelectedUser?.MustChangePassword == 1, "Reset exposes temporary password once and updates status");
        await ExpectApi(401, () => member.MeAsync());
        var temporaryLogin = await member.LoginAsync("invited-test", secret);
        Check(temporaryLogin.MustChangePassword, "Temporary login requires change");
        await ExpectApi(403, () => member.AccountsAsync("", null));
        await member.ChangePasswordAsync(secret, "changed123");
        await member.LoginAsync("invited-test", "changed123");
        Check((await member.MeAsync()).Username == "invited-test", "Invitation through reset/change full flow");
        var adminWindow = new AdminWindow(api, false);
        await adminWindow.ViewModel.RefreshCommand.ExecuteAsync(null);
        adminWindow.ViewModel.SelectedUser = adminWindow.ViewModel.Users.Single(u => u.Username == "other-test");
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        await Task.Delay(250);
        Capture(adminWindow, "admin-users", 960, 680);
        ((System.Windows.Controls.TabControl)adminWindow.FindName("Tabs")).SelectedIndex = 1;
        Capture(adminWindow, "admin-invitations", 960, 680);
        adminWindow.Close();
        var register = new RegisterWindow(api); Capture(register, "register", 540, 660); register.Close();
        var once = new SecretWindow("邀请码已创建", "example-only-not-a-real-invitation", "示例：仅在此处展示一次，请复制后手动发给同事。");
        Capture(once, "one-time-secret", 580, 290); once.Close();
        // Confirm view/copy are declared separately for server auditing.
        using var handler = new AdminPasswordHandler(); using var auditApi = new ApiClient(handler);
        var auditVm = new MainViewModel(auditApi, me) { ViewedUser = target, SelectedAccount = new AccountRecord(accountId, platformId, "", "test", "") };
        await auditVm.RevealPasswordCommand.ExecuteAsync(null); await auditVm.CopyPasswordAsync(_ => { });
        Check(handler.Actions.SequenceEqual(new[] { "view", "copy" }), "Admin reveal and copy use distinct audit actions");
    }

    private sealed class AdminPasswordHandler : HttpMessageHandler
    {
        public List<string> Actions { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(token));
            Actions.Add(body.RootElement.GetProperty("action").GetString()!);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"user\":{\"id\":\"test\",\"username\":\"test\",\"role\":\"user\"},\"data\":{\"password\":\"fake\"}}", Encoding.UTF8, "application/json") };
        }
    }

    private static void Capture(Window window, string name, int width, int height)
    {
        var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        Directory.CreateDirectory("artifacts/previews"); using var file = File.Create($"artifacts/previews/{name}.png"); encoder.Save(file);
        Check(root.ActualWidth == width && root.ActualHeight == height, "Layout rendered " + name);
    }
    private sealed class DelayedHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<HttpResponseMessage> pending = new();
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token) => pending.Task;
        public void Complete() => pending.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"password\":\"late-secret\"}", Encoding.UTF8, "application/json") });
    }
}
