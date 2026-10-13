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
        if (args.Length == 2 && args[0] is "--instance-probe" or "--instance-hold") return SingleInstanceChecks.Probe(args[1], args[0] == "--instance-hold");
        if (args[0] == "--single-instance")
        {
            try { SingleInstanceChecks.Run(Check); Console.WriteLine($"PASS {checks} single-instance checks"); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        if (args.Length == 2 && args[0] == "--update-package")
        {
            try { PackageUpdateChecks.Run(args[1]).GetAwaiter().GetResult(); return 0; }
            catch (Exception e) { Console.Error.WriteLine(e); return 1; }
        }
        if (args.Length == 2 && args[0] == "--lifecycle") return LifecycleChecks.Run(args[1]);
        // Load styles without running production startup or reading the user's saved session.
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/AccountManagement;component/Styles.xaml") });
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
        int code = 0;
        Dispatcher.CurrentDispatcher.BeginInvoke(async () =>
        {
            try { if (args[0] == "--layout") await AlignmentChecks.Run(Check, Capture); else await Run(args[0]); Console.WriteLine($"PASS {checks} desktop checks; previews in artifacts/previews"); }
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
        await AlignmentChecks.Run(Check, Capture);
        await BatchShareChecks.Failures(Check);
        await UpdateChecks.Run(Check, Capture);
        foreach (var bad in new[] { "https://user:password@example.com", "https://example.com/api", "ftp://localhost", "https://example.com/?token=x" })
        {
            try { ApiClient.ValidateEndpoint(bad); throw new Exception("Unsafe endpoint accepted"); }
            catch (ArgumentException) { checks++; }
        }
        Check(ApiClient.ValidateEndpoint("http://127.0.0.1:18083") == "http://127.0.0.1:18083/", "Loopback allowed");
        Check(ApiClient.ValidateEndpoint("http://example.com:18082") == "http://example.com:18082/", "Remote HTTP allowed");
        Check(ApiClient.ValidateEndpoint("https://example.com") == "https://example.com/", "HTTPS allowed");

        string sessionDir = Path.GetFullPath("artifacts/test-session-" + Guid.NewGuid().ToString("N"));
        var store = new SessionStore(sessionDir);
        Check(store.LoadEndpoint() == SessionStore.DefaultEndpoint, "First-run server default");
        var saved = new SavedSession(endpoint, "test", "secret-test-token", DateTimeOffset.UtcNow.AddDays(7).ToUnixTimeMilliseconds());
        store.Save(saved);
        Check(store.Load() == saved, "DPAPI session round-trip");
        Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(sessionDir, "session.bin"))).Contains(saved.Token), "No plaintext token on disk");
        store.Save(saved with { ExpiresAt = 1 }); Check(store.Load() is null, "Expired saved session rejected");
        File.Delete(Path.Combine(sessionDir, "server.txt"));
        Check(store.LoadEndpoint() == ApiClient.ValidateEndpoint(endpoint) && store.Load() is null, "Migrate expired legacy address without restoring token");
        Check(File.ReadAllText(Path.Combine(sessionDir, "server.txt")) == ApiClient.ValidateEndpoint(endpoint), "Address file contains only endpoint");
        store.SaveEndpoint("https://example.com");
        Check(new SessionStore(sessionDir).LoadEndpoint() == "https://example.com/", "Explicit address survives new store and overrides legacy session");
        try { store.SaveEndpoint("https://user:secret@example.com"); throw new Exception("Invalid address accepted"); }
        catch (ArgumentException) { Check(store.LoadEndpoint() == "https://example.com/", "Invalid address leaves saved address intact"); }
        File.WriteAllText(Path.Combine(sessionDir, "session.bin"), "corrupted"); Check(store.Load() is null, "Corrupted session ignored");
        store.Clear(); Check(store.Load() is null, "Session removed");
        Check(new SessionStore(sessionDir).LoadEndpoint() == "https://example.com/", "Logout preserves server address");
        File.WriteAllText(Path.Combine(sessionDir, "server.txt"), "invalid");
        Check(store.LoadEndpoint() == SessionStore.DefaultEndpoint, "Invalid settings fall back to default");
        store.SaveEndpoint(endpoint);

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
        await window.CopyRowAsync(vm.Accounts.Single(a => a.Id == secondId), false, value => copied = value);
        Check(copied == "personal@example.com" && vm.SelectedAccount?.Id == secondId, "Inline copy selects the clicked row");
        await window.CopyRowAsync(vm.Accounts.Single(a => a.Id == accountId), true, value => copied = value);
        Check(copied == "测试密码🔐" && !vm.PasswordVisible, "Inline password copy uses clicked account without revealing");
        await vm.RevealPasswordCommand.ExecuteAsync(null);
        window.SetDetailsVisible(false);
        Check(!vm.PasswordVisible && ((UIElement)window.FindName("DetailPanel")).Visibility == Visibility.Collapsed, "Collapsing details hides secrets");
        window.SetDetailsVisible(true);
        vm.SearchText = "工作"; await vm.SearchCommand.ExecuteAsync(null); Check(vm.Accounts.Count == 1, "View model search");
        vm.SearchText = ""; await vm.RefreshCommand.ExecuteAsync(null);
        vm.SelectedAccount = vm.Accounts.Single(a => a.Id == accountId);
        await Dispatcher.Yield(DispatcherPriority.ContextIdle);
        Check(vm.SearchCommand.CanExecute(null) && vm.RefreshCommand.CanExecute(null), "Commands re-enabled after async completion");
        await Task.Delay(250); // Allow WPF UI state transitions to finish before rendering.
        Check(((UIElement)window.FindName("SearchButton")).IsEnabled, "Search button enabled after refresh");
        Capture(window, "main", 1320, 820);
        Capture(window, "main-small", 1060, 660);
        var denseWindow = new MainWindow(api, me, false);
        foreach (var item in vm.Platforms) denseWindow.ViewModel.Platforms.Add(item);
        denseWindow.ViewModel.TagChoices.Add(TagRecord.All);
        var previewTags = new List<TagRecord> { new("preview-red", "正式平台", "red"), new("preview-blue", "测试帐号", "blue"), new("preview-green", "运维", "green") };
        for (int i = 0; i < 30; i++) denseWindow.ViewModel.Accounts.Add(account with { Id = "preview-" + i, Name = "生产环境帐号 " + (i + 1), Login = "team" + i + "@example.com", Tags = previewTags });
        denseWindow.ViewModel.SelectedAccount = denseWindow.ViewModel.Accounts[0];
        Check(denseWindow.ViewModel.SelectedAccount.CompactTags.Count() == 2 && denseWindow.ViewModel.SelectedAccount.ExtraTags == "+1" && denseWindow.ViewModel.SelectedAccount.TagsSummary.Contains("运维"), "Tag summary retains overflow names");
        Capture(denseWindow, "main-dense", 1320, 820);
        var table = (System.Windows.Controls.DataGrid)denseWindow.FindName("AccountList");
        Check(table.ItemContainerGenerator.ContainerFromIndex(16) is System.Windows.Controls.DataGridRow { ActualHeight: 32 }, "Dense layout fits at least seventeen rows");
        double expandedWidth = table.ActualWidth;
        denseWindow.SetDetailsVisible(false);
        Capture(denseWindow, "main-dense-collapsed", 1320, 820);
        Check(table.ActualWidth > expandedWidth + 250, "Collapsed details return width to table");
        Capture(denseWindow, "main-dense-small", 1060, 660);
        denseWindow.Close();
        var loginWindow = new LoginWindow(api, store);
        var settings = (System.Windows.Controls.Expander)loginWindow.FindName("ServerSettings");
        Check(!settings.IsExpanded && ((Wpf.Ui.Controls.TextBox)loginWindow.FindName("EndpointBox")).Text == ApiClient.ValidateEndpoint(endpoint), "Login restores server with settings collapsed");
        Capture(loginWindow, "login", 820, 620);
        settings.IsExpanded = true;
        await Task.Delay(300);
        Capture(loginWindow, "login-server-settings", 820, 620);
        ((Wpf.Ui.Controls.TextBox)loginWindow.FindName("EndpointBox")).Text = "https://example.com";
        ((Wpf.Ui.Controls.Button)loginWindow.FindName("SaveServerButton")).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Check(!settings.IsExpanded && new SessionStore(sessionDir).LoadEndpoint() == "https://example.com/", "Save server button persists and collapses settings");
        loginWindow.Close();
        var editor = new EditorWindow(api, EditorKind.Account, vm.Platforms, account: account); Capture(editor, "edit-account", 530, 680); editor.Close();
        var platformEditor = new EditorWindow(api, EditorKind.Platform, platform: platform); Capture(platformEditor, "edit-platform", 530, 460); platformEditor.Close();
        var passwordEditor = new EditorWindow(api, EditorKind.ChangePassword); Capture(passwordEditor, "change-password", 530, 540); passwordEditor.Close();
        await AdminFlow(api, me, other, endpoint, window);
        await SharingFlow(api, other, window, accountId, platformId);
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

    private static async Task SharingFlow(ApiClient owner, ApiClient receiver, MainWindow ownWindow, string accountId, string platformId)
    {
        var production = await owner.CreateTagAsync("正式平台", "red");
        var testing = await owner.CreateTagAsync("测试帐号", "blue");
        Check((await owner.CreateTagAsync("正式平台", "green")).Id == production.Id, "Tags reused within same owner");
        var otherTag = await receiver.CreateTagAsync("正式平台", "orange");
        Check(otherTag.Id != production.Id, "Same-name tag ownership isolation");
        var account = (await owner.AccountsAsync("", null)).Single(a => a.Id == accountId);
        await owner.SaveAccountAsync(account, platformId, account.Name, account.Login, account.Notes, null, [production.Id, testing.Id]);
        account = (await owner.AccountsAsync("", null)).Single(a => a.Id == accountId);
        Check(account.Tags.Count == 2, "Multiple tag assignment serialized");
        await ownWindow.ViewModel.RunAsync(() => ownWindow.ViewModel.ReloadAsync(accountId));
        Check(ownWindow.ViewModel.TagChoices.Count == 3, "Filter offers assigned tags");
        ownWindow.ViewModel.SelectedTag = production;
        Check(ownWindow.ViewModel.Accounts.Count == 1 && ownWindow.ViewModel.IsIdle, "Changing tag filters locally without starting requests");
        await ownWindow.ViewModel.RunAsync(async () => {
            ownWindow.ViewModel.SelectedTag = production; await ownWindow.ViewModel.LoadAccountsAsync(accountId);
        });
        Check(ownWindow.ViewModel.Accounts.Count == 1, "Tag filtering in view model");
        Capture(ownWindow, "tags-main", 1320, 820);
        var editor = new EditorWindow(owner, EditorKind.Account, ownWindow.ViewModel.Platforms, account: account);
        await editor.LoadTagsAsync();
        Check(((System.Windows.Controls.ListBox)editor.FindName("TagPicker")).SelectedItems.Count == 2, "Editor preselects saved tags");
        Capture(editor, "tags-editor", 530, 900); editor.Close();
        var matches = await owner.FindRecipientsAsync("other-test");
        Check(matches.Count == 1 && matches[0].Id == (await receiver.MeAsync()).Id, "Exact recipient lookup through client");
        await owner.GrantAsync(accountId, matches[0].Id);
        Check((await owner.SharesAsync(accountId)).Count == 1, "Grant and list recipients");
        await BatchShareChecks.Integration(owner, receiver, ownWindow, account, matches[0], Check, Capture);
        var outgoing = ownWindow.ViewModel;
        Check(await outgoing.SwitchOutgoingAsync(), "Open outgoing shares");
        Check(outgoing.Accounts.Single().Id == accountId && outgoing.Accounts[0].ShareCount == 1 && outgoing.CanEdit, "Outgoing lists only owned shared accounts and retains management");
        Check(outgoing.Platforms[0].AccountCount == 1, "Outgoing platform count includes only shared accounts");
        outgoing.SelectedAccount = outgoing.Accounts[0];
        Capture(ownWindow, "shared-by-me", 1060, 660);
        Check(await outgoing.SwitchSharedAsync() && !outgoing.ShowingOutgoing && outgoing.IsReadOnly, "Switch outgoing to received clears outgoing mode");
        await outgoing.SwitchOutgoingAsync();
        var shareWindow = new ShareWindow(owner, account, false); await shareWindow.LoadAsync();
        Capture(shareWindow, "share-management", 650, 650); shareWindow.Close();
        var sharedWindow = new MainWindow(receiver, await receiver.MeAsync(), false);
        var vm = sharedWindow.ViewModel;
        Check(await vm.SwitchSharedAsync(), "Open shared view");
        Check(vm.IsReadOnly && !vm.CanEdit && vm.Accounts.Single().Id == accountId && vm.Accounts[0].Tags.Count == 2, "Received view exposes only authorized tagged account");
        Check(vm.Accounts[0].OwnerUsername == "desktop-test" && vm.TagChoices.Count == 3, "Shared owner identity and filter choices");
        vm.SelectedAccount = vm.Accounts[0];
        await vm.RevealPasswordCommand.ExecuteAsync(null);
        Check(vm.PasswordVisible && vm.PasswordDisplay == "测试密码🔐", "Shared password reveal");
        vm.HidePassword();
        string? copied = null; await vm.CopyPasswordAsync(value => copied = value);
        Check(copied == "测试密码🔐", "Shared password copy");
        Capture(sharedWindow, "shared-with-me", 1320, 820);
        Capture(sharedWindow, "shared-with-me-small", 1060, 660);
        await ExpectApi(404, () => receiver.SaveAccountAsync(account, platformId, account.Name, account.Login, account.Notes, "tamper"));
        await ExpectApi(404, async () => await receiver.GrantAsync(accountId, (await owner.MeAsync()).Id));
        await owner.RevokeShareAsync(accountId, matches[0].Id);
        await outgoing.RefreshCommand.ExecuteAsync(null);
        Check(outgoing.Accounts.Count == 0 && outgoing.Platforms[0].AccountCount == 0, "Last revocation removes outgoing entry and count");
        await outgoing.SwitchUserAsync(null);
        Check(!outgoing.ShowingOutgoing && outgoing.Accounts.Any(a => a.Id == accountId), "Revoked account remains in own library");
        copied = null;
        await vm.CopyPasswordAsync(value => copied = value);
        Check(copied is null && !vm.PasswordVisible && vm.Accounts.Count == 0, "Revocation prevents copy and clears stale shared UI");
        await vm.RefreshCommand.ExecuteAsync(null);
        Check(vm.Accounts.Count == 0, "Revoked entry removed on refresh");
        await vm.SwitchUserAsync(null);
        Check(!vm.IsReadOnly && vm.Accounts.Count == 1 && vm.TagChoices.Count == 1, "Own view restored without foreign tags");
        sharedWindow.Close();
        await ownWindow.ViewModel.SwitchUserAsync(null);
        ownWindow.ViewModel.ClearData();
        Check(ownWindow.ViewModel.Accounts.Count == 0 && ownWindow.ViewModel.IsIdle, "Clearing tagged data does not reload accounts");
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
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ContextIdle);
        root.UpdateLayout();
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
