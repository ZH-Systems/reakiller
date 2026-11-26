using System.Diagnostics;
using System.Security.Cryptography;
using Transflux.Core;

internal sealed record OcrConfiguration(string Ghostscript, string GhostscriptSha256,
    string Tesseract, string TesseractSha256, string Tessdata, string EnglishDataSha256,
    double MinimumConfidence = 80);

internal sealed class OcrTools(OcrConfiguration config)
{
    private static string Verify(string path, string expected)
    {
        if (!Path.IsPathFullyQualified(path) || expected.Length != 64 || !expected.All(Uri.IsHexDigit))
            throw new InvalidDataException("OCR requires absolute tool paths and approved SHA-256 hashes.");
        path = Path.GetFullPath(path);
        using var file = File.OpenRead(path);
        if (!Convert.ToHexString(SHA256.HashData(file)).Equals(expected, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("OCR tool or language data hash mismatch.");
        return path;
    }

    private static async Task<string> Run(string exe, string directory, IEnumerable<string> args,
        string? monitoredFile = null, bool version = false)
    {
        var start = new ProcessStartInfo(exe) { WorkingDirectory = directory, UseShellExecute = false,
            CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        foreach (var key in new[] { "GS_OPTIONS", "GS_LIB", "GS_FONTPATH", "TESSDATA_PREFIX" })
            start.Environment.Remove(key);
        start.Environment["OMP_THREAD_LIMIT"] = "1";
        start.Environment["TEMP"] = directory;
        start.Environment["TMP"] = directory;
        using var containment = OperatingSystem.IsWindows() ? WindowsProcessJob.Create(768L * 1024 * 1024, 2) : null;
        using var p = Process.Start(start) ?? throw new IOException("OCR process failed to start.");
        try { containment?.Assign(p); }
        catch { if (!p.HasExited) p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); throw; }
        var stdout = version ? ReadVersion(p.StandardOutput) : Drain(p.StandardOutput.BaseStream);
        var stderr = p.StandardError.BaseStream.CopyToAsync(Stream.Null);
        var timer = Stopwatch.StartNew();
        try
        {
            while (!p.HasExited)
            {
                p.Refresh();
                if (timer.Elapsed > TimeSpan.FromSeconds(90) || p.PrivateMemorySize64 > 768L * 1024 * 1024 ||
                    (monitoredFile is not null && File.Exists(monitoredFile) && new FileInfo(monitoredFile).Length > 64L * 1024 * 1024))
                    throw new InvalidDataException("OCR process exceeded resource limits.");
                await Task.Delay(100);
            }
            if (p.ExitCode != 0) throw new InvalidDataException("OCR tool failed.");
            return await stdout ?? "";
        }
        finally
        {
            if (!p.HasExited) p.Kill(entireProcessTree: true);
            containment?.Dispose();
            await p.WaitForExitAsync();
            await Task.WhenAll(stdout, stderr);
        }
    }
    private static async Task<string?> ReadVersion(StreamReader reader)
    {
        var buffer = new char[1024];
        var result = new System.Text.StringBuilder();
        int n;
        while ((n = await reader.ReadAsync(buffer.AsMemory())) > 0)
            if (result.Length < 4096) result.Append(buffer, 0, Math.Min(n, 4096 - result.Length));
        return result.ToString().Split('\n')[0].Trim();
    }
    private static async Task<string?> Drain(Stream stream)
    { await stream.CopyToAsync(Stream.Null); return null; }

    public IReadOnlyList<OcrWord> ReadPage(string source, int page, double pageWidth, double pageHeight)
    {
        if (pageWidth <= 0 || pageHeight <= 0 || pageWidth * pageHeight > 2_500_000 ||
            pageWidth > 2000 || pageHeight > 2000)
            throw new InvalidDataException("OCR page dimensions exceed limits.");
        var gs = Verify(config.Ghostscript, config.GhostscriptSha256);
        var tess = Verify(config.Tesseract, config.TesseractSha256);
        var data = Verify(Path.Combine(config.Tessdata, "eng.traineddata"), config.EnglishDataSha256);
        var directory = Directory.CreateTempSubdirectory("transflux-ocr-").FullName;
        try
        {
            var gsVersion = Run(gs, directory, new[] { "--version" }, version: true).GetAwaiter().GetResult();
            var tessVersion = Run(tess, directory, new[] { "--version" }, version: true).GetAwaiter().GetResult();
            if (gsVersion.Trim() != "10.08.0" || !tessVersion.StartsWith("tesseract 5.5.4", StringComparison.Ordinal))
                throw new InvalidDataException("OCR tools must match reviewed versions Ghostscript 10.08.0 and Tesseract 5.5.4.");
            var image = Path.Combine(directory, "page.png");
            var output = Path.Combine(directory, "words");
            Run(gs, directory, new[] { "-dSAFER", "-dBATCH", "-dNOPAUSE", "-dPDFSTOPONERROR",
                "-sDEVICE=pnggray", "-r300", "-dFirstPage=" + page, "-dLastPage=" + page,
                "-sOutputFile=" + image, "-f", Path.GetFullPath(source) }, image).GetAwaiter().GetResult();
            if (!File.Exists(image) || new FileInfo(image).Length > 64L * 1024 * 1024)
                throw new InvalidDataException("Invalid raster output.");
            Run(tess, directory, new[] { image, output, "--tessdata-dir", Path.GetDirectoryName(data)!,
                "-l", "eng", "--psm", "6", "tsv" }, output + ".tsv").GetAwaiter().GetResult();
            var tsv = output + ".tsv";
            if (!File.Exists(tsv) || new FileInfo(tsv).Length > 64L * 1024 * 1024)
                throw new InvalidDataException("Invalid OCR output.");
            using var reader = File.OpenText(tsv);
            return OcrTsv.Parse(reader, config.MinimumConfidence);
        }
        finally { Directory.Delete(directory, true); }
    }
}
