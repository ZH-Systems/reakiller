using System.Diagnostics;
using System.Collections.ObjectModel;
using System.Windows.Media;
using System.Text.Json;
using Microsoft.Win32;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using MessageBox = System.Windows.MessageBox;

namespace ReaKiller.Desktop;

public partial class MainWindow : Window
{
    private sealed record FileResult(string FileName, string Status, string Detail, Brush StatusColor);
    private readonly ObservableCollection<FileResult> _fileResults = new();
    private Process? _process;
    private bool _running;
    private bool _cancelRequested;
    private string? _lastReport;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".img", ".dcm", ".dicom", ".jpg", ".jpeg", ".tif", ".tiff" };

    public MainWindow()
    {
        InitializeComponent();
        ResultsList.ItemsSource = _fileResults;
        Closed += (_, _) =>
        {
            try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
            catch { /* Process may already have exited. */ }
        };
    }

    private static string? BrowseFolder()
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "Select an image folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        return dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            ? dialog.SelectedPath : null;
    }

    private void BrowseSource_Click(object sender, RoutedEventArgs e)
    {
        var chosen = BrowseFolder();
        if (chosen is not null) SourcePath.Text = chosen;
    }

    private void BrowseOutput_Click(object sender, RoutedEventArgs e)
    {
        var chosen = BrowseFolder();
        if (chosen is not null) OutputPath.Text = chosen;
    }

    private void ContrastSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ContrastValue is not null) ContrastValue.Text = ((int)e.NewValue).ToString();
    }

    private void SharpenSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SharpenValue is not null)
            SharpenValue.Text = e.NewValue.ToString("F1", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void SourcePath_Changed(object sender, TextChangedEventArgs e)
    {
        if (FileCountText is not null) FileCountText.Text = "Scan to count files";
    }

    private void ScanSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (FileCountText is not null) FileCountText.Text = "Scan to count files";
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        var source = SourcePath.Text.Trim().Trim('"');
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            MessageBox.Show("Select an existing image or folder first.", "Source required");
            return;
        }
        ScanButton.IsEnabled = false;
        FileCountText.Text = "Scanning...";
        var recursive = Recursive.IsChecked == true;
        try
        {
            var count = await Task.Run(() => File.Exists(source)
                ? (Extensions.Contains(Path.GetExtension(source)) ? 1 : 0)
                : Directory.EnumerateFiles(source, "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .Count(f => Extensions.Contains(Path.GetExtension(f))));
            FileCountText.Text = $"{count:N0} supported image(s)";
        }
        catch (Exception ex)
        {
            FileCountText.Text = "Scan failed";
            MessageBox.Show(ex.Message, "Unable to scan folder");
        }
        finally { ScanButton.IsEnabled = !_running; }
    }

    private void OpenReport_Click(object sender, RoutedEventArgs e)
    {
        if (_lastReport is null || !File.Exists(_lastReport)) return;
        try
        {
            Process.Start(new ProcessStartInfo(_lastReport) { UseShellExecute = true });
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Unable to open report"); }
    }

    private void InPlace_Changed(object sender, RoutedEventArgs e)
    {
        if (OutputPath is null || BrowseOutput is null || InPlace is null) return;
        var enable = InPlace.IsChecked != true;
        OutputPath.IsEnabled = enable;
        BrowseOutput.IsEnabled = enable;
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Dispatcher.Invoke(() =>
        {
            if (text.StartsWith("REAKILLER_EVENT:", StringComparison.Ordinal))
            {
                try
                {
                    using var json = JsonDocument.Parse(text["REAKILLER_EVENT:".Length..]);
                    var data = json.RootElement;
                    var source = data.GetProperty("source").GetString() ?? "";
                    var status = data.GetProperty("status").GetString() ?? "UNKNOWN";
                    var detail = data.GetProperty("detail").GetString() ?? "";
                    var done = data.GetProperty("done").GetInt32();
                    var total = data.GetProperty("total").GetInt32();
                    var color = status switch
                    {
                        "SUCCEEDED" => Brushes.ForestGreen,
                        "FAILED" => Brushes.Firebrick,
                        _ => Brushes.SlateGray
                    };
                    _fileResults.Add(new FileResult(Path.GetFileName(source), status == "SUCCEEDED" ? "Succeeded" : status == "FAILED" ? "Failed" : "Skipped", detail, color));
                    if (_fileResults.Count > 2000) _fileResults.RemoveAt(0);
                    if (total > 0) Progress.Value = 100.0 * done / total;
                    ProgressText.Text = $"{done:N0} / {total:N0} files";
                    ResultsList.ScrollIntoView(_fileResults[^1]);
                }
                catch (Exception ex) { Log.AppendText("Progress parse warning: " + ex.Message + Environment.NewLine); }
                return;
            }
            // CLI progress is written to redirected stdout one line per completed file.
            Log.AppendText(text.TrimEnd('\r') + Environment.NewLine);
            if (text.StartsWith("Complete:", StringComparison.OrdinalIgnoreCase))
                SummaryText.Text = text;
            if (Log.Text.Length > 16000) Log.Text = Log.Text[^12000..];
            Log.ScrollToEnd();
            var match = Regex.Match(text, @"\((\d+)/(\d+)\)");
            if (match.Success
                && int.TryParse(match.Groups[1].Value, out int done)
                && int.TryParse(match.Groups[2].Value, out int total)
                && total > 0)
            {
                Progress.Value = 100.0 * done / total;
                ProgressText.Text = $"{done:N0} / {total:N0} files";
            }
        });
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        var source = SourcePath.Text.Trim().Trim('"');
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            MessageBox.Show("Choose an existing source file or directory.", "Invalid source");
            return;
        }

        var sourceWasFile = File.Exists(source);
        var sourceDirectory = sourceWasFile ? Path.GetDirectoryName(Path.GetFullPath(source))! : Path.GetFullPath(source);
        var inPlace = InPlace.IsChecked == true;
        var destination = OutputPath.Text.Trim().Trim('"');
        if (!inPlace && string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show("Choose an output folder.", "Output required");
            return;
        }

        if (!int.TryParse(Threads.Text, out int threads) || threads < 1 || threads > 64)
        {
            MessageBox.Show("Threads must be between 1 and 64.", "Invalid worker count");
            return;
        }

        if (!string.IsNullOrWhiteSpace(MaxWidthInput.Text)
            && (!int.TryParse(MaxWidthInput.Text, out int width) || width < 1))
        {
            MessageBox.Show("Max width must be a positive integer.", "Invalid maximum width");
            return;
        }

        if (inPlace && MessageBox.Show(
                "In-place mode removes each source file after conversion. Back up the folder first. Continue?",
                "Confirm in-place replacement", MessageBoxButton.YesNo, MessageBoxImage.Warning)
            != MessageBoxResult.Yes) return;

        var cli = Path.Combine(AppContext.BaseDirectory, "ReaKiller.exe");
        if (!File.Exists(cli))
        {
            MessageBox.Show("ReaKiller.exe was not found alongside the desktop application. Use the published desktop bundle.",
                "Conversion engine missing");
            return;
        }

        _lastReport = null;
        OpenReportButton.IsEnabled = false;
        var start = new ProcessStartInfo(cli)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add("convert");
        start.ArgumentList.Add(source);
        if (inPlace) start.ArgumentList.Add("--in-place");
        else
        {
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(destination);
        }
        if (Recursive.IsChecked == true) start.ArgumentList.Add("--recursive");
        if (Overwrite.IsChecked == true) start.ArgumentList.Add("--overwrite");
        start.ArgumentList.Add("--threads");
        start.ArgumentList.Add(threads.ToString());
        start.ArgumentList.Add("--compression");
        start.ArgumentList.Add(((ComboBoxItem)Compression.SelectedItem).Tag.ToString()!);
        start.ArgumentList.Add("--compression-effort");
        start.ArgumentList.Add(((int)EffortSlider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (AutoContrast.IsChecked == true) start.ArgumentList.Add("--auto-contrast");
        if (ContrastSlider.Value != 0)
        {
            start.ArgumentList.Add("--contrast");
            start.ArgumentList.Add(((int)ContrastSlider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (SharpenSlider.Value > 0)
        {
            start.ArgumentList.Add("--sharpen");
            start.ArgumentList.Add(SharpenSlider.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(MaxWidthInput.Text))
        {
            start.ArgumentList.Add("--max-width");
            start.ArgumentList.Add(MaxWidthInput.Text.Trim());
        }

        try
        {
            _running = true;
            _cancelRequested = false;
            StartButton.IsEnabled = false;
            ScanButton.IsEnabled = false;
            SourcePath.IsEnabled = false;
            OutputPath.IsEnabled = false;
            BrowseOutput.IsEnabled = false;
            InPlace.IsEnabled = false;
            Recursive.IsEnabled = false;
            Overwrite.IsEnabled = false;
            Compression.IsEnabled = false;
            Threads.IsEnabled = false;
            MaxWidthInput.IsEnabled = false;
            AutoContrast.IsEnabled = false;
            ContrastSlider.IsEnabled = false;
            SharpenSlider.IsEnabled = false;
            EffortSlider.IsEnabled = false;
            CancelButton.IsEnabled = true;
            Progress.Value = 0;
            ProgressText.Text = "Starting...";
            SummaryText.Text = "Conversion in progress";
            Log.Clear();
            _fileResults.Clear();
            _process = new Process { StartInfo = start, EnableRaisingEvents = true };
            _process.Start();
            var standard = ReadLinesAsync(_process.StandardOutput);
            var errors = ReadLinesAsync(_process.StandardError);
            await _process.WaitForExitAsync();
            await Task.WhenAll(standard, errors);
            ProgressText.Text = _cancelRequested ? "Cancelled" : _process.ExitCode == 0 ? "Complete" : $"Exited with code {_process.ExitCode}";
            if (_cancelRequested) SummaryText.Text = "Cancelled. Review source and output files before rerunning.";
            _lastReport = Path.Combine(inPlace ? sourceDirectory : Path.GetFullPath(destination), "reakiller-report.csv");
            OpenReportButton.IsEnabled = File.Exists(_lastReport);
            if (_process.ExitCode == 0 && !_cancelRequested) Progress.Value = 100;
        }
        catch (Exception ex)
        {
            AppendLog("ERROR: " + ex.Message);
            ProgressText.Text = "Failed";
        }
        finally
        {
            _process?.Dispose();
            _process = null;
            _running = false;
            StartButton.IsEnabled = true;
            ScanButton.IsEnabled = true;
            SourcePath.IsEnabled = true;
            OutputPath.IsEnabled = InPlace.IsChecked != true;
            BrowseOutput.IsEnabled = InPlace.IsChecked != true;
            InPlace.IsEnabled = true;
            Recursive.IsEnabled = true;
            Overwrite.IsEnabled = true;
            Compression.IsEnabled = true;
            Threads.IsEnabled = true;
            MaxWidthInput.IsEnabled = true;
            AutoContrast.IsEnabled = true;
            ContrastSlider.IsEnabled = true;
            SharpenSlider.IsEnabled = true;
            EffortSlider.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private async Task ReadLinesAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line) AppendLog(line);
    }


    private sealed record SavedPreset(int CompressionEffort, int Contrast, double Sharpen,
        bool AutoContrast, int Threads, int? MaxWidth, bool Recursive, bool Overwrite);

    private void EffortSlider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (EffortValue is not null) EffortValue.Text = $"{(int)e.NewValue}%";
    }

    private void Window_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = !_running && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private void Window_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (_running || !e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths && paths.Length > 0)
            SourcePath.Text = paths[0];
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Threads.Text, out var threads) || threads < 1 || threads > 64)
        {
            MessageBox.Show("Workers must be between 1 and 64.", "Invalid settings");
            return;
        }
        int? width = null;
        if (!string.IsNullOrWhiteSpace(MaxWidthInput.Text))
        {
            if (!int.TryParse(MaxWidthInput.Text, out var parsed) || parsed < 1)
            {
                MessageBox.Show("Maximum width must be a positive integer.", "Invalid settings");
                return;
            }
            width = parsed;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "ReaKiller preset (*.json)|*.json",
            DefaultExt = ".json",
            FileName = "reakiller-preset.json"
        };
        if (dialog.ShowDialog() != true) return;
        var preset = new SavedPreset((int)EffortSlider.Value, (int)ContrastSlider.Value,
            SharpenSlider.Value, AutoContrast.IsChecked == true, threads, width,
            Recursive.IsChecked == true, Overwrite.IsChecked == true);
        try { File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true })); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Unable to save preset"); }
    }

    private void LoadPreset_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "ReaKiller preset (*.json)|*.json" };
        if (dialog.ShowDialog() != true) return;
        try
        {
            var preset = JsonSerializer.Deserialize<SavedPreset>(File.ReadAllText(dialog.FileName))
                ?? throw new InvalidDataException("Preset is empty.");
            if (preset.CompressionEffort is < 0 or > 100 || preset.Contrast is < -50 or > 50 ||
                !double.IsFinite(preset.Sharpen) || preset.Sharpen is < 0 or > 2 ||
                preset.Threads is < 1 or > 64 || preset.MaxWidth is <= 0)
                throw new InvalidDataException("Preset contains invalid values.");
            EffortSlider.Value = preset.CompressionEffort;
            ContrastSlider.Value = preset.Contrast;
            SharpenSlider.Value = preset.Sharpen;
            AutoContrast.IsChecked = preset.AutoContrast;
            Threads.Text = preset.Threads.ToString();
            MaxWidthInput.Text = preset.MaxWidth?.ToString() ?? "";
            Recursive.IsChecked = preset.Recursive;
            Overwrite.IsChecked = preset.Overwrite;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Unable to load preset"); }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        string source = SourcePath.Text.Trim().Trim('"');
        if (Directory.Exists(source))
        {
            try
            {
                source = Directory.EnumerateFiles(source, "*", Recursive.IsChecked == true
                        ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(path => Extensions.Contains(Path.GetExtension(path))) ?? "";
            }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Preview error"); return; }
        }
        if (!File.Exists(source))
        {
            MessageBox.Show("Choose a folder containing supported images or an individual image.", "Preview");
            return;
        }
        var cli = Path.Combine(AppContext.BaseDirectory, "ReaKiller.exe");
        if (!File.Exists(cli)) { MessageBox.Show("ReaKiller.exe is missing.", "Preview"); return; }
        var temporary = Path.Combine(Path.GetTempPath(), "ReaKillerPreview", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var start = new ProcessStartInfo(cli) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "convert", source, "--output", temporary,
                "--compression-effort", ((int)EffortSlider.Value).ToString(),
                "--contrast", ((int)ContrastSlider.Value).ToString(),
                "--sharpen", SharpenSlider.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture) })
                start.ArgumentList.Add(arg);
            if (AutoContrast.IsChecked == true) start.ArgumentList.Add("--auto-contrast");
            if (int.TryParse(MaxWidthInput.Text, out var width) && width > 0)
            { start.ArgumentList.Add("--max-width"); start.ArgumentList.Add(width.ToString()); }
            using var process = Process.Start(start) ?? throw new IOException("Cannot start converter.");
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            await Task.WhenAll(outputTask, errorTask);
            if (process.ExitCode != 0) throw new InvalidDataException(await errorTask);
            var converted = Directory.EnumerateFiles(temporary, "*.png").FirstOrDefault()
                ?? throw new InvalidDataException("No preview PNG was created.");
            var baselineFolder = Path.Combine(temporary, "baseline");
            Directory.CreateDirectory(baselineFolder);
            var baselineStart = new ProcessStartInfo(cli)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "convert", source, "--output", baselineFolder })
                baselineStart.ArgumentList.Add(arg);
            using var baselineProcess = Process.Start(baselineStart)
                ?? throw new IOException("Cannot launch baseline converter.");
            var baselineOutput = baselineProcess.StandardOutput.ReadToEndAsync();
            var baselineError = baselineProcess.StandardError.ReadToEndAsync();
            await baselineProcess.WaitForExitAsync();
            await Task.WhenAll(baselineOutput, baselineError);
            if (baselineProcess.ExitCode != 0)
                throw new InvalidDataException(await baselineError);
            var baselinePng = Directory.EnumerateFiles(baselineFolder, "*.png").FirstOrDefault()
                ?? throw new InvalidDataException("Cannot generate baseline PNG.");
            var window = new PreviewWindow(source, baselinePng, converted) { Owner = this };
            window.ShowDialog();
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Preview failed"); }
        finally
        {
            try { Directory.Delete(temporary, true); } catch { /* Temp cleanup will be retried by OS/user. */ }
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _cancelRequested = true;
            ProgressText.Text = "Cancelling...";
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
}
