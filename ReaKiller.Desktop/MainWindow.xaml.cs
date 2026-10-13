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

    private void InPlace_Changed(object sender, RoutedEventArgs e)
    {
        if (OutputPath is null || BrowseOutput is null) return;
        var enable = InPlace.IsChecked != true;
        OutputPath.IsEnabled = enable;
        BrowseOutput.IsEnabled = enable;
    }

    private void AppendLog(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Dispatcher.Invoke(() =>
        {
            Log.AppendText(text + Environment.NewLine);
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
            StartButton.IsEnabled = false;
            CancelButton.IsEnabled = true;
            Progress.Value = 0;
            ProgressText.Text = "Starting...";
            Log.Clear();
            _process = new Process { StartInfo = start, EnableRaisingEvents = true };
            _process.Start();
            var standard = ReadLinesAsync(_process.StandardOutput);
            var errors = ReadLinesAsync(_process.StandardError);
            await _process.WaitForExitAsync();
            await Task.WhenAll(standard, errors);
            ProgressText.Text = _process.ExitCode == 0 ? "Complete" : $"Exited with code {_process.ExitCode}";
            if (_process.ExitCode == 0) Progress.Value = 100;
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
            if (_process is { HasExited: false })
                _process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
    }
}
