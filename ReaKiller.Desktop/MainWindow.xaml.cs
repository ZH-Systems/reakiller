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
    private sealed record FileResult(string FileName, string Status, string Detail, System.Windows.Media.Brush StatusColor);
    private readonly ObservableCollection<FileResult> _fileResults = new();
    private Process? _process;
    private bool _running;
    private bool _cancelRequested;
    private string? _stopMarker;
    private string? _pauseMarker;
    private bool _paused;
    private readonly Stopwatch _jobClock = new();
    private TimeSpan _pausedTotal;
    private TimeSpan _pauseStarted;
    private int _completedFiles;
    private int _totalFiles;
    private string? _lastReport;
    private string? _selectionListFile;
    private readonly List<string> _selectedFiles = new();
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".img", ".dcm", ".dic", ".dicom", ".jpg", ".jpeg", ".png", ".bmp", ".pcx", ".cmp", ".tif", ".tiff" };

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
        if (SingleFileMode.IsChecked == true)
        {
            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select images to convert",
                Multiselect = true,
                Filter = "Supported images|*.img;*.dcm;*.dic;*.dicom;*.jpg;*.jpeg;*.tif;*.tiff|All files|*.*"
            };
            if (picker.ShowDialog() != true) return;
            _selectedFiles.Clear();
            _selectedFiles.AddRange(picker.FileNames.Where(path => Extensions.Contains(Path.GetExtension(path))));
            SourcePath.Text = _selectedFiles.Count == 1 ? _selectedFiles[0] : $"{_selectedFiles.Count} files selected";
            FileCountText.Text = $"{_selectedFiles.Count:N0} selected image(s)";
            return;
        }
        var chosen = BrowseFolder();
        if (chosen is not null) SourcePath.Text = chosen;
    }

    private void SingleFileMode_Changed(object sender, RoutedEventArgs e)
    {
        if (SourcePath is null || Recursive is null || SelectedFilesHint is null) return;
        _selectedFiles.Clear();
        SourcePath.Text = "";
        var individual = SingleFileMode.IsChecked == true;
        SourcePath.IsReadOnly = individual;
        Recursive.IsEnabled = !individual;
        SelectedFilesHint.Visibility = individual ? Visibility.Visible : Visibility.Collapsed;
        if (FileCountText is not null) FileCountText.Text = individual ? "No files selected" : "No source selected";
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

    private void NamePattern_Changed(object sender, TextChangedEventArgs e)
    {
        if (NamePreview is null || NamePattern is null) return;
        var pattern = NamePattern.Text;
        var example = pattern.Replace("{name}", "I11624", StringComparison.OrdinalIgnoreCase)
            .Replace("{ext}", "img", StringComparison.OrdinalIgnoreCase)
            .Replace("{index}", "00001", StringComparison.OrdinalIgnoreCase)
            .Replace("{date}", "20180315", StringComparison.OrdinalIgnoreCase);
        var invalid = string.IsNullOrWhiteSpace(example)
            || example.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0
            || example.Contains('{') || example.Contains('}')
            || example.EndsWith('.') || example.EndsWith(' ');
        var extension = (OutputFormat?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "jpg" ? ".jpg" : ".png";
        NamePreview.Text = invalid ? "Invalid naming pattern" : "Example: " + example + extension;
        NamePreview.Foreground = invalid ? System.Windows.Media.Brushes.Firebrick : System.Windows.Media.Brushes.RoyalBlue;
    }

    private void SourcePath_Changed(object sender, TextChangedEventArgs e)
    {
        if (FileCountText is not null && SingleFileMode?.IsChecked != true) FileCountText.Text = "Scan to count files";
    }

    private void ScanSetting_Changed(object sender, RoutedEventArgs e)
    {
        if (FileCountText is not null) FileCountText.Text = "Scan to count files";
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        var source = SourcePath.Text.Trim().Trim('"');
        if (SingleFileMode.IsChecked == true)
        {
            FileCountText.Text = $"{_selectedFiles.Count:N0} selected image(s)";
            return;
        }
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

    private void OutputFormat_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (NamePreview is not null) NamePattern_Changed(sender, null!);
    }

    private void OptimizeInPlace_Changed(object sender, RoutedEventArgs e)
    {
        if (OptimizeInPlace is null || InPlace is null || OutputPath is null || BrowseOutput is null) return;
        if (OptimizeInPlace.IsChecked == true) InPlace.IsChecked = false;
        OutputPath.IsEnabled = OptimizeInPlace.IsChecked != true && InPlace.IsChecked != true;
        BrowseOutput.IsEnabled = OutputPath.IsEnabled;
        if (OutputFormat is not null) OutputFormat.IsEnabled = OptimizeInPlace.IsChecked != true;
    }

    private void InPlace_Changed(object sender, RoutedEventArgs e)
    {
        if (OutputPath is null || BrowseOutput is null || InPlace is null) return;
        if (InPlace.IsChecked == true && OptimizeInPlace?.IsChecked == true) OptimizeInPlace.IsChecked = false;
        var enable = InPlace.IsChecked != true && OptimizeInPlace?.IsChecked != true;
        OutputPath.IsEnabled = enable;
        BrowseOutput.IsEnabled = enable;
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Dispatcher.Invoke(() =>
        {
            if (text.StartsWith("REAKILLER_BATCH:", StringComparison.Ordinal))
            {
                try
                {
                    using var json = JsonDocument.Parse(text["REAKILLER_BATCH:".Length..]);
                    var data = json.RootElement;
                    var batch = data.GetProperty("batch").GetInt32();
                    var batches = data.GetProperty("batches").GetInt32();
                    var completed = data.GetProperty("completed").GetInt32();
                    var total = data.GetProperty("total").GetInt32();
                    SummaryText.Text = $"Batch {batch:N0} of {batches:N0} ({completed:N0} of {total:N0} files completed)";
                    ProgressText.Text = $"{completed:N0} / {total:N0} files";
                    if (total > 0) Progress.Value = 100.0 * completed / total;
                    UpdateMetrics(completed, total);
                    Log.AppendText($"Starting batch {batch:N0} of {batches:N0}" + Environment.NewLine);
                    Log.ScrollToEnd();
                }
                catch (Exception ex) { Log.AppendText("Batch progress parse warning: " + ex.Message + Environment.NewLine); }
                return;
            }
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
                        "SUCCEEDED" => System.Windows.Media.Brushes.ForestGreen,
                        "STOPPED" => System.Windows.Media.Brushes.DarkOrange,
                        "FAILED" => System.Windows.Media.Brushes.Firebrick,
                        _ => System.Windows.Media.Brushes.SlateGray
                    };
                    _fileResults.Add(new FileResult(Path.GetFileName(source), status == "SUCCEEDED" ? "Succeeded" : status == "FAILED" ? "Failed" : status == "STOPPED" ? "Stopped" : "Skipped", detail, color));
                    if (_fileResults.Count > 2000) _fileResults.RemoveAt(0);
                    if (total > 0) Progress.Value = 100.0 * done / total;
                    UpdateMetrics(done, total);
                    ProgressText.Text = $"{done:N0} / {total:N0} files";
                    if (data.TryGetProperty("batch", out var batchNumber) && data.TryGetProperty("batches", out var batchCount))
                        SummaryText.Text = $"Batch {batchNumber.GetInt32():N0} of {batchCount.GetInt32():N0}";
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
                && int.TryParse(match.Groups[1].Value, out int consoleDone)
                && int.TryParse(match.Groups[2].Value, out int consoleTotal)
                && consoleTotal > 0)
            {
                Progress.Value = 100.0 * consoleDone / consoleTotal;
                ProgressText.Text = $"{consoleDone:N0} / {consoleTotal:N0} files";
            }
        });
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        var source = SingleFileMode.IsChecked == true ? _selectedFiles.FirstOrDefault() ?? "" : SourcePath.Text.Trim().Trim('"');
        if (SingleFileMode.IsChecked == true && (_selectedFiles.Count == 0 ||
            _selectedFiles.Any(path => !File.Exists(path) || !Extensions.Contains(Path.GetExtension(path)))))
        {
            MessageBox.Show("Select one or more existing supported images.", "Invalid selection");
            return;
        }
        if (!File.Exists(source) && !Directory.Exists(source))
        {
            MessageBox.Show("Choose an existing source file or directory.", "Invalid source");
            return;
        }

        var pattern = NamePattern.Text.Trim();
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Contains('{') && !System.Text.RegularExpressions.Regex.IsMatch(pattern, @"\{(?:name|ext|index|date)\}", RegexOptions.IgnoreCase))
        {
            MessageBox.Show("Enter a valid naming pattern.", "Naming");
            return;
        }
        var sourceWasFile = File.Exists(source);
        var sourceDirectory = sourceWasFile ? Path.GetDirectoryName(Path.GetFullPath(source))! : Path.GetFullPath(source);
        var optimize = OptimizeInPlace.IsChecked == true;
        var inPlace = InPlace.IsChecked == true;
        var destination = OutputPath.Text.Trim().Trim('"');
        if (!inPlace && !optimize && string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show("Choose an output folder.", "Output required");
            return;
        }

        if (!int.TryParse(Threads.Text, out int threads) || threads < 1 || threads > 64)
        {
            MessageBox.Show("Threads must be between 1 and 64.", "Invalid worker count");
            return;
        }

        if (ResizeImages.IsChecked == true && !string.IsNullOrWhiteSpace(MaxWidthInput.Text)
            && (!int.TryParse(MaxWidthInput.Text, out int width) || width < 1))
        {
            MessageBox.Show("Max width must be a positive integer.", "Invalid maximum width");
            return;
        }

        if (!int.TryParse(BatchSizeInput.Text, out var batchSize) || batchSize < 1 || batchSize > 100000)
        {
            MessageBox.Show("Batch size must be between 1 and 100,000.", "Invalid batch size");
            return;
        }

        if ((inPlace || optimize) && MessageBox.Show(
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
        _stopMarker = Path.Combine(Path.GetTempPath(), "reakiller-stop-" + Guid.NewGuid().ToString("N") + ".flag");
        _pauseMarker = Path.Combine(Path.GetTempPath(), "reakiller-pause-" + Guid.NewGuid().ToString("N") + ".flag");
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
        if (SingleFileMode.IsChecked == true)
        {
            _selectionListFile = Path.Combine(Path.GetTempPath(), "reakiller-files-" + Guid.NewGuid().ToString("N") + ".txt");
            File.WriteAllLines(_selectionListFile, _selectedFiles);
            start.ArgumentList.Add("--files-from");
            start.ArgumentList.Add(_selectionListFile);
        }
        start.ArgumentList.Add("--stop-file");
        start.ArgumentList.Add(_stopMarker);
        start.ArgumentList.Add("--pause-file");
        start.ArgumentList.Add(_pauseMarker);
        start.ArgumentList.Add("--format");
        start.ArgumentList.Add(((ComboBoxItem)OutputFormat.SelectedItem).Tag.ToString()!);
        if (optimize) start.ArgumentList.Add("--optimize-in-place");
        else if (inPlace) start.ArgumentList.Add("--in-place");
        else
        {
            start.ArgumentList.Add("--output");
            start.ArgumentList.Add(destination);
        }
        if (!string.IsNullOrWhiteSpace(IncludeExtensions.Text))
        {
            start.ArgumentList.Add("--include-ext");
            start.ArgumentList.Add(IncludeExtensions.Text.Trim());
        }
        if (!string.IsNullOrWhiteSpace(ExcludeExtensions.Text))
        {
            start.ArgumentList.Add("--exclude-ext");
            start.ArgumentList.Add(ExcludeExtensions.Text.Trim());
        }
        if (Recursive.IsChecked == true) start.ArgumentList.Add("--recursive");
        if (Overwrite.IsChecked == true) start.ArgumentList.Add("--overwrite");
        if (AdaptiveWorkers.IsChecked == true) start.ArgumentList.Add("--adaptive-workers");
        start.ArgumentList.Add("--batch-size");
        start.ArgumentList.Add(batchSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.ArgumentList.Add("--threads");
        start.ArgumentList.Add(threads.ToString());
        start.ArgumentList.Add("--compression");
        start.ArgumentList.Add(((ComboBoxItem)Compression.SelectedItem).Tag.ToString()!);
        start.ArgumentList.Add("--name-pattern");
        start.ArgumentList.Add(pattern);
        start.ArgumentList.Add("--compression-effort");
        start.ArgumentList.Add(((int)EffortSlider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!optimize && AutoContrast.IsChecked == true) start.ArgumentList.Add("--auto-contrast");
        if (!optimize && ContrastSlider.Value != 0)
        {
            start.ArgumentList.Add("--contrast");
            start.ArgumentList.Add(((int)ContrastSlider.Value).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!optimize && SharpenSlider.Value > 0)
        {
            start.ArgumentList.Add("--sharpen");
            start.ArgumentList.Add(SharpenSlider.Value.ToString("F1", System.Globalization.CultureInfo.InvariantCulture));
        }
        if (!optimize && ResizeImages.IsChecked == true && !string.IsNullOrWhiteSpace(MaxWidthInput.Text))
        {
            start.ArgumentList.Add("--max-width");
            start.ArgumentList.Add(MaxWidthInput.Text.Trim());
        }

        try
        {
            _running = true;
            _cancelRequested = false;
            _paused = false;
            _pausedTotal = TimeSpan.Zero;
            _pauseStarted = TimeSpan.Zero;
            _completedFiles = 0;
            _totalFiles = 0;
            _jobClock.Restart();
            PauseButton.Content = "Pause";
            PauseButton.IsEnabled = true;
            BatchSizeInput.IsEnabled = false;
            MetricsText.Text = "Speed: calculating | Elapsed: 00:00:00 | ETA: —";
            StartButton.IsEnabled = false;
            ScanButton.IsEnabled = false;
            SourcePath.IsEnabled = false;
            SingleFileMode.IsEnabled = false;
            OutputPath.IsEnabled = false;
            BrowseOutput.IsEnabled = false;
            InPlace.IsEnabled = false;
            OptimizeInPlace.IsEnabled = false;
            OutputFormat.IsEnabled = false;
            Recursive.IsEnabled = false;
            Overwrite.IsEnabled = false;
            Compression.IsEnabled = false;
            Threads.IsEnabled = false;
            AdaptiveWorkers.IsEnabled = false;
            ResizeImages.IsEnabled = false;
            MaxWidthInput.IsEnabled = false;
            NamePattern.IsEnabled = false;
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
            _lastReport = Path.Combine(inPlace || optimize ? sourceDirectory : Path.GetFullPath(destination), "reakiller-report.csv");
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
            if (_selectionListFile is not null)
            {
                try { File.Delete(_selectionListFile); } catch (IOException) { }
                _selectionListFile = null;
            }
            if (_stopMarker is not null)
            {
                try { File.Delete(_stopMarker); } catch (IOException) { }
                _stopMarker = null;
            }
            if (_pauseMarker is not null)
            {
                try { File.Delete(_pauseMarker); } catch (IOException) { }
                _pauseMarker = null;
            }
            _running = false;
            _jobClock.Stop();
            PauseButton.IsEnabled = false;
            PauseButton.Content = "Pause";
            BatchSizeInput.IsEnabled = true;
            StartButton.IsEnabled = true;
            ScanButton.IsEnabled = true;
            SourcePath.IsEnabled = true;
            SingleFileMode.IsEnabled = true;
            OutputPath.IsEnabled = InPlace.IsChecked != true && OptimizeInPlace.IsChecked != true;
            BrowseOutput.IsEnabled = InPlace.IsChecked != true && OptimizeInPlace.IsChecked != true;
            InPlace.IsEnabled = true;
            OptimizeInPlace.IsEnabled = true;
            OutputFormat.IsEnabled = OptimizeInPlace.IsChecked != true;
            Recursive.IsEnabled = SingleFileMode.IsChecked != true;
            Overwrite.IsEnabled = true;
            Compression.IsEnabled = true;
            Threads.IsEnabled = true;
            AdaptiveWorkers.IsEnabled = true;
            ResizeImages.IsEnabled = true;
            MaxWidthInput.IsEnabled = ResizeImages.IsChecked == true;
            NamePattern.IsEnabled = true;
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
        bool AutoContrast, int Threads, int? MaxWidth, bool Recursive, bool Overwrite, string NamePattern);

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
        {
            if (SingleFileMode.IsChecked == true)
            {
                _selectedFiles.Clear();
                _selectedFiles.AddRange(paths.Where(path => File.Exists(path) && Extensions.Contains(Path.GetExtension(path))).Distinct(StringComparer.OrdinalIgnoreCase));
                SourcePath.Text = _selectedFiles.Count == 1 ? _selectedFiles[0] : $"{_selectedFiles.Count} files selected";
                FileCountText.Text = $"{_selectedFiles.Count:N0} selected image(s)";
            }
            else if (Directory.Exists(paths[0])) SourcePath.Text = paths[0];
            else if (File.Exists(paths[0])) SourcePath.Text = paths[0];
        }
    }

    private void SavePreset_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(Threads.Text, out var threads) || threads < 1 || threads > 64)
        {
            MessageBox.Show("Workers must be between 1 and 64.", "Invalid settings");
            return;
        }
        int? width = null;
        if (ResizeImages.IsChecked == true && !string.IsNullOrWhiteSpace(MaxWidthInput.Text))
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
            Recursive.IsChecked == true, Overwrite.IsChecked == true, NamePattern.Text);
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
            ResizeImages.IsChecked = preset.MaxWidth.HasValue;
            Recursive.IsChecked = preset.Recursive;
            Overwrite.IsChecked = preset.Overwrite;
            NamePattern.Text = string.IsNullOrWhiteSpace(preset.NamePattern) ? "{name}_{ext}" : preset.NamePattern;
        }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Unable to load preset"); }
    }

    private async void Preview_Click(object sender, RoutedEventArgs e)
    {
        if (_running) return;
        string source = SingleFileMode.IsChecked == true ? _selectedFiles.FirstOrDefault() ?? "" : SourcePath.Text.Trim().Trim('"');
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
            if (ResizeImages.IsChecked == true && int.TryParse(MaxWidthInput.Text, out var width) && width > 0)
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

    private void UpdateMetrics(int completed, int total)
    {
        _completedFiles = completed;
        _totalFiles = total;
        var elapsed = _jobClock.Elapsed - _pausedTotal - (_paused ? _jobClock.Elapsed - _pauseStarted : TimeSpan.Zero);
        var speed = elapsed.TotalSeconds > 0 ? completed / elapsed.TotalSeconds : 0;
        var eta = speed > 0 && total >= completed ? TimeSpan.FromSeconds((total - completed) / speed).ToString(@"hh\:mm\:ss") : "—";
        MetricsText.Text = $"Speed: {speed:F2} images/sec | Elapsed: {elapsed:hh\\:mm\\:ss} | ETA: {eta}";
    }

    private void Pause_Click(object sender, RoutedEventArgs e)
    {
        if (!_running || _cancelRequested || _pauseMarker is null) return;
        try
        {
            if (!_paused)
            {
                File.WriteAllText(_pauseMarker, "PAUSE");
                _pauseStarted = _jobClock.Elapsed;
                _paused = true;
                PauseButton.Content = "Resume";
                SummaryText.Text = "Pausing: active file conversions may finish before workers become idle.";
            }
            else
            {
                File.Delete(_pauseMarker);
                _pausedTotal += _jobClock.Elapsed - _pauseStarted;
                _paused = false;
                PauseButton.Content = "Pause";
                SummaryText.Text = "Resumed conversion.";
            }
            UpdateMetrics(_completedFiles, _totalFiles);
        }
        catch (Exception ex) { MessageBox.Show("Pause/resume failed: " + ex.Message, "Conversion control"); }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (!_running || _cancelRequested || _stopMarker is null) return;
        try
        {
            File.WriteAllText(_stopMarker, "STOP");
            if (_pauseMarker is not null && File.Exists(_pauseMarker)) File.Delete(_pauseMarker);
            PauseButton.IsEnabled = false;
            _cancelRequested = true;
            CancelButton.IsEnabled = false;
            ProgressText.Text = "Stopping after active conversions...";
            SummaryText.Text = "Stop requested. Completed PNGs will remain. Uncommitted conversions will be discarded.";
        }
        catch (Exception ex)
        {
            MessageBox.Show("Unable to request a safe stop: " + ex.Message, "Stop error");
        }
    }
}
