namespace Transflux.Core;

public static class ConversionPlanner
{
    public static IReadOnlyList<ConversionItem> Plan(ConversionJob job, IFileConverter converter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(job);
        ArgumentNullException.ThrowIfNull(converter);
        if (job.Operation != converter.Operation) throw new ArgumentException("Operation mismatch.");
        if (job.Workers < 1 || job.Workers > 64) throw new ArgumentOutOfRangeException(nameof(job.Workers));
        if (!Enum.IsDefined(job.ExistingOutput)) throw new ArgumentException("Invalid output policy.");
        var extension = converter.OutputExtension;
        if (string.IsNullOrEmpty(extension) || extension[0] != '.' || extension.Length < 2 ||
            extension.Skip(1).Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("Invalid output extension.");
        var source = Path.GetFullPath(job.Source);
        var destination = Path.GetFullPath(job.Destination);
        var directory = Directory.Exists(source);
        if (!directory && !File.Exists(source)) throw new FileNotFoundException("Source not found.", source);
        var root = directory ? source : Path.GetDirectoryName(source)!;
        if (directory && Inside(destination, root))
            throw new ArgumentException("Destination must be outside the source directory.");
        if (File.Exists(destination)) throw new IOException("Destination is a file.");
        var extensions = new HashSet<string>(converter.SourceExtensions, StringComparer.OrdinalIgnoreCase);
        var options = new EnumerationOptions { RecurseSubdirectories = job.Recursive,
            AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
        IEnumerable<string> candidates = directory ? Directory.EnumerateFiles(source, "*", options) : new[] { source };
        var items = new List<ConversionItem>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!extensions.Contains(Path.GetExtension(file))) continue;
            var relative = Path.GetRelativePath(root, file);
            var output = Path.GetFullPath(Path.Combine(destination, Path.ChangeExtension(relative, extension)));
            if (!Inside(output, destination) || string.Equals(file, output, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Unsafe output path.");
            if (!seen.Add(output)) throw new IOException("Multiple inputs resolve to the same output: " + output);
            items.Add(new ConversionItem(file, output));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (items.Count == 0) throw new InvalidOperationException("No supported input files found.");
        return items.OrderBy(item => item.Source, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool Inside(string path, string root)
    {
        var relative = Path.GetRelativePath(root, path);
        return !Path.IsPathRooted(relative) && relative != ".." &&
            !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal) &&
            !relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal);
    }
}
