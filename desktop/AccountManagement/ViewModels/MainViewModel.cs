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
    public UserRecord User { get; }
    public ObservableCollection<PlatformRecord> Platforms { get; } = [];
    public ObservableCollection<AccountRecord> Accounts { get; } = [];
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
    public string DetailPlatform => Platforms.FirstOrDefault(p => p.Id == SelectedAccount?.PlatformId)?.Name ?? "";
    public string DetailUrl => Platforms.FirstOrDefault(p => p.Id == SelectedAccount?.PlatformId)?.Url ?? "";
    public string Identity => $"{User.Username} · {(User.Role == "admin" ? "管理员" : "用户")}";
    private bool CanRun() => !IsBusy;
    private bool CanReveal() => !IsBusy && HasSelection;

    public MainViewModel(ApiClient api, UserRecord user) { this.api = api; User = user; }

    partial void OnSelectedAccountChanged(AccountRecord? value) { HidePassword(); }
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
        var platforms = await api.PlatformsAsync();
        Platforms.Clear();
        Platforms.Add(PlatformRecord.All with { AccountCount = platforms.Sum(p => p.AccountCount) });
        foreach (var platform in platforms) Platforms.Add(platform);
        SelectedPlatform = Platforms.FirstOrDefault(p => p.Id == selectedId) ?? Platforms[0];
        await LoadAccountsAsync(selectAccountId);
        OnPropertyChanged(nameof(DetailUrl)); OnPropertyChanged(nameof(DetailPlatform));
    }

    public async Task LoadAccountsAsync(string? selectAccountId = null)
    {
        HidePassword();
        SelectedAccount = null;
        Accounts.Clear();
        var query = SearchText.Trim();
        var records = await api.AccountsAsync(query, SelectedPlatform?.Id);
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
            var password = await api.PasswordAsync(selected.Id);
            if (version != selectionVersion || SelectedAccount?.Id != selected.Id) return;
            PasswordDisplay = password.Length == 0 ? "（未设置密码）" : password;
            PasswordVisible = true;
        }, "密码已显示");
    }

    public async Task CopyPasswordAsync(Action<string> copy)
    {
        var selected = SelectedAccount;
        var version = selectionVersion;
        if (selected is null) return;
        await RunAsync(async () =>
        {
            var password = await api.PasswordAsync(selected.Id);
            if (version == selectionVersion && SelectedAccount?.Id == selected.Id) copy(password);
        }, "密码已复制");
    }

    public void ClearData()
    {
        HidePassword(); SelectedAccount = null; Accounts.Clear(); Platforms.Clear(); SearchText = "";
    }
}
