using System.IO;
using Velopack;

namespace AccountManagement.Services;

public interface IDesktopUpdater
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    Task<UpdateInfo?> CheckAsync();
    Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellation);
    void PrepareRestart(UpdateInfo update);
}

public sealed class DesktopUpdater : IDesktopUpdater
{
    public const string FeedUrl = "http://www.shjpnn.com/updates/windows/";
    private readonly UpdateManager manager;
    public DesktopUpdater() : this(CreateManager()) { }
    private static UpdateManager CreateManager()
    {
        using var stream = typeof(DesktopUpdater).Assembly.GetManifestResourceStream("AccountManagement.UpdatePublicKey.pem")!;
        using var reader = new StreamReader(stream);
        return new UpdateManager(new SignedUpdateSource(FeedUrl, reader.ReadToEnd()), new UpdateOptions { ExplicitChannel = "win" });
    }
    internal DesktopUpdater(UpdateManager manager) { this.manager = manager; }
    public bool IsInstalled => manager.IsInstalled;
    public string CurrentVersion => manager.CurrentVersion?.ToString()
        ?? typeof(App).Assembly.GetName().Version?.ToString(3) ?? "未知";
    public Task<UpdateInfo?> CheckAsync() => manager.CheckForUpdatesAsync();
    public Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellation)
        => manager.DownloadUpdatesAsync(update, progress, cancellation);
    public void PrepareRestart(UpdateInfo update)
    {
        SignedUpdateSource.VerifyPackage(Path.Combine(Velopack.Locators.VelopackLocator.Current.PackagesDir!, update.TargetFullRelease.FileName), update.TargetFullRelease.SHA256);
        manager.WaitExitThenApplyUpdates(update, silent: false, restart: true);
    }
}
