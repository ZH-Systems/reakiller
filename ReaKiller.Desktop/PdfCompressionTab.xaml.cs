using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using MessageBox = System.Windows.MessageBox;

namespace ReaKiller.Desktop;

public partial class PdfCompressionTab : System.Windows.Controls.UserControl
{
    private sealed record PdfResult(string Name, string Before, string After, string Status);
    private readonly ObservableCollection<PdfResult> _results = new();
    private readonly List<string> _files = new();
    private string? _sourceFolder;
    private CancellationTokenSource? _stop;
    private readonly ConcurrentDictionary<int, Process> _activeProcesses = new();

    public PdfCompressionTab()
    {
        InitializeComponent();
        Results.ItemsSource = _results;
        Loaded += (_, _) => CheckEngine();
    }

    private static string? FindGhostscript()
    {
        var paths = new List<string>();
        foreach (var root in new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) })
        {
            var gsRoot = Path.Combine(root, "gs");
            if (Directory.Exists(gsRoot))
                paths.AddRange(Directory.EnumerateFiles(gsRoot, "gswin64c.exe", SearchOption.AllDirectories));
        }
        var env = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in env.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var file = Path.Combine(dir.Trim('"'), "gswin64c.exe");
                if (File.Exists(file)) paths.Add(file);
            }
            catch (ArgumentException) { }
        }
        return paths.OrderByDescending(p => p, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
    }

    private void CheckEngine()
    {
        try
        {
            var executable = FindGhostscript();
            EngineStatus.Text = executable is null
                ? "Ghostscript not detected. Install Ghostscript for Windows before compressing PDFs."
                : "Ready: " + executable;
        }
        catch (Exception ex) { EngineStatus.Text = "Ghostscript check failed: " + ex.Message; }
    }

    private void CheckEngine_Click(object sender, RoutedEventArgs e) => CheckEngine();

    private void GhostscriptLink_Click(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void ChooseFiles_Click(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choose PDFs to compress",
            Filter = "PDF documents (*.pdf)|*.pdf",
            Multiselect = true
        };
        if (picker.ShowDialog() != true) return;
        _sourceFolder = null;
        _files.Clear();
        _files.AddRange(picker.FileNames.Distinct(StringComparer.OrdinalIgnoreCase));
        SelectionSummary.Text = _files.Count == 1 ? Path.GetFileName(_files[0]) : $"{_files.Count} PDF files selected";
        SelectedCount.Text = $"{_files.Count:N0} selected";
        if (_files.Count > 0 && string.IsNullOrWhiteSpace(DestinationPath.Text))
            DestinationPath.Text = Path.Combine(Path.GetDirectoryName(_files[0])!, "Compressed");
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var picker = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a folder containing PDF documents",
            UseDescriptionForTitle = true
        };
        if (picker.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _sourceFolder = Path.GetFullPath(picker.SelectedPath);
        _files.Clear();
        SelectionSummary.Text = _sourceFolder;
        DestinationPath.Text = Path.Combine(_sourceFolder, "Compressed");
        UpdateFolderCount();
    }

    private void RecursiveScan_Changed(object sender, RoutedEventArgs e)
    {
        if (SelectedCount is not null) UpdateFolderCount();
    }

    private void UpdateFolderCount()
    {
        if (_sourceFolder is null) return;
        try
        {
            var files = DiscoverFolderPdfs(_sourceFolder, DestinationPath.Text.Trim().Trim('"'), RecursiveScan.IsChecked == true);
            SelectedCount.Text = $"{files.Length:N0} PDFs found";
        }
        catch (Exception ex) { SelectedCount.Text = "Unable to scan folder: " + ex.Message; }
    }

    private static bool IsWithinDirectory(string path, string directory)
    {
        var fullPath = Path.GetFullPath(path);
        var fullDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        return fullPath.StartsWith(fullDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string[] DiscoverFolderPdfs(string sourceFolder, string destination, bool recursive)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint,
            MatchCasing = MatchCasing.CaseInsensitive
        };
        return Directory.EnumerateFiles(sourceFolder, "*.pdf", options)
            .Where(path => string.IsNullOrWhiteSpace(destination) || !IsWithinDirectory(path, destination))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private void ChooseDestination_Click(object sender, RoutedEventArgs e)
    {
        using var picker = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Choose a destination folder for compressed PDFs",
            UseDescriptionForTitle = true
        };
        if (picker.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            DestinationPath.Text = picker.SelectedPath;
    }

    private static string Size(long bytes) => bytes < 1024 * 1024
        ? $"{bytes / 1024d:F1} KB" : $"{bytes / 1048576d:F2} MB";

    private sealed record PdfJobResult(string Name, string Before, string After, string Status);

    private async Task<PdfJobResult> CompressOnePdfAsync(
        string source, string destination, string? sourceFolder,
        string gs, string preset, bool skipLarger, CancellationToken cancellationToken)
    {
        var displayName = sourceFolder is null
            ? Path.GetFileName(source)
            : Path.GetRelativePath(sourceFolder, source);
        string beforeText = "—";
        string afterText = "—";
        string? temp = null;
        Process? process = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Snapshot the original metadata before Ghostscript reads the document.
            var sourceCreatedUtc = File.GetCreationTimeUtc(source);
            var sourceModifiedUtc = File.GetLastWriteTimeUtc(source);
            var sourceAccessedUtc = File.GetLastAccessTimeUtc(source);
            var before = new FileInfo(source).Length;
            beforeText = Size(before);

            var relativeDirectory = sourceFolder is null
                ? string.Empty
                : Path.GetDirectoryName(Path.GetRelativePath(sourceFolder, source)) ?? string.Empty;
            var outputDirectory = Path.Combine(destination, relativeDirectory);
            var output = Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(source) + "_compressed.pdf");
            if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
                throw new IOException("Input and output path cannot match.");
            if (File.Exists(output))
                return new PdfJobResult(displayName, beforeText, afterText, "Skipped (exists)");

            Directory.CreateDirectory(outputDirectory);
            temp = Path.Combine(outputDirectory, ".reakiller-" + Guid.NewGuid().ToString("N") + ".pdf");
            var start = new ProcessStartInfo(gs)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            foreach (var arg in new[]
            {
                "-dSAFER", "-dBATCH", "-dNOPAUSE", "-dQUIET", "-sDEVICE=pdfwrite",
                "-dCompatibilityLevel=1.6", "-dDetectDuplicateImages=true",
                "-dCompressFonts=true", "-dSubsetFonts=true",
                "-dPDFSETTINGS=/" + preset, "-sOutputFile=" + temp, source
            }) start.ArgumentList.Add(arg);

            process = new Process { StartInfo = start };
            cancellationToken.ThrowIfCancellationRequested();
            if (!process.Start()) throw new IOException("Unable to start Ghostscript.");
            _activeProcesses[process.Id] = process;
            // Drain redirected streams in parallel to avoid blocking on full pipe buffers.
            var stdout = process.StandardOutput.ReadToEndAsync();
            var stderr = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
                catch (InvalidOperationException) { }
                try { await process.WaitForExitAsync().ConfigureAwait(false); }
                catch (InvalidOperationException) { }
                throw;
            }
            var errors = await stderr.ConfigureAwait(false);
            await stdout.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (process.ExitCode != 0)
                throw new IOException($"Ghostscript exit code {process.ExitCode}. {errors.Trim()}");

            await using (var stream = new FileStream(temp, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (stream.Length < 8) throw new IOException("Generated PDF is empty.");
                var header = new byte[5];
                await stream.ReadExactlyAsync(header, cancellationToken).ConfigureAwait(false);
                if (System.Text.Encoding.ASCII.GetString(header) != "%PDF-")
                    throw new IOException("Output does not contain a PDF signature.");
            }
            var written = new FileInfo(temp).Length;
            if (skipLarger && written >= before)
                return new PdfJobResult(displayName, beforeText, afterText, "Skipped (not smaller)");

            cancellationToken.ThrowIfCancellationRequested();
            // Set filesystem timestamps before publishing the output, then restore
            // again at its final location for filesystem/provider consistency.
            File.SetCreationTimeUtc(temp, sourceCreatedUtc);
            File.SetLastWriteTimeUtc(temp, sourceModifiedUtc);
            File.SetLastAccessTimeUtc(temp, sourceAccessedUtc);
            File.Move(temp, output);
            temp = null;
            File.SetCreationTimeUtc(output, sourceCreatedUtc);
            File.SetLastWriteTimeUtc(output, sourceModifiedUtc);
            File.SetLastAccessTimeUtc(output, sourceAccessedUtc);
            afterText = Size(written);
            return new PdfJobResult(displayName, beforeText, afterText, "Compressed");
        }
        catch (OperationCanceledException)
        {
            return new PdfJobResult(displayName, beforeText, afterText, "Stopped");
        }
        catch (Exception ex)
        {
            return new PdfJobResult(displayName, beforeText, afterText, "Failed: " + ex.Message);
        }
        finally
        {
            if (process is not null)
            {
                try { _activeProcesses.TryRemove(process.Id, out _); }
                catch (InvalidOperationException) { }
                process.Dispose();
            }
            if (temp is not null)
            {
                try { if (File.Exists(temp)) File.Delete(temp); }
                catch (IOException) { }
            }
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_stop is not null) return;
        var gs = FindGhostscript();
        if (gs is null)
        {
            MessageBox.Show("Ghostscript was not found. Install Ghostscript for Windows and try again.", "PDF compression");
            return;
        }
        if (_sourceFolder is null && (_files.Count == 0 || _files.Any(p => !File.Exists(p))))
        {
            MessageBox.Show("Select existing PDF documents or a source folder first.", "PDF compression");
            return;
        }
        if (_sourceFolder is not null && !Directory.Exists(_sourceFolder))
        {
            MessageBox.Show("The selected source folder no longer exists.", "PDF compression");
            return;
        }
        var destination = DestinationPath.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show("Choose an output folder.", "PDF compression");
            return;
        }
        string[] files;
        try
        {
            destination = Path.GetFullPath(destination);
            if (_sourceFolder is not null &&
                string.Equals(Path.TrimEndingDirectorySeparator(destination),
                    Path.TrimEndingDirectorySeparator(_sourceFolder), StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Choose a different output folder to avoid processing generated PDFs.", "PDF compression");
                return;
            }
            files = _sourceFolder is null
                ? _files.ToArray()
                : DiscoverFolderPdfs(_sourceFolder, destination, RecursiveScan.IsChecked == true);
            if (files.Length == 0)
            {
                MessageBox.Show("No PDF documents were found in the selected source.", "PDF compression");
                return;
            }
            Directory.CreateDirectory(destination);
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "PDF folder error"); return; }

        // Separate Ghostscript processes provide document-level concurrency.
        // Keep the default at two workers to prevent excessive disk contention.
        var requestedWorkers = (Workers.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "2";
        var workers = Math.Clamp(int.TryParse(requestedWorkers, out var value) ? value : 2, 1, 4);
        var preset = ((ComboBoxItem)Preset.SelectedItem).Tag?.ToString() ?? "ebook";
        var skipLarger = SkipLarger.IsChecked == true;
        var sourceFolder = _sourceFolder;
        var tokenSource = new CancellationTokenSource();
        _stop = tokenSource;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        _results.Clear();
        Progress.Value = 0;
        SelectedCount.Text = $"{files.Length:N0} PDFs queued";

        int processed = 0, succeeded = 0, skipped = 0, failed = 0;
        try
        {
            try
            {
                await Parallel.ForEachAsync(files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = workers,
                        CancellationToken = tokenSource.Token
                    },
                    async (source, ct) =>
                    {
                        var result = await CompressOnePdfAsync(
                            source, destination, sourceFolder, gs, preset, skipLarger, ct).ConfigureAwait(false);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            _results.Add(new PdfResult(result.Name, result.Before, result.After, result.Status));
                            processed++;
                            if (result.Status == "Compressed") succeeded++;
                            else if (result.Status.StartsWith("Skipped", StringComparison.Ordinal)) skipped++;
                            else if (result.Status.StartsWith("Failed", StringComparison.Ordinal)) failed++;
                            Progress.Value = 100d * processed / files.Length;
                            ProgressText.Text = $"{processed} / {files.Length}";
                            SummaryText.Text = $"Compressed: {succeeded} · Skipped: {skipped} · Failed: {failed} · Workers: {workers}";
                        });
                    });
            }
            catch (OperationCanceledException)
            {
                // Completed files remain committed, incomplete .pdf temporaries are removed.
            }
            ProgressText.Text = $"{processed} / {files.Length}";
            SummaryText.Text = $"Compressed: {succeeded} · Skipped: {skipped} · Failed: {failed}" +
                (tokenSource.IsCancellationRequested ? " · Stopped" : "");
        }
        finally
        {
            _stop = null;
            tokenSource.Dispose();
            StartButton.IsEnabled = true;
            StopButton.IsEnabled = false;
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        _stop?.Cancel();
        foreach (var process in _activeProcesses.Values)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
            catch (InvalidOperationException) { }
        }
        StopButton.IsEnabled = false;
    }
}
