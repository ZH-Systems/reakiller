using Transflux.Core;
using Xunit;

public sealed class QueueJobTests
{
    private static string Root => Path.GetFullPath(Path.GetTempPath());
    private static QueueJob Job(QueueState state, params string[] options) => new(Guid.NewGuid(),
        new[] { "xml-to-csv", Path.Combine(Root, "source.xml"), "--output", Path.Combine(Root, "csv") }.Concat(options).ToArray(),
        DateTimeOffset.UtcNow, state);
    [Fact]
    public void RunningJobRecoversAsInterrupted()
    {
        Assert.Equal(QueueState.Interrupted, Job(QueueState.Running).Recover().State);
        Assert.Equal(QueueState.Pending, Job(QueueState.Pending).Recover().State);
    }
    [Fact]
    public void RetryRemovesOverwriteButPreservesColumnValue()
    {
        var retry = Job(QueueState.Failed, "--overwrite", "--column", "--overwrite").Retry();
        Assert.Equal(QueueState.Pending, retry.State);
        Assert.Equal(new[] { "--column", "--overwrite" }, retry.Arguments.Skip(4).ToArray());
        retry.Validate();
    }
    [Fact]
    public void RejectsUnknownOptionAndDuplicateOutput()
    {
        Assert.Throws<InvalidDataException>(() => Job(QueueState.Pending, "--internal-pdf-worker").Validate());
        Assert.Throws<InvalidDataException>(() => Job(QueueState.Pending, "--output", Root).Validate());
    }
    [Fact]
    public void CompletedJobsCannotBeRetried()
    {
        Assert.Throws<InvalidOperationException>(() => Job(QueueState.Completed).Retry());
    }
}
