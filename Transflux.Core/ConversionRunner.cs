using System.Collections.Concurrent;

namespace Transflux.Core;

public static class ConversionRunner
{
    public static async Task<IReadOnlyList<ConversionResult>> RunAsync(ConversionJob job,
        IFileConverter converter, IProgress<ConversionProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var items = ConversionPlanner.Plan(job, converter, cancellationToken);
        var results = new ConcurrentBag<ConversionResult>();
        var completed = 0;
        var workerLimit = job.Operation == ConversionOperation.PdfToCsv ? 4 : 16;
        var workers = Math.Min(job.Workers, workerLimit);
        await Parallel.ForEachAsync(items, new ParallelOptions { MaxDegreeOfParallelism = workers,
            CancellationToken = cancellationToken }, async (item, token) =>
        {
            var result = await RunItemAsync(job, converter, item, token);
            results.Add(result);
            progress?.Report(new ConversionProgress(Interlocked.Increment(ref completed), items.Count, result));
        });
        return results.OrderBy(result => result.Item.Source, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static async Task<ConversionResult> RunItemAsync(ConversionJob job, IFileConverter converter,
        ConversionItem item, CancellationToken token)
    {
        string? temporary = null;
        try
        {
            token.ThrowIfCancellationRequested();
            if (File.Exists(item.Destination))
            {
                if (job.ExistingOutput == ExistingOutputPolicy.Skip) return new(item, ConversionStatus.Skipped);
                if (job.ExistingOutput == ExistingOutputPolicy.Fail) throw new IOException("Output already exists.");
            }
            var directory = Path.GetDirectoryName(item.Destination)!;
            Directory.CreateDirectory(directory);
            temporary = Path.Combine(directory, ".transflux-" + Guid.NewGuid().ToString("N") + Path.GetExtension(item.Destination));
            // Capture before reading the source.
            var created = File.GetCreationTimeUtc(item.Source);
            var modified = File.GetLastWriteTimeUtc(item.Source);
            var accessed = File.GetLastAccessTimeUtc(item.Source);
            await converter.ConvertAsync(item.Source, temporary, token);
            token.ThrowIfCancellationRequested();
            if (!File.Exists(temporary)) throw new IOException("Converter did not produce an output.");
            if (File.GetLastWriteTimeUtc(item.Source) != modified)
                throw new IOException("Source changed during conversion; output not published.");
            if (job.PreserveTimestamps)
            {
                File.SetCreationTimeUtc(temporary, created);
                File.SetLastWriteTimeUtc(temporary, modified);
                File.SetLastAccessTimeUtc(temporary, accessed);
            }
            token.ThrowIfCancellationRequested();
            try { File.Move(temporary, item.Destination, job.ExistingOutput == ExistingOutputPolicy.Overwrite); }
            catch (IOException) when (job.ExistingOutput == ExistingOutputPolicy.Skip && File.Exists(item.Destination))
            { return new(item, ConversionStatus.Skipped); }
            return new(item, ConversionStatus.Succeeded);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { return new(item, ConversionStatus.Failed, ex.Message); }
        finally
        {
            if (temporary is not null)
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }
}
