using System.Collections.ObjectModel;
using AccountManagement.Models;
using AccountManagement.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AccountManagement.ViewModels;

public partial class AdminViewModel(ApiClient api) : ObservableObject
{
    public ObservableCollection<ManagedUser> Users { get; } = [];
    public ObservableCollection<InvitationRecord> Invitations { get; } = [];
    public event Action? SessionExpired;
    public event Action<string, string, string>? SecretReady;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(IsIdle))]
    [NotifyCanExecuteChangedFor(nameof(RefreshCommand), nameof(CreateInviteCommand), nameof(MoreInvitesCommand))]
    private bool isBusy;
    [ObservableProperty] private string status = "准备就绪";
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanManageUser), nameof(CanViewUser), nameof(ToggleLabel))]
    private ManagedUser? selectedUser;
    [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanRevoke))] private InvitationRecord? selectedInvitation;
    [ObservableProperty] [NotifyCanExecuteChangedFor(nameof(MoreInvitesCommand))] private bool hasMoreInvites;
    public bool IsIdle => !IsBusy;
    public bool CanManageUser => SelectedUser is { Role: "user" };
    public bool CanViewUser => SelectedUser is not null;
    public bool CanRevoke => SelectedInvitation is { Status: "available" or "expired" };
    public string ToggleLabel => SelectedUser?.Active == 0 ? "恢复启用" : "停用用户";
    private bool CanRun() => IsIdle;
    private bool CanLoadMore() => IsIdle && HasMoreInvites;

    public async Task<bool> RunAsync(Func<Task> operation, string success = "已刷新")
    {
        if (IsBusy) return false;
        IsBusy = true; Status = "正在处理…";
        try { await operation(); Status = success; return true; }
        catch (ApiException e)
        {
            Status = e.Message;
            if (e.Status is 401 or 403) { Users.Clear(); Invitations.Clear(); SessionExpired?.Invoke(); }
            return false;
        }
        finally { IsBusy = false; }
    }

    private async Task LoadUsersAsync()
    {
        var id = SelectedUser?.Id;
        var users = await api.UsersAsync();
        Users.Clear(); foreach (var user in users) Users.Add(user);
        SelectedUser = Users.FirstOrDefault(u => u.Id == id);
    }
    private async Task LoadInvitesAsync(bool more = false)
    {
        var id = SelectedInvitation?.Id;
        var records = await api.InvitationsAsync(more ? Invitations.LastOrDefault()?.Id : null);
        if (!more) Invitations.Clear();
        foreach (var record in records) Invitations.Add(record);
        SelectedInvitation = Invitations.FirstOrDefault(i => i.Id == id);
        HasMoreInvites = records.Count == 100;
    }
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task Refresh() => RunAsync(async () => { await LoadUsersAsync(); await LoadInvitesAsync(); });
    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task MoreInvites() => RunAsync(() => LoadInvitesAsync(true));
    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task CreateInvite() => RunAsync(async () =>
    {
        var invite = await api.CreateInvitationAsync();
        SecretReady?.Invoke("邀请码已创建", invite.InviteCode,
            $"有效期至 {DateTimeOffset.FromUnixTimeMilliseconds(invite.ExpiresAt).ToLocalTime():yyyy-MM-dd HH:mm}。仅能注册一次。\n请现在复制并手动发给同事，关闭后无法再次查看。遗失时可撤销并重新创建。");
        await LoadInvitesAsync();
    }, "邀请码已创建");
    public Task ToggleUserAsync()
    {
        var user = SelectedUser;
        if (user is not { Role: "user" }) return Task.CompletedTask;
        return RunAsync(async () => { await api.SetUserActiveAsync(user.Id, user.Active == 0); await LoadUsersAsync(); },
            user.Active == 0 ? "用户已恢复，请重新登录" : "用户已停用，全部旧会话已撤销");
    }
    public Task ResetPasswordAsync()
    {
        var user = SelectedUser;
        if (user is not { Role: "user" }) return Task.CompletedTask;
        return RunAsync(async () =>
        {
            var result = await api.ResetPasswordAsync(user.Id);
            SecretReady?.Invoke($"{user.Username} · 临时密码", result.TemporaryPassword,
                "全部旧会话已撤销。请现在复制并手动交给本人，登录后必须修改。\n关闭后无法再次查看；如遗失可再次重置。已停用的用户仍需先恢复启用。");
            await LoadUsersAsync();
        }, "登录密码已重置");
    }
    public Task RevokeInviteAsync()
    {
        var invite = SelectedInvitation;
        if (!CanRevoke || invite is null) return Task.CompletedTask;
        return RunAsync(async () => { await api.RevokeInvitationAsync(invite.Id); await LoadInvitesAsync(); }, "邀请已撤销");
    }
}
