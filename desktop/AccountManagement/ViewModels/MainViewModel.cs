using System.Collections.ObjectModel;
using AccountManagement.Models;
using AccountManagement.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountManagement.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ApiClient api;
    private int selectionVersion;
    private List<AccountRecord> loadedAccounts = [];
    public UserRecord User { get; }
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnly), nameof(CanEdit), nameof(LibraryTitle), nameof(LibrarySubtitle))]
    private ManagedUser? viewedUser;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReadOnly), nameof(CanEdit), nameof(LibraryTitle), nameof(LibrarySubtitle))]
    private bool showingShared;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LibraryTitle), nameof(LibrarySubtitle))]
    private bool showingOutgoing;
    [ObservableProperty] private TagRecord? selectedTag;
    public ObservableCollection<TagRecord> TagChoices { get; } = [];
    public bool IsAdmin => User.Role == "admin";
    public bool IsReadOnly => ViewedUser is not null || ShowingShared;
    public bool CanEdit => !IsReadOnly;
    public string LibraryTitle => ShowingOutgoing ? "分享给别人" : ShowingShared ? "分享给我" : ViewedUser is null ? "我的帐号库" : $"{ViewedUser.Username} 的帐号库";
    public string LibrarySubtitle => ShowingOutgoing ? "已分享的帐号 · 查看接收人或撤销分享" : ShowingShared ? "接收的帐号 · 只读 · 可由所有者撤销" : IsReadOnly ? "管理员只读查看 · 密码操作会记录审计" : "平台与登录信息，集中整理";

    public Task<bool> SwitchUserAsync(ManagedUser? user) => RunAsync(async () =>
    {
        if (user is not null && !IsAdmin) throw new ArgumentException("此功能仅限管理员使用。");
        ClearData(); SelectedPlatform = null; ViewedUser = user; ShowingShared = false; ShowingOutgoing = false;
        await ReloadAsync();
    });
    public Task<bool> SwitchSharedAsync() => RunAsync(async () =>
    {
        ClearData(); SelectedPlatform = null; ViewedUser = null; ShowingShared = true; ShowingOutgoing = false;
        await ReloadAsync();
    });
    public Task<bool> SwitchOutgoingAsync() => RunAsync(async () =>
    {
        ClearData(); SelectedPlatform = null; ViewedUser = null; ShowingShared = false; ShowingOutgoing = true;
        await ReloadAsync();
    });
    public ObservableCollection<PlatformRecord> Platforms { get; } = [];
    public ObservableCollection<AccountRecord> Accounts { get; } = [];
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CheckedCount), nameof(CanBatchShare))]
    private HashSet<string> checkedAccountIds = [];
    public int CheckedCount => CheckedAccountIds.Count;
    public bool CanBatchShare => CanEdit && CheckedCount > 0;
    public void ClearChecks() => CheckedAccountIds = [];
    public void ToggleCheck(AccountRecord account)
    {
        if (!CanEdit || IsBusy || !Accounts.Contains(account)) return;
        var ids = new HashSet<string>(CheckedAccountIds);
        if (!ids.Add(account.Id)) ids.Remove(account.Id);
        CheckedAccountIds = ids;
    }
    public void CheckAll()
    {
        if (CanEdit && !IsBusy) CheckedAccountIds = Accounts.Select(a => a.Id).ToHashSet();
    }
    public event Action? SessionExpired;

    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(SearchCommand), nameof(RevealPasswordCommand))]
    private bool isBusy;
    [ObservableProperty] private string searchText = "";
    [ObservableProperty] private string status = "准备就绪";
    [ObservableProperty] private PlatformRecord? selectedPlatform;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(HasSelection), nameof(DetailPlatform), nameof(DetailUrl))]
    [NotifyCanExecuteChangedFor(nameof(RevealPasswordCommand))]
    private AccountRecord? selectedAccount;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(RevealLabel))] private bool passwordVisible;
    [ObservableProperty] private string passwordDisplay = "••••••••";
    [ObservableProperty] private string listTitle = "全部帐号";

    public bool IsIdle => !IsBusy;
    public bool HasSelection => SelectedAccount is not null;
    public string RevealLabel => PasswordVisible ? "隐藏" : "显示";
    public string DetailPlatform => ShowingShared ? SelectedAccount?.PlatformName ?? "" : Platforms.FirstOrDefault(p => p.Id == SelectedAccount?.PlatformId)?.Name ?? "";
    public string DetailUrl => ShowingShared ? SelectedAccount?.PlatformUrl ?? "" : Platforms.FirstOrDefault(p => p.Id == SelectedAccount?.PlatformId)?.Url ?? "";
    public string Identity => $"{User.Username} · {(User.Role == "admin" ? "管理员" : "用户")}";
    private bool CanRun() => !IsBusy;
    private bool CanReveal() => !IsBusy && HasSelection;

    public MainViewModel(ApiClient api, UserRecord user) { this.api = api; User = user; }

    partial void OnSelectedAccountChanged(AccountRecord? value) { HidePassword(); }
    partial void OnSelectedTagChanged(TagRecord? value) { ClearChecks(); if (IsIdle) ApplyFilter(); }
    partial void OnSearchTextChanged(string value) => ClearChecks();
    partial void OnSelectedPlatformChanged(PlatformRecord? value) => ClearChecks();
    public void HidePassword() { selectionVersion++; PasswordDisplay = "••••••••"; PasswordVisible = false; }

    public async Task<bool> RunAsync(Func<Task> action, string? success = null)
    {
        if (IsBusy) return false;
        IsBusy = true;
        Status = "正在处理…";
        try { await action(); Status = success ?? "已同步"; return true; }
        catch (ApiException e)
        {
            Status = e.Message;
            if (ShowingShared && e.Status == 404) ClearData();
            if (e.Status == 401 || e.Code == "password_change_required") { ClearData(); SessionExpired?.Invoke(); }
            return false;
        }
        catch (Exception e) when (e is ArgumentException or System.IO.IOException or System.Runtime.InteropServices.ExternalException)
        { Status = e is ArgumentException ? e.Message : "操作未完成，请稍后再试。"; return false; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Refresh() => RunAsync(() => ReloadAsync());

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Search() => RunAsync(() => LoadAccountsAsync());

    public async Task ReloadAsync(string? selectAccountId = null, string? selectPlatformId = null)
    {
        HidePassword();
        var selectedId = selectPlatformId ?? SelectedPlatform?.Id ?? "";
        var platforms = ShowingShared ? (await api.SharedAccountsAsync()).GroupBy(a => a.PlatformId)
            .Select(g => new PlatformRecord(g.Key, g.First().PlatformName + " · " + g.First().OwnerUsername, g.First().PlatformUrl, AccountCount: g.Count())).ToList()
            : ViewedUser is null ? await api.PlatformsAsync() : await api.UserPlatformsAsync(ViewedUser.Id);
        Platforms.Clear();
        Platforms.Add(PlatformRecord.All with { AccountCount = platforms.Sum(p => p.AccountCount) });
        foreach (var platform in platforms) Platforms.Add(platform);
        SelectedPlatform = Platforms.FirstOrDefault(p => p.Id == selectedId) ?? Platforms[0];
        await LoadAccountsAsync(selectAccountId);
        OnPropertyChanged(nameof(DetailUrl)); OnPropertyChanged(nameof(DetailPlatform));
    }

    public async Task LoadAccountsAsync(string? selectAccountId = null)
    {
        ClearChecks();
        HidePassword();
        SelectedAccount = null;
        Accounts.Clear();
        var records = ShowingShared ? await api.SharedAccountsAsync() : ViewedUser is null ? await api.AccountsAsync("", null)
            : await api.UserAccountsAsync(ViewedUser.Id, "", null);
        if (ShowingOutgoing)
        {
            if (records.Any(a => a.ShareCount is null)) throw new ArgumentException("请先更新服务器程序，以支持分享给别人的帐号列表。");
            records = records.Where(a => a.ShareCount > 0).ToList();
            var selectedPlatformId = SelectedPlatform?.Id;
            var platforms = (await api.PlatformsAsync())
                .Select(p => p with { AccountCount = records.Count(a => a.PlatformId == p.Id) }).Where(p => p.AccountCount > 0).ToList();
            Platforms.Clear(); Platforms.Add(PlatformRecord.All with { AccountCount = records.Count });
            foreach (var platform in platforms) Platforms.Add(platform);
            SelectedPlatform = Platforms.FirstOrDefault(p => p.Id == selectedPlatformId) ?? Platforms[0];
        }
        loadedAccounts = records;
        var tagId = SelectedTag?.Id;
        TagChoices.Clear(); TagChoices.Add(TagRecord.All);
        foreach (var tag in records.SelectMany(a => a.Tags.Select(t => ShowingShared ? t with { Name = t.Name + " · " + a.OwnerUsername } : t))
            .DistinctBy(t => t.Id).OrderBy(t => t.Name)) TagChoices.Add(tag);
        SelectedTag = TagChoices.FirstOrDefault(t => t.Id == tagId) ?? TagChoices[0];
        ApplyFilter(selectAccountId);
    }

    private void ApplyFilter(string? selectAccountId = null)
    {
        ClearChecks();
        HidePassword(); SelectedAccount = null; Accounts.Clear();
        var query = SearchText.Trim();
        var records = loadedAccounts.Where(a => (query.Length > 0
            ? new[] { a.PlatformName, a.Name, a.Login, a.Notes }.Any(text => text.Contains(query, StringComparison.OrdinalIgnoreCase))
            : string.IsNullOrEmpty(SelectedPlatform?.Id) || a.PlatformId == SelectedPlatform.Id)
            && (string.IsNullOrEmpty(SelectedTag?.Id) || a.Tags.Any(t => t.Id == SelectedTag.Id))).ToList();
        foreach (var account in records) Accounts.Add(account);
        ListTitle = query.Length > 0 ? $"搜索结果 · {records.Count}" : $"{SelectedPlatform?.Name ?? "全部帐号"} · {records.Count}";
        SelectedAccount = selectAccountId is null ? null : Accounts.FirstOrDefault(a => a.Id == selectAccountId);
    }

    [RelayCommand(CanExecute = nameof(CanReveal))]
    private async Task RevealPassword()
    {
        if (PasswordVisible) { HidePassword(); return; }
        var selected = SelectedAccount;
        var version = selectionVersion;
        if (selected is null) return;
        await RunAsync(async () =>
        {
            var password = ShowingShared ? await api.SharedPasswordAsync(selected.Id, "view") : ViewedUser is null ? await api.PasswordAsync(selected.Id)
                : await api.UserPasswordAsync(ViewedUser.Id, selected.Id, "view");
            if (version != selectionVersion || SelectedAccount?.Id != selected.Id) return;
            PasswordDisplay = password.Length == 0 ? "（未设置密码）" : password;
            PasswordVisible = true;
        }, "密码已显示");
    }

    public Task CopyPasswordAsync(Action<string> copy) => CopySecretAsync(copy, false);
    public Task CopyAllAsync(Action<string> copy) => CopySecretAsync(copy, true);

    private async Task CopySecretAsync(Action<string> copy, bool wholeAccount)
    {
        var selected = SelectedAccount;
        var version = selectionVersion;
        if (selected is null) return;
        var platform = DetailPlatform;
        var url = DetailUrl;
        await RunAsync(async () =>
        {
            var password = ShowingShared ? await api.SharedPasswordAsync(selected.Id, "copy") : ViewedUser is null ? await api.PasswordAsync(selected.Id)
                : await api.UserPasswordAsync(ViewedUser.Id, selected.Id, "copy");
            if (version == selectionVersion && SelectedAccount?.Id == selected.Id)
                copy(wholeAccount ? $"【平台名称】{platform}\n【地址】{url}\n【帐号】{selected.Login}\n【密码】{password}" : password);
        }, wholeAccount ? "帐号信息已整体复制" : "密码已复制");
    }

    public void ClearData()
    {
        ClearChecks();
        loadedAccounts.Clear();
        HidePassword(); SelectedAccount = null; Accounts.Clear(); Platforms.Clear(); SearchText = ""; TagChoices.Clear(); SelectedTag = null;
    }
}
