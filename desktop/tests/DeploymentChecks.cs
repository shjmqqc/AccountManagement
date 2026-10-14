using System.IO;
using System.Text.Json;
using AccountManagement.Services;
using AccountManagement.ViewModels;

internal static class DeploymentChecks
{
    public static async Task Run(Action<bool, string> check)
    {
        var root = Path.GetFullPath("artifacts/config-test-" + Guid.NewGuid().ToString("N"));
        var app = Path.Combine(root, "app"); var user = Path.Combine(root, "user");
        Directory.CreateDirectory(app); Directory.CreateDirectory(user);
        void Write(string dir, string server, string update) => File.WriteAllText(Path.Combine(dir, "appsettings.json"),
            JsonSerializer.Serialize(new DeploymentSettings { ServerUrl = server, UpdateUrl = update }));
        check(DeploymentSettings.Load(app, user).UpdateUrl == "", "No deployment config disables updates");
        Write(app, "https://api.example.com", "https://updates.example.com/windows");
        var settings = DeploymentSettings.Load(app, user);
        check(settings.ServerUrl == "https://api.example.com/" && settings.UpdateUrl == "https://updates.example.com/windows/", "Deployment URLs normalized");
        Write(user, "http://intranet:18082", "");
        check(DeploymentSettings.Load(app, user).ServerUrl == "http://intranet:18082/" && DeploymentSettings.Load(app, user).UpdateUrl == "", "User config overrides installation and can disable updates");
        foreach (var url in new[] { "file:///tmp/updates", "https://u:p@example.com/", "https://example.com/?token=x", "https://example.com/#fragment" })
        {
            Write(user, "https://api.example.com", url);
            try { DeploymentSettings.Load(app, user); throw new Exception("Invalid update URL accepted"); }
            catch (ArgumentException) { check(true, "Unsafe update URL rejected"); }
        }
        File.WriteAllText(Path.Combine(user, "appsettings.json"), "{");
        try { DeploymentSettings.Load(app, user); throw new Exception("Malformed config accepted"); }
        catch (JsonException) { check(true, "Invalid user config does not silently use another server"); }
        File.WriteAllText(Path.Combine(user, "appsettings.json"), "{\"ServerUrl\":null}");
        try { DeploymentSettings.Load(app, user); throw new Exception("Null server accepted"); }
        catch (ArgumentException) { check(true, "Null server produces configuration error"); }
        var model = new UpdateViewModel(new DesktopUpdater(null));
        await model.CheckAsync();
        check(model.Status.Contains("未启用") && !model.CanDownload, "Disabled updater reports configuration without network requests");
    }
}
