using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using MessageBox = System.Windows.MessageBox;

namespace ReaKiller.Desktop;

public partial class MainWindow : Window
{
    private Process? _process;
    private bool _running;
    private bool _cancelRequested;
    private string? _lastReport;
    private static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    { ".img", ".dcm", ".dicom", ".jpg", ".jpeg", ".tif", ".tiff" };

    public MainWindow()
    {
        InitializeComponent();
        Closed += (_, _) =>
        {
            try { if (_process is { HasExited: false }) _process.Kill(entireProcessTree: true); }
            catch { /* Process may already have exited. */ }
        };
    }

    private static string? BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
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

        if (!string.IsNullOrWhiteSpace(MaxWidth.Text)
            && (!int.TryParse(MaxWidth.Text, out int width) || width < 1))
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
        if (!string.IsNullOrWhiteSpace(MaxWidth.Text))
        {
            start.ArgumentList.Add("--max-width");
            start.ArgumentList.Add(MaxWidth.Text.Trim());
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
            MaxWidth.IsEnabled = false;
            CancelButton.IsEnabled = true;
            Progress.Value = 0;
            ProgressText.Text = "Starting...";
            SummaryText.Text = "Conversion in progress";
            Log.Clear();
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
            MaxWidth.IsEnabled = true;
            CancelButton.IsEnabled = false;
        }
    }

    private async Task ReadLinesAsync(StreamReader reader)
    {
        while (await reader.ReadLineAsync() is { } line) AppendLog(line);
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
