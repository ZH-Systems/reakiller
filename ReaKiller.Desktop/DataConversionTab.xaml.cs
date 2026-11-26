using System.Diagnostics;
using Transflux.Core;
using System.IO;
using System.Data;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ReaKiller.Desktop;

public partial class DataConversionTab : System.Windows.Controls.UserControl
{
    private CancellationTokenSource? stop;
    public bool IsBusy => stop is not null || draining;
    private QueueStore? store;
    private List<QueueJob> jobs = new();
    private bool paused = true;
    private bool draining;
    public DataConversionTab()
    {
        InitializeComponent();
        try { store = new QueueStore(); jobs = store.Load(); store.Save(jobs); RefreshQueue(); }
        catch { store?.Dispose(); store = null; QueueStatus.Text = "Queue unavailable. Existing storage was not reset."; }
    }
    public void CloseQueue() { store?.Dispose(); store = null; }
    private void RefreshQueue()
    {
        QueueList.ItemsSource = null;
        QueueList.ItemsSource = jobs.Select(j => new QueueDisplay(j.Id, j.Operation, j.State, j.Attempts, j.Created)).ToList();
        QueueStatus.Text = paused ? "Queue paused" : "Queue enabled";
    }
    private sealed record QueueDisplay(Guid Id, string Operation, QueueState State, int Attempts, DateTimeOffset Created);
    private void Persist(List<QueueJob> next)
    {
        if (store is null) throw new IOException("Persistent queue unavailable.");
        store.Save(next); jobs = next; RefreshQueue();
    }
    private void Change(QueueJob job) => Persist(jobs.Select(j => j.Id == job.Id ? job : j).ToList());
    private async void ResumeQueue(object sender, RoutedEventArgs e) { paused = false; RefreshQueue(); await DrainQueue(); }
    private void PauseQueue(object sender, RoutedEventArgs e) { paused = true; RefreshQueue(); }
    private async void RetryQueue(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueDisplay selected) return;
        try { Change(jobs.Single(j => j.Id == selected.Id).Retry()); await DrainQueue(); }
        catch (Exception) { QueueStatus.Text = "Unable to retry selected job."; }
    }
    private void RemoveQueue(object sender, RoutedEventArgs e)
    {
        if (QueueList.SelectedItem is not QueueDisplay selected) return;
        if (jobs.Any(j => j.Id == selected.Id && j.State == QueueState.Running)) return;
        try { Persist(jobs.Where(j => j.Id != selected.Id).ToList()); }
        catch { QueueStatus.Text = "Unable to remove selected job."; }
    }
    private async Task DrainQueue()
    {
        if (draining || stop is not null || paused || store is null) return;
        draining = true;
        try
        {
            while (!paused && jobs.FirstOrDefault(j => j.State == QueueState.Pending) is QueueJob job)
            {
                // Never silently run a recovered queue: resume is explicit after startup.
                if (job.Arguments.Contains("--overwrite") && System.Windows.MessageBox.Show("This queued job permits replacing existing CSV files. Continue?", "Confirm overwrite", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                { Change(job with { State = QueueState.Cancelled, Updated = DateTimeOffset.UtcNow }); continue; }
                job.Validate();
                var running = job with { State = QueueState.Running, Attempts = job.Attempts + 1, Updated = DateTimeOffset.UtcNow };
                Change(running);
                var state = QueueState.Failed;
                try
                {
                    var exe = Path.Combine(AppContext.BaseDirectory, "ReaKiller.exe");
                    var start = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                    foreach (var arg in running.Arguments) start.ArgumentList.Add(arg);
                    // Reconfirm OCR every execution because its configuration may have changed.
                    if (running.Arguments.Contains("--ocr-config") &&
                        System.Windows.MessageBox.Show("Run queued OCR using the previously selected trusted configuration?", "Confirm OCR", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                        state = QueueState.Cancelled;
                    else
                    {
                        await RunProcess(start, false);
                        state = QueueState.Completed;
                    }
                }
                catch (OperationCanceledException) { state = QueueState.Cancelled; paused = true; }
                catch { state = QueueState.Failed; }
                Change(running with { State = state, Updated = DateTimeOffset.UtcNow });
            }
        }
        catch { paused = true; QueueStatus.Text = "Queue storage or execution failed. Resume only after review."; }
        finally { draining = false; }
    }
    private bool Pdf => Mode.SelectedIndex == 1;
    private void ModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PdfSettings is null || XmlSettings is null || PreviewButton is null || Source is null || PreviewGrid is null) return;
        PdfSettings.Visibility = Pdf ? Visibility.Visible : Visibility.Collapsed;
        XmlSettings.Visibility = Pdf ? Visibility.Collapsed : Visibility.Visible;
        PreviewButton.IsEnabled = Pdf;
        Source.Clear();
        PreviewGrid.ItemsSource = null;
    }
    private void ChooseFile(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = Pdf ? "PDF files|*.pdf" : "XML files|*.xml" };
        if (picker.ShowDialog() == true) SetSource(picker.FileName);
    }
    private static string? Folder()
    {
        using var picker = new System.Windows.Forms.FolderBrowserDialog();
        return picker.ShowDialog() == System.Windows.Forms.DialogResult.OK ? picker.SelectedPath : null;
    }
    private void ChooseFolder(object sender, RoutedEventArgs e) { if (Folder() is string p) SetSource(p); }
    private void ChooseOutput(object sender, RoutedEventArgs e) { if (Folder() is string p) Destination.Text = p; }
    private void ChooseOcr(object sender, RoutedEventArgs e)
    {
        var picker = new Microsoft.Win32.OpenFileDialog { Filter = "Reviewed OCR configuration|*.json" };
        if (picker.ShowDialog() == true) OcrConfig.Text = picker.FileName;
    }
    private void ClearOcr(object sender, RoutedEventArgs e) => OcrConfig.Clear();
    private void SetSource(string path)
    {
        Source.Text = path;
        var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(path));
        if (parent is not null && string.IsNullOrWhiteSpace(Destination.Text))
            Destination.Text = Path.Combine(parent, "CSV");
    }
    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = !IsBusy && e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }
    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        e.Handled = true;
        if (!IsBusy && e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] { Length: 1 } paths)
            SetSource(paths[0]);
    }
    private static IEnumerable<string> Lines(string s) => s.Split(new[] {'\r','\n'}, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    private async void PreviewClick(object sender, RoutedEventArgs e) => await Execute(true);
    private async void RunClick(object sender, RoutedEventArgs e) => await Execute(false);
    private void StopClick(object sender, RoutedEventArgs e) { paused = true; stop?.Cancel(); }
    public void Cancel() { paused = true; stop?.Cancel(); }

    private async Task Execute(bool preview)
    {
        if (IsBusy) return;
        try
        {
            if (string.IsNullOrWhiteSpace(Source.Text)) throw new ArgumentException("Select a source first.");
            var source = Path.GetFullPath(Source.Text.Trim());
            if (!Directory.Exists(source) && !File.Exists(source)) throw new ArgumentException("Select an existing source.");
            if (File.Exists(source) && !Path.GetExtension(source).Equals(Pdf ? ".pdf" : ".xml", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Source does not match the selected operation.");
            if (preview && (!Pdf || !File.Exists(source))) throw new ArgumentException("Preview requires one PDF.");
            if (!int.TryParse(Workers.Text, out var workers) || workers < 1 || workers > (Pdf ? 4 : 16))
                throw new ArgumentException("Invalid worker count.");
            var executable = Path.Combine(AppContext.BaseDirectory, "ReaKiller.exe");
            if (!File.Exists(executable)) throw new IOException("The bundled CLI is missing.");
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true };
            void Add(params string[] values) { foreach (var value in values) start.ArgumentList.Add(value); }
            Add(Pdf ? "pdf-to-csv" : "xml-to-csv", source, "--threads", workers.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (preview) Add("--preview");
            else
            {
                if (string.IsNullOrWhiteSpace(Destination.Text)) throw new ArgumentException("Select an output folder.");
                Add("--output", Path.GetFullPath(Destination.Text.Trim()));
                if (Recursive.IsChecked == true) Add("--recursive");
                if (Overwrite.IsChecked == true)
                {
                    if (System.Windows.MessageBox.Show("Replace existing CSV outputs?", "Confirm overwrite", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    Add("--overwrite");
                }
            }
            if (Pdf)
            {
                Add("--first-page", FirstPage.Text.Trim());
                if (!string.IsNullOrWhiteSpace(LastPage.Text)) Add("--last-page", LastPage.Text.Trim());
                foreach (var boundary in Boundaries.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) Add("--boundary", boundary);
                foreach (var header in Lines(PdfHeaders.Text)) Add("--header", header);
                if (KeepHeaders.IsChecked == true) Add("--keep-headers");
                if (!string.IsNullOrWhiteSpace(OcrConfig.Text))
                {
                    if (workers != 1) throw new ArgumentException("OCR requires one worker.");
                    if (System.Windows.MessageBox.Show("Enable experimental OCR using the selected trusted configuration?", "Confirm OCR", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    Add("--ocr-config", OcrConfig.Text);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(RecordPath.Text)) Add("--record-path", RecordPath.Text.Trim());
                foreach (var column in Lines(XmlColumns.Text)) Add("--column", column);
            }
            if (!preview)
            {
                var job = new QueueJob(Guid.NewGuid(), start.ArgumentList.ToArray(), DateTimeOffset.UtcNow, QueueState.Pending);
                job.Validate();
                Persist(jobs.Append(job).ToList());
                QueueStatus.Text = "Job added. Resume queue to execute.";
                if (!paused) await DrainQueue();
                return;
            }
            await RunProcess(start, true);
        }
        catch (OperationCanceledException) { Status.Text = "Stopped."; }
        catch (Exception ex) { Status.Text = ex.Message; }
    }

    private async Task RunProcess(ProcessStartInfo start, bool preview)
    {
        try
        {
            stop = new CancellationTokenSource(TimeSpan.FromHours(2));
            var token = stop.Token;
            SettingsPanel.IsEnabled = RunButton.IsEnabled = PreviewButton.IsEnabled = false;
            StopButton.IsEnabled = true;
            Activity.IsIndeterminate = true;
            Status.Text = preview ? "Extracting preview..." : "Converting...";
            PreviewGrid.ItemsSource = null;
            Log.Clear();
            using var containment = WindowsProcessJob.Create(5L * 1024 * 1024 * 1024, 20);
            using var process = Process.Start(start) ?? throw new IOException("Unable to start conversion.");
            try { containment.Assign(process); }
            catch { if (!process.HasExited) process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); throw; }
            // Drain both streams concurrently; retain only bounded preview text, not document logs.
            var stdout = ReadBounded(process.StandardOutput, preview ? 4 * 1024 * 1024 : 0, token);
            var stderr = ReadBounded(process.StandardError, 0, token);
            try
            {
                await process.WaitForExitAsync(token);
                var data = await stdout;
                await stderr;
                if (process.ExitCode != 0) throw new IOException("Conversion failed (exit " + process.ExitCode + "). Use CLI diagnostics if needed.");
                if (preview)
                {
                    var rows = JsonSerializer.Deserialize<string[][]>(data) ?? throw new InvalidDataException("Invalid preview.");
                    if (rows.Length > 20 || rows.Length == 0 || rows[0].Length > 512) throw new InvalidDataException("Invalid preview dimensions.");
                    var table = new DataTable();
                    for (int i = 0; i < rows[0].Length; i++) table.Columns.Add("Column " + (i + 1), typeof(string));
                    foreach (var row in rows)
                    {
                        if (row.Length != table.Columns.Count) throw new InvalidDataException("Inconsistent preview columns.");
                        table.Rows.Add(row.Cast<object>().ToArray());
                    }
                    PreviewGrid.ItemsSource = table.DefaultView;
                }
                Status.Text = preview ? "Preview ready. Verify against the source." : "Conversion complete.";
                Log.Text = Status.Text;
            }
            finally
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                containment.Dispose();
                await process.WaitForExitAsync();
                try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { } catch (InvalidDataException) { }
            }
        }
        catch (OperationCanceledException) { Status.Text = "Stopped. Check output folder for completed files."; throw; }
        catch (Exception) { Status.Text = "Conversion failed."; throw; }
        finally
        {
            stop?.Dispose(); stop = null;
            SettingsPanel.IsEnabled = RunButton.IsEnabled = true;
            PreviewButton.IsEnabled = Pdf;
            StopButton.IsEnabled = Activity.IsIndeterminate = false;
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader, int limit, CancellationToken token)
    {
        var buffer = new char[4096];
        var result = new System.Text.StringBuilder();
        bool overflow = false;
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), token)) > 0)
        {
            if (limit == 0) continue;
            if (result.Length + count > limit) overflow = true;
            else if (!overflow) result.Append(buffer, 0, count);
        }
        if (overflow) throw new InvalidDataException("Preview exceeds size limit.");
        return result.ToString();
    }
}
