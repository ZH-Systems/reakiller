using Transflux.Core;
using Xunit;
using System.Diagnostics;

public sealed class ResourceLimitTests
{
    [Fact]
    public async Task DeepXmlFailsBeforeRecursiveFlattening()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "deep.xml");
            await File.WriteAllTextAsync(source, "<root><record>" +
                string.Concat(Enumerable.Repeat("<n>", 80)) + "x" +
                string.Concat(Enumerable.Repeat("</n>", 80)) + "</record></root>");
            await Assert.ThrowsAsync<InvalidDataException>(() => new XmlCsvConverter().ConvertAsync(
                source, Path.Combine(root, "out.csv"), CancellationToken.None));
        }
        finally { Directory.Delete(root, true); }
    }
    [Fact]
    public void InvalidJobLimitsAreRejected()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Throws<ArgumentOutOfRangeException>(() => WindowsProcessJob.Create(0, 1));
    }
    [Fact]
    public async Task ClosingJobTerminatesAssignedProcess()
    {
        if (!OperatingSystem.IsWindows()) return;
        using var job = WindowsProcessJob.Create(256L * 1024 * 1024, 2);
        using var process = Process.Start(new ProcessStartInfo(
            Path.Combine(Environment.SystemDirectory, "cmd.exe"), "/d /q /k")
            { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true })!;
        try
        {
            job.Assign(process);
            job.Dispose();
            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await process.WaitForExitAsync(deadline.Token);
            Assert.True(process.HasExited);
        }
        finally { if (!process.HasExited) process.Kill(true); }
    }
}
