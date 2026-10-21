using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace ReaKiller.Desktop;

internal sealed record AvailableUpdate(Version Version, string Notes, string MsiUrl, string ShaUrl);

internal static class UpdateService
{
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromMinutes(4) };
    private const string Latest = "https://api.github.com/repos/ZH-Systems/reakiller/releases/latest";

    static UpdateService()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ReaKiller", "1.0"));
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    public static async Task<AvailableUpdate?> CheckAsync()
    {
        using var response = await Client.GetAsync(Latest);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString()?.TrimStart('v', 'V') ?? "";
        if (!Version.TryParse(tag, out var latest)) return null;
        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        if (latest <= current) return null;
        string? msi = null, hash = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == "ReaKiller-win-x64.msi") msi = url;
            if (name == "ReaKiller-win-x64.msi.sha256") hash = url;
        }
        return msi is null || hash is null ? null :
            new AvailableUpdate(latest, root.GetProperty("body").GetString() ?? "", msi, hash);
    }

    public static async Task DownloadAndLaunchAsync(AvailableUpdate update)
    {
        var updater = Path.Combine(AppContext.BaseDirectory, "ReaKiller.Updater.exe");
        if (!File.Exists(updater)) throw new FileNotFoundException("The update helper is missing.", updater);
        var directory = Path.Combine(Path.GetTempPath(), "ReaKiller-Updates", update.Version.ToString());
        Directory.CreateDirectory(directory);
        var msi = Path.Combine(directory, "ReaKiller-win-x64.msi");
        var expectedText = await Client.GetStringAsync(update.ShaUrl);
        var sha = expectedText.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new InvalidDataException("Release checksum is invalid.");
        using (var response = await Client.GetAsync(update.MsiUrl, HttpCompletionOption.ResponseHeadersRead))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync();
            await using var output = File.Create(msi);
            await input.CopyToAsync(output);
        }
        await using (var file = File.OpenRead(msi))
        {
            if (!Convert.ToHexString(await SHA256.HashDataAsync(file)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer checksum verification failed.");
        }
        var start = new ProcessStartInfo(updater) { UseShellExecute = true };
        foreach (var arg in new[] { "--install", msi, sha, Environment.ProcessId.ToString() })
            start.ArgumentList.Add(arg);
        Process.Start(start);
        System.Windows.Application.Current.Shutdown();
    }
}
