using System.Diagnostics;
using System.Reflection;
using System.Security.Principal;
using System.Text.Json;
using Transflux.Core;

internal sealed class IsolatedPdfConverter(PdfCsvOptions options, OcrConfiguration? ocr = null) : IFileConverter
{
    public ConversionOperation Operation => ConversionOperation.PdfToCsv;
    public IReadOnlyCollection<string> SourceExtensions => new[] { ".pdf" };
    public string OutputExtension => ".csv";
    internal const long MaxInputBytes = 256L * 1024 * 1024;
    internal const long MaxOutputBytes = 256L * 1024 * 1024;
    internal const long MaxWorkerBytes = 1024L * 1024 * 1024;

    public Task ConvertAsync(string source, string temporaryOutput, CancellationToken token) =>
        ExecuteAsync(source, temporaryOutput, false, token);

    public async Task<string[][]> PreviewAsync(string source, CancellationToken token)
    {
        var path = Path.Combine(Path.GetTempPath(), "transflux-preview-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            await ExecuteAsync(source, path, true, token);
            if (new FileInfo(path).Length > 4 * 1024 * 1024)
                throw new InvalidDataException("Preview exceeds size limit.");
            return JsonSerializer.Deserialize<string[][]>(await File.ReadAllTextAsync(path, token))
                ?? throw new InvalidDataException("Invalid preview.");
        }
        finally { File.Delete(path); }
    }

    private async Task ExecuteAsync(string source, string output, bool preview, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        source = Path.GetFullPath(source);
        output = Path.GetFullPath(output);
        if (new FileInfo(source).Length > MaxInputBytes)
            throw new InvalidDataException("PDF exceeds 256 MiB input limit.");
        var executable = Environment.ProcessPath ?? throw new InvalidOperationException("Worker executable unavailable.");
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardError = true, RedirectStandardOutput = true };
        if (Path.GetFileNameWithoutExtension(executable).Equals("dotnet", StringComparison.OrdinalIgnoreCase))
            start.ArgumentList.Add(typeof(IsolatedPdfConverter).Assembly.Location);
        foreach (var argument in new[] { "--internal-pdf-worker", source, output,
            preview ? "preview" : "convert", JsonSerializer.Serialize(options), JsonSerializer.Serialize(ocr) })
            start.ArgumentList.Add(argument);
        start.Environment["DOTNET_GCHeapHardLimit"] = "0x20000000";
        using var containment = OperatingSystem.IsWindows() ? WindowsProcessJob.Create(2L * 1024 * 1024 * 1024, 4) : null;
        using var process = Process.Start(start) ?? throw new IOException("Unable to start PDF worker.");
        try { containment?.Assign(process); }
        catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
        // Drain without retaining document-derived stdout or stderr.
        var stdout = process.StandardOutput.BaseStream.CopyToAsync(Stream.Null);
        var stderr = process.StandardError.BaseStream.CopyToAsync(Stream.Null);
        var elapsed = Stopwatch.StartNew();
        try
        {
            while (!process.HasExited)
            {
                token.ThrowIfCancellationRequested();
                process.Refresh();
                if (elapsed.Elapsed > TimeSpan.FromMinutes(5))
                    throw new TimeoutException("PDF worker exceeded time limit.");
                if (process.PrivateMemorySize64 > MaxWorkerBytes)
                    throw new InvalidDataException("PDF worker exceeded memory limit.");
                if (File.Exists(output) && new FileInfo(output).Length > MaxOutputBytes)
                    throw new InvalidDataException("PDF worker exceeded output limit.");
                await Task.Delay(100, token);
            }
            await process.WaitForExitAsync(token);
            if (process.ExitCode != 0)
                throw new InvalidDataException(process.ExitCode == 4
                    ? "PDF has no extractable text; OCR is required."
                    : "PDF worker failed or was terminated (exit " + process.ExitCode + ").");
            if (!File.Exists(output) || new FileInfo(output).Length > MaxOutputBytes)
                throw new InvalidDataException("Invalid PDF worker output.");
        }
        finally
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            containment?.Dispose();
            await process.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    }

    internal static async Task<int> WorkerAsync(string[] args)
    {
        try
        {
            if (args.Length != 6 || (args[3] != "preview" && args[3] != "convert")) return 2;
            if (OperatingSystem.IsWindows())
            {
                using var identity = WindowsIdentity.GetCurrent();
                if (new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator)) return 2;
            }
            var source = Path.GetFullPath(args[1]);
            var output = Path.GetFullPath(args[2]);
            if (string.Equals(source, output, StringComparison.OrdinalIgnoreCase) || File.Exists(output)
                || new FileInfo(source).Length > MaxInputBytes) return 2;
            var settings = JsonSerializer.Deserialize<PdfCsvOptions>(args[4]) ?? throw new InvalidDataException();
            var config = JsonSerializer.Deserialize<OcrConfiguration>(args[5]);
            var tools = config is null ? null : new OcrTools(config);
            var converter = new PdfCsvConverter(settings, tools is null ? null : tools.ReadPage);
            if (args[3] == "preview")
            {
                var json = JsonSerializer.Serialize(converter.Preview(source));
                if (System.Text.Encoding.UTF8.GetByteCount(json) > 4 * 1024 * 1024) return 2;
                await File.WriteAllTextAsync(output, json);
            }
            else await converter.ConvertAsync(source, output, CancellationToken.None);
            return 0;
        }
        catch (InvalidDataException ex) when (ex.Message.Contains("OCR is required", StringComparison.Ordinal)) { return 4; }
        catch { return 1; }
    }
}
