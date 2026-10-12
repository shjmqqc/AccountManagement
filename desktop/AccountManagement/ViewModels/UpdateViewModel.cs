using AccountManagement.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Velopack;

namespace AccountManagement.ViewModels;

public partial class UpdateViewModel(IDesktopUpdater updater) : ObservableObject
{
    private UpdateInfo? available;
    private CancellationTokenSource? cancellation;
    public string CurrentVersion => updater.CurrentVersion;
    public string Source => DesktopUpdater.FeedUrl;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCheck), nameof(CanDownload), nameof(CanRestart))]
    private bool isBusy;
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanDownload), nameof(CanRestart))]
    private bool isReady;
    [ObservableProperty] private int progress;
    [ObservableProperty] private string status = "准备检查更新";
    [ObservableProperty] private string releaseNotes = "";
    [ObservableProperty] private string menuText = "检查更新";
    public bool CanCheck => !IsBusy && !IsReady;
    public bool CanDownload => !IsBusy && !IsReady && available is not null;
    public bool CanRestart => !IsBusy && IsReady;
    public async Task CheckAsync()
    {
        if (!CanCheck) return;
        if (!updater.IsInstalled) { Status = "当前为开发版或旧解压版，请先安装新版 Setup.exe，再使用自动更新。"; return; }
        IsBusy = true; available = null; ReleaseNotes = ""; Status = "正在检查更新…";
        try
        {
            available = await updater.CheckAsync();
            Status = available is null ? "当前已是最新版本。" : $"发现新版本 {available.TargetFullRelease.Version}";
            MenuText = available is null ? "检查更新" : Status;
            ReleaseNotes = available?.TargetFullRelease.NotesMarkdown ?? "";
        }
        catch (Exception) { Status = "检查或签名验证失败，请检查网络和服务器发布文件后重试。"; MenuText = "检查更新"; }
        finally { IsBusy = false; }
    }
    public async Task DownloadAsync()
    {
        if (!CanDownload || available is null) return;
        IsBusy = true; Progress = 0; Status = "正在下载并验证更新…";
        using var token = new CancellationTokenSource(); cancellation = token;
        var progress = new Progress<int>(value => Progress = Math.Clamp(value, 0, 100));
        try
        {
            await updater.DownloadAsync(available, value => ((IProgress<int>)progress).Report(value), token.Token);
            IsReady = true; Progress = 100; Status = "下载及校验完成，点击“重启并更新”安装。"; MenuText = "更新已就绪";
        }
        catch (OperationCanceledException) { Status = "下载已取消，可以重新下载。"; }
        catch (Exception) { Status = "下载或校验失败，未安装更新。请重试。"; }
        finally { cancellation = null; IsBusy = false; }
    }
    public void CancelDownload() => cancellation?.Cancel();
    public bool PrepareRestart()
    {
        if (!CanRestart || available is null) return false;
        try { updater.PrepareRestart(available); return true; }
        catch (Exception) { Status = "无法验证安装包或启动更新程序，请重试。"; return false; }
    }
}
