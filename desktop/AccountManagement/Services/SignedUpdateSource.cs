using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Velopack;
using Velopack.Logging;
using Velopack.Sources;

namespace AccountManagement.Services;

// HTTP is transport only. A pinned publisher key authenticates the feed and its package hashes.
public sealed class SignedUpdateSource : IUpdateSource
{
    private readonly Uri root;
    private readonly string publicKey;
    private readonly SimpleWebSource downloads;
    private VelopackAsset[] verifiedAssets = [];
    public SignedUpdateSource(string url, string publicKey)
    {
        root = new Uri(url); this.publicKey = publicKey;
        downloads = new SimpleWebSource(url);
    }
    public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
        Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
    {
        verifiedAssets = [];
        if (appId != "Shjmqqc.AccountManagement" || channel != "win") throw new InvalidDataException("Unexpected update channel.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20), MaxResponseContentBufferSize = 1024 * 1024 };
        byte[] feedBytes = await http.GetByteArrayAsync(new Uri(root, "releases.win.json"));
        byte[] signature = await http.GetByteArrayAsync(new Uri(root, "releases.win.json.sig"));
        var feed = VerifyFeed(feedBytes, signature, publicKey);
        verifiedAssets = feed.Assets;
        return feed;
    }
    internal static VelopackAssetFeed VerifyFeed(byte[] bytes, byte[] signature, string publicKey)
    {
        using var rsa = RSA.Create(); rsa.ImportFromPem(publicKey);
        if (!rsa.VerifyData(bytes, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pss))
            throw new CryptographicException("Update signature verification failed.");
        var feed = VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(bytes));
        foreach (var asset in feed.Assets)
            if (asset.PackageId != "Shjmqqc.AccountManagement" || !Regex.IsMatch(asset.FileName, @"\A[A-Za-z0-9][A-Za-z0-9._-]*\.nupkg\z")
                || !Regex.IsMatch(asset.SHA256 ?? "", @"\A[0-9a-fA-F]{64}\z"))
                throw new InvalidDataException("Invalid signed package metadata.");
        return feed;
    }
    public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
        Action<int> progress, CancellationToken cancelToken = default)
    {
        if (!verifiedAssets.Any(a => a.FileName == releaseEntry.FileName && a.SHA256 == releaseEntry.SHA256))
            throw new CryptographicException("Package was not in a verified feed.");
        await downloads.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
        VerifyPackage(localFile, releaseEntry.SHA256);
    }
    internal static void VerifyPackage(string path, string expected)
    {
        using var file = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("Update package verification failed.");
    }
}
