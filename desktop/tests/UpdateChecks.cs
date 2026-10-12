using System.IO;
using System.Security.Cryptography;
using System.Text;
using AccountManagement.Services;
using AccountManagement.ViewModels;
using AccountManagement.Views;
using Velopack;

internal static class UpdateChecks
{
    public static async Task Run(Action<bool, string> check, Action<System.Windows.Window, string, int, int> capture)
    {
        using var rsa = RSA.Create(2048);
        const string json = """
        {"Assets":[{"PackageId":"Shjmqqc.AccountManagement","Version":"1.1.1","Type":"Full","FileName":"AccountManagement-1.1.1-full.nupkg","SHA256":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA","Size":10,"NotesMarkdown":"更新验证版本"}]}
        """;
        byte[] bytes = Encoding.UTF8.GetBytes(json);
        byte[] signature = rsa.SignData(bytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pss);
        var feed = SignedUpdateSource.VerifyFeed(bytes, signature, rsa.ExportSubjectPublicKeyInfoPem());
        check(feed.Assets.Length == 1, "Signed feed verified");
        void Reject(Action action, string label)
        {
            try { action(); throw new Exception("Accepted invalid update: " + label); }
            catch (Exception e) when (e is CryptographicException or InvalidDataException or FormatException) { check(true, label); }
        }
        byte[] altered = Encoding.UTF8.GetBytes(json.Replace("1.1.1", "9.9.9"));
        Reject(() => SignedUpdateSource.VerifyFeed(altered, signature, rsa.ExportSubjectPublicKeyInfoPem()), "Tampered version rejected");
        using var wrongKey = RSA.Create(2048);
        Reject(() => SignedUpdateSource.VerifyFeed(bytes, signature, wrongKey.ExportSubjectPublicKeyInfoPem()), "Wrong publisher rejected");
        Reject(() => SignedUpdateSource.VerifyFeed(bytes, [], rsa.ExportSubjectPublicKeyInfoPem()), "Missing signature rejected");
        var traversal = Encoding.UTF8.GetBytes(json.Replace("AccountManagement-1.1.1-full.nupkg", "../evil.nupkg"));
        Reject(() => SignedUpdateSource.VerifyFeed(traversal, rsa.SignData(traversal, HashAlgorithmName.SHA256, RSASignaturePadding.Pss), rsa.ExportSubjectPublicKeyInfoPem()), "Signed path traversal rejected");
        Directory.CreateDirectory("artifacts/update-checks");
        const string path = "artifacts/update-checks/package.bin";
        File.WriteAllBytes(path, bytes);
        SignedUpdateSource.VerifyPackage(path, Convert.ToHexString(SHA256.HashData(bytes)));
        check(true, "Package hash verified");
        File.AppendAllText(path, "tampered");
        Reject(() => SignedUpdateSource.VerifyPackage(path, Convert.ToHexString(SHA256.HashData(bytes))), "Tampered package rejected");
        var fake = new FakeUpdater { Update = new UpdateInfo(feed.Assets[0], false, null!, []) };
        var vm = new UpdateViewModel(fake);
        check(!vm.PrepareRestart(), "Cannot restart before download");
        await vm.CheckAsync(); check(vm.CanDownload && !vm.CanRestart, "Available update awaits user download");
        var window = new UpdateWindow(vm); capture(window, "updates-available", 590, 550); window.Close();
        fake.FailDownload = true; await vm.DownloadAsync(); check(!vm.CanRestart && vm.CanDownload, "Failed download cannot install");
        fake.FailDownload = false; fake.WaitDownload = true;
        var downloading = vm.DownloadAsync(); vm.CancelDownload(); await downloading;
        check(!vm.IsReady && !vm.IsBusy, "Canceled download cannot install");
        fake.WaitDownload = false; await vm.DownloadAsync();
        check(vm.CanRestart && !vm.CanDownload && fake.Restarts == 0, "Download never automatically restarts");
        fake.FailRestart = true; check(!vm.PrepareRestart(), "Updater launch failure retains app");
        fake.FailRestart = false; check(vm.PrepareRestart() && fake.Restarts == 1, "Explicit restart starts updater");
        fake.Update = null; var current = new UpdateViewModel(fake); await current.CheckAsync();
        check(!current.CanDownload && !current.IsBusy, "No update available");
        fake.FailCheck = true; await current.CheckAsync(); check(current.CanCheck && !current.CanDownload, "Check failure remains retryable");
        fake.Installed = false; var unpacked = new UpdateViewModel(fake); await unpacked.CheckAsync();
        check(unpacked.Status.Contains("Setup.exe"), "Unmanaged ZIP explains first install");
    }
    private sealed class FakeUpdater : IDesktopUpdater
    {
        public bool Installed = true, FailCheck, FailDownload, WaitDownload, FailRestart;
        public int Restarts;
        public UpdateInfo? Update;
        public bool IsInstalled => Installed;
        public string CurrentVersion => "1.1.0";
        public Task<UpdateInfo?> CheckAsync() => FailCheck ? Task.FromException<UpdateInfo?>(new IOException()) : Task.FromResult(Update);
        public async Task DownloadAsync(UpdateInfo update, Action<int> progress, CancellationToken cancellation)
        {
            if (FailDownload) throw new CryptographicException();
            if (WaitDownload) await Task.Delay(Timeout.Infinite, cancellation);
            progress(100);
        }
        public void PrepareRestart(UpdateInfo update) { if (FailRestart) throw new IOException(); Restarts++; }
    }
}
