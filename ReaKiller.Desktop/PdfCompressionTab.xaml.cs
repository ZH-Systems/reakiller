using System.Collections.ObjectModel;
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
    private CancellationTokenSource? _stop;

    public PdfCompressionTab()
    {
        InitializeComponent();
        Results.ItemsSource = _results;
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
        _files.Clear();
        _files.AddRange(picker.FileNames.Distinct(StringComparer.OrdinalIgnoreCase));
        SelectionSummary.Text = _files.Count == 1 ? Path.GetFileName(_files[0]) : $"{_files.Count} PDF files selected";
        SelectedCount.Text = $"{_files.Count:N0} selected";
        if (_files.Count > 0 && string.IsNullOrWhiteSpace(DestinationPath.Text))
            DestinationPath.Text = Path.Combine(Path.GetDirectoryName(_files[0])!, "Compressed");
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

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_stop is not null) return;
        if (_files.Count == 0 || _files.Any(p => !File.Exists(p)))
        {
            MessageBox.Show("Select existing PDF documents first.", "PDF compression");
            return;
        }
        var destination = DestinationPath.Text.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(destination))
        {
            MessageBox.Show("Choose an output folder.", "PDF compression");
            return;
        }
        try { Directory.CreateDirectory(destination); }
        catch (Exception ex) { MessageBox.Show(ex.Message, "Output folder error"); return; }
        var tokenSource = new CancellationTokenSource();
        _stop = tokenSource;
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        _results.Clear();
        Progress.Value = 0;
        int processed = 0, succeeded = 0, skipped = 0, failed = 0;
        try
        {
            var preset = ((ComboBoxItem)Preset.SelectedItem).Tag?.ToString() ?? "ebook";
            var files = _files.ToArray();
            foreach (var source in files)
            {
                if (tokenSource.IsCancellationRequested) break;
                var before = new FileInfo(source).Length;
                var output = Path.Combine(destination, Path.GetFileNameWithoutExtension(source) + "_compressed.pdf");
                ProgressText.Text = $"{processed} / {files.Length}";
                SummaryText.Text = "Processing: " + Path.GetFileName(source);
                var temp = Path.Combine(destination, ".reakiller-" + Guid.NewGuid().ToString("N") + ".pdf");
                string status;
                string after = "—";
                try
                {
                    if (string.Equals(Path.GetFullPath(source), Path.GetFullPath(output), StringComparison.OrdinalIgnoreCase))
                        throw new IOException("Input and output path cannot match.");
                    if (File.Exists(output))
                    {
                        status = "Skipped (exists)";
                        skipped++;
                    }
                    else
                    {
                        var result = await Task.Run(() =>
                            NativePdfOptimizer.Compress(source, temp, preset, tokenSource.Token), tokenSource.Token);
                        var written = result.OutputBytes;

                        if (SkipLarger.IsChecked == true && written >= before)
                        {
                            status = "Skipped (not smaller)";
                            skipped++;
                        }
                        else
                        {
                            File.Move(temp, output);
                            status = "Compressed";
                            after = Size(written);
                            succeeded++;
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    status = "Stopped";
                    tokenSource.Cancel();
                }
                catch (Exception ex)
                {
                    status = "Failed: " + ex.Message;
                    failed++;
                }
                finally
                {
                    try { if (File.Exists(temp)) File.Delete(temp); } catch (IOException) { }
                }
                _results.Add(new PdfResult(Path.GetFileName(source), Size(before), after, status));
                processed++;
                Progress.Value = 100d * processed / files.Length;
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
        StopButton.IsEnabled = false;
    }
}
