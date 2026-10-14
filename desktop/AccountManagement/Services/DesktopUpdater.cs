using System.IO;
using Velopack;

namespace AccountManagement.Services;

public interface IDesktopUpdater
{
    bool IsConfigured => true;
    string Source => "";
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    Task<UpdateInfo?> CheckAsync();
    Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellation);
    void PrepareRestart(UpdateInfo update);
}

public sealed class DesktopUpdater : IDesktopUpdater
{
    public static string FeedUrl => DeploymentSettings.Current.UpdateUrl;
    public bool IsConfigured => manager is not null;
    public string Source => FeedUrl;
    private readonly UpdateManager? manager;
    public DesktopUpdater() : this(CreateManager()) { }
    private static UpdateManager? CreateManager()
    {
        if (string.IsNullOrEmpty(FeedUrl)) return null;
        using var stream = typeof(DesktopUpdater).Assembly.GetManifestResourceStream("AccountManagement.UpdatePublicKey.pem")!;
        using var reader = new StreamReader(stream);
        return new UpdateManager(new SignedUpdateSource(FeedUrl, reader.ReadToEnd()), new UpdateOptions { ExplicitChannel = "win" });
    }
    internal DesktopUpdater(UpdateManager? manager) { this.manager = manager; }
    public bool IsInstalled => manager?.IsInstalled ?? false;
    public string CurrentVersion => manager?.CurrentVersion?.ToString()
        ?? typeof(App).Assembly.GetName().Version?.ToString(3) ?? "未知";
    public Task<UpdateInfo?> CheckAsync() => manager is null ? Task.FromResult<UpdateInfo?>(null) : manager.CheckForUpdatesAsync();
    public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellation)
        => (manager ?? throw new InvalidOperationException("未配置更新地址。")).DownloadUpdatesAsync(update, progress, cancellation);
    public void PrepareRestart(UpdateInfo update)
    {
        SignedUpdateSource.VerifyPackage(Path.Combine(Velopack.Locators.VelopackLocator.Current.PackagesDir!, update.TargetFullRelease.FileName), update.TargetFullRelease.SHA256);
        (manager ?? throw new InvalidOperationException("未配置更新地址。")).WaitExitThenApplyUpdates(update, silent: false, restart: true);
    }
}
