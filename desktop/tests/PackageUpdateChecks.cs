using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using AccountManagement.Services;
using Velopack;
using Velopack.Locators;

internal static class PackageUpdateChecks
{
    public static async Task Run(string releaseDirectory)
    {
        string root = Path.GetFullPath(releaseDirectory);
        string packages = Path.GetFullPath("artifacts/update-download-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(packages);
        string publicKey = File.ReadAllText("AccountManagement/UpdatePublicKey.pem");
        var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var stop = new CancellationTokenSource();
        bool corruptSignature = false;
        var server = Task.Run(async () => {
            while (!stop.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(stop.Token);
                using var stream = client.GetStream();
                using var reader = new StreamReader(stream, Encoding.ASCII, false, 1024, true);
                var request = await reader.ReadLineAsync();
                while (!string.IsNullOrEmpty(await reader.ReadLineAsync())) { }
                string file = Path.GetFileName(new Uri("http://localhost" + request!.Split(' ')[1]).AbsolutePath);
                byte[] content = File.ReadAllBytes(Path.Combine(root, file));
                if (corruptSignature && file.EndsWith(".sig")) content[0] ^= 1;
                byte[] header = Encoding.ASCII.GetBytes($"HTTP/1.1 200 OK\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n");
                await stream.WriteAsync(header, stop.Token); await stream.WriteAsync(content, stop.Token);
            }
        });
        try
        {
            var source = new SignedUpdateSource($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/", publicKey);
            var locator = new TestVelopackLocator("Shjmqqc.AccountManagement", "1.0.0", packages);
            var manager = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "win" }, locator);
            var update = await manager.CheckForUpdatesAsync() ?? throw new Exception("No newer package found");
            await manager.DownloadUpdatesAsync(update);
            SignedUpdateSource.VerifyPackage(Path.Combine(packages, update.TargetFullRelease.FileName), update.TargetFullRelease.SHA256);
            Console.WriteLine("PASS real signed HTTP feed and full package download: " + update.TargetFullRelease.Version);
            var same = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "win" },
                new TestVelopackLocator("Shjmqqc.AccountManagement", update.TargetFullRelease.Version.ToString(), packages));
            if (await same.CheckForUpdatesAsync() is not null) throw new Exception("Same version should not update");
            var newer = new UpdateManager(source, new UpdateOptions { ExplicitChannel = "win" },
                new TestVelopackLocator("Shjmqqc.AccountManagement", "99.0.0", packages));
            if (await newer.CheckForUpdatesAsync() is not null) throw new Exception("Downgrade should not update");
            corruptSignature = true;
            try { await manager.CheckForUpdatesAsync(); throw new Exception("Corrupted signature accepted"); }
            catch (CryptographicException) { Console.WriteLine("PASS HTTP signature tampering rejected; same version and downgrade ignored"); }
        }
        finally
        {
            stop.Cancel(); listener.Stop();
            try { await server; } catch (OperationCanceledException) { } catch (SocketException) { }
        }
    }
}
