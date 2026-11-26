using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;

namespace Transflux.Desktop;

internal sealed record AvailableUpdate(Version Version, string Notes, string MsiUrl, string ShaUrl);

internal static class UpdateService
{
    private static readonly HttpClient Client = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromMinutes(4) };
    private const string Latest = "https://api.github.com/repos/ZH-Systems/reakiller/releases/latest";

    static UpdateService()
    {
        Client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Transflux", "1.0"));
        Client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
    }

    private static bool AllowedAsset(string url, string tag, string name)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" &&
            uri.Host.Equals("github.com", StringComparison.OrdinalIgnoreCase) && uri.IsDefaultPort &&
            string.IsNullOrEmpty(uri.UserInfo) && string.IsNullOrEmpty(uri.Query) && string.IsNullOrEmpty(uri.Fragment) &&
            uri.AbsolutePath == "/ZH-Systems/reakiller/releases/download/" + tag + "/" + name;
    }

    private static async Task<byte[]> ReadLimited(HttpResponseMessage response, int max)
    {
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > max) throw new InvalidDataException("Update response exceeds limit.");
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var memory = new MemoryStream();
        var buffer = new byte[8192];
        int n;
        using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(60));
        while ((n = await stream.ReadAsync(buffer.AsMemory(), deadline.Token)) > 0)
        {
            if (memory.Length + n > max) throw new InvalidDataException("Update response exceeds limit.");
            memory.Write(buffer, 0, n);
        }
        return memory.ToArray();
    }

    private static async Task<HttpResponseMessage> GetAsset(string url)
    {
        for (var i = 0; i < 5; i++)
        {
            var uri = new Uri(url);
            if (uri.Scheme != "https" || !uri.IsDefaultPort || !string.IsNullOrEmpty(uri.UserInfo) ||
                uri.Host is not ("github.com" or "release-assets.githubusercontent.com" or "objects.githubusercontent.com"))
                throw new InvalidDataException("Untrusted update host.");
            var response = await Client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead);
            if ((int)response.StatusCode is >= 300 and < 400)
            {
                var next = response.Headers.Location;
                response.Dispose();
                if (next is null) throw new InvalidDataException("Missing update redirect.");
                url = new Uri(uri, next).AbsoluteUri;
                continue;
            }
            return response;
        }
        throw new InvalidDataException("Too many update redirects.");
    }

    public static async Task<AvailableUpdate?> CheckAsync()
    {
        using var response = await Client.GetAsync(Latest, HttpCompletionOption.ResponseHeadersRead);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await ReadLimited(response, 1024 * 1024));
        var root = doc.RootElement;
        var rawTag = root.GetProperty("tag_name").GetString() ?? "";
        if (!rawTag.StartsWith("v", StringComparison.Ordinal) || !Version.TryParse(rawTag[1..], out var latest) ||
            latest.Build < 0 || latest.Revision >= 0 || rawTag != "v" + latest.ToString()) return null;
        if (root.GetProperty("draft").GetBoolean() || root.GetProperty("prerelease").GetBoolean()) return null;
        var current = Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(1, 0, 0);
        if (latest <= current) return null;
        string? msi = null, hash = null;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (name == "Transflux-win-x64.msi" && url is not null && AllowedAsset(url, rawTag, name)) msi = url;
            if (name == "Transflux-win-x64.msi.sha256" && url is not null && AllowedAsset(url, rawTag, name)) hash = url;
        }
        return msi is null || hash is null ? null :
            new AvailableUpdate(latest, root.GetProperty("body").GetString() ?? "", msi, hash);
    }

    public static async Task DownloadAndLaunchAsync(AvailableUpdate update)
    {
        var updater = Path.Combine(AppContext.BaseDirectory, "Transflux.Updater.exe");
        if (!File.Exists(updater)) throw new FileNotFoundException("The update helper is missing.", updater);
        var directory = Directory.CreateTempSubdirectory("transflux-update-").FullName;
        bool launched = false;
        try
        {
        var msi = Path.Combine(directory, "Transflux-win-x64.msi");
        using var checksumResponse = await GetAsset(update.ShaUrl);
        var expectedText = System.Text.Encoding.UTF8.GetString(await ReadLimited(checksumResponse, 4096));
        var sha = expectedText.Split([' ', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)[0];
        if (sha.Length != 64 || !sha.All(Uri.IsHexDigit))
            throw new InvalidDataException("Release checksum is invalid.");
        using (var response = await GetAsset(update.MsiUrl))
        {
            response.EnsureSuccessStatusCode();
            await using var input = await response.Content.ReadAsStreamAsync();
            const long maxInstaller = 512L * 1024 * 1024;
            if (response.Content.Headers.ContentLength > maxInstaller) throw new InvalidDataException("Installer exceeds limit.");
            await using var output = new FileStream(msi, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            var buffer = new byte[65536];
            long total = 0;
            using var deadline = new System.Threading.CancellationTokenSource(TimeSpan.FromMinutes(4));
            int n;
            while ((n = await input.ReadAsync(buffer.AsMemory(), deadline.Token)) > 0)
            {
                total += n;
                if (total > maxInstaller) throw new InvalidDataException("Installer exceeds limit.");
                await output.WriteAsync(buffer.AsMemory(0, n), deadline.Token);
            }
        }
        await using (var file = File.OpenRead(msi))
        {
            if (!Convert.ToHexString(await SHA256.HashDataAsync(file)).Equals(sha, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Installer checksum verification failed.");
        }
        var stagedUpdater = Path.Combine(directory, "Transflux.Updater.exe");
        File.Copy(updater, stagedUpdater, false);
        var start = new ProcessStartInfo(stagedUpdater) { UseShellExecute = false, WorkingDirectory = directory };
        foreach (var arg in new[] { "--install", msi, sha, Environment.ProcessId.ToString() })
            start.ArgumentList.Add(arg);
        using var process = Process.Start(start) ?? throw new IOException("Unable to launch update helper.");
        launched = true;
        System.Windows.Application.Current.Shutdown();
        }
        finally
        {
            if (!launched) { try { Directory.Delete(directory, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        }
    }
}
