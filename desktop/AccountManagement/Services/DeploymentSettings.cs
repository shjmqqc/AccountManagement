using System.IO;
using System.Text.Json;

namespace AccountManagement.Services;

public sealed class DeploymentSettings
{
    public string ServerUrl { get; init; } = "http://127.0.0.1:18082/";
    public string UpdateUrl { get; init; } = "";
    private static readonly Lazy<DeploymentSettings> current = new(() => Load(AppContext.BaseDirectory,
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AccountManagement")));
    public static DeploymentSettings Current => current.Value;

    internal static DeploymentSettings Load(string applicationDirectory, string userDirectory)
    {
        // A complete user configuration survives replacement of the installed application directory.
        var path = Path.Combine(userDirectory, "appsettings.json");
        if (!File.Exists(path)) path = Path.Combine(applicationDirectory, "appsettings.json");
        var settings = File.Exists(path)
            ? JsonSerializer.Deserialize<DeploymentSettings>(File.ReadAllText(path))
                ?? throw new ArgumentException("appsettings.json 不能为空。")
            : new DeploymentSettings();
        if (string.IsNullOrWhiteSpace(settings.ServerUrl)) throw new ArgumentException("ServerUrl 不能为空。");
        var server = ApiClient.ValidateEndpoint(settings.ServerUrl);
        var update = (settings.UpdateUrl ?? "").Trim();
        if (update.Length > 0)
        {
            if (!Uri.TryCreate(update, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
                || uri.Host.Length == 0 || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
                throw new ArgumentException("UpdateUrl 须为 HTTP 或 HTTPS 更新目录地址，不能包含用户名、密码、查询参数或片段。");
            update = uri.AbsoluteUri.TrimEnd('/') + "/";
        }
        return new DeploymentSettings { ServerUrl = server, UpdateUrl = update };
    }
}
