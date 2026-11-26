using Transflux.Core;
using Xunit;

public sealed class ConversionCoreTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "transflux-test-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(root, "input");
    private string Output => Path.Combine(root, "output");
    public ConversionCoreTests() => Directory.CreateDirectory(Source);
    public void Dispose() => Directory.Delete(root, true);
    private ConversionJob Job(ExistingOutputPolicy policy = ExistingOutputPolicy.Skip) =>
        new(ConversionOperation.XmlToCsv, Source, Output, ExistingOutput: policy);
    private sealed class CopyConverter(bool fail = false, bool cancel = false) : IFileConverter
    {
        public ConversionOperation Operation => ConversionOperation.XmlToCsv;
        public IReadOnlyCollection<string> SourceExtensions => new[] { ".xml", ".txt" };
        public string OutputExtension => ".csv";
        public async Task ConvertAsync(string source, string temporaryOutput, CancellationToken token)
        {
            await File.WriteAllTextAsync(temporaryOutput, await File.ReadAllTextAsync(source, token), token);
            if (fail) throw new IOException("Test failure.");
            if (cancel) throw new OperationCanceledException(token);
        }
    }
    [Fact]
    public async Task PreservesSourceRelativePathAndModifiedTime()
    {
        var dir = Directory.CreateDirectory(Path.Combine(Source, "nested")).FullName;
        var file = Path.Combine(dir, "patient.XML");
        File.WriteAllText(file, "sample");
        File.SetLastWriteTimeUtc(file, new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        var expected = File.GetLastWriteTimeUtc(file);
        var result = Assert.Single(await ConversionRunner.RunAsync(Job(), new CopyConverter()));
        Assert.Equal(ConversionStatus.Succeeded, result.Status);
        Assert.Equal(Path.Combine(Output, "nested", "patient.csv"), result.Item.Destination);
        Assert.Equal(expected, File.GetLastWriteTimeUtc(result.Item.Destination));
        Assert.Equal("sample", File.ReadAllText(file));
    }
    [Fact]
    public void RejectsCollisionAndNestedDestination()
    {
        File.WriteAllText(Path.Combine(Source, "a.xml"), "a");
        File.WriteAllText(Path.Combine(Source, "a.txt"), "b");
        Assert.Throws<IOException>(() => ConversionPlanner.Plan(Job(), new CopyConverter()));
        Assert.Throws<ArgumentException>(() => ConversionPlanner.Plan(Job() with { Destination = Path.Combine(Source, "out") }, new CopyConverter()));
    }
    [Fact]
    public async Task FailureLeavesExistingOutputAndCleansTemporaryFile()
    {
        File.WriteAllText(Path.Combine(Source, "a.xml"), "new");
        Directory.CreateDirectory(Output);
        var output = Path.Combine(Output, "a.csv");
        File.WriteAllText(output, "old");
        var result = Assert.Single(await ConversionRunner.RunAsync(Job(ExistingOutputPolicy.Overwrite), new CopyConverter(fail: true)));
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Equal("old", File.ReadAllText(output));
        Assert.Single(Directory.GetFiles(Output));
    }
    [Fact]
    public async Task OutputPoliciesAreExplicit()
    {
        File.WriteAllText(Path.Combine(Source, "a.xml"), "new");
        Directory.CreateDirectory(Output);
        var output = Path.Combine(Output, "a.csv");
        File.WriteAllText(output, "old");
        Assert.Equal(ConversionStatus.Skipped, Assert.Single(await ConversionRunner.RunAsync(Job(), new CopyConverter())).Status);
        Assert.Equal(ConversionStatus.Failed, Assert.Single(await ConversionRunner.RunAsync(Job(ExistingOutputPolicy.Fail), new CopyConverter())).Status);
        Assert.Equal(ConversionStatus.Succeeded, Assert.Single(await ConversionRunner.RunAsync(Job(ExistingOutputPolicy.Overwrite), new CopyConverter())).Status);
        Assert.Equal("new", File.ReadAllText(output));
    }
    [Fact]
    public async Task PreCancelledJobProducesNoOutput()
    {
        File.WriteAllText(Path.Combine(Source, "a.xml"), "new");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => ConversionRunner.RunAsync(Job(), new CopyConverter(), cancellationToken: cancellation.Token));
        Assert.False(Directory.Exists(Output));
    }
}
