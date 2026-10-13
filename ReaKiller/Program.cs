using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using ImageMagick;

return await ReaKiller.RunAsync(args);

internal static class ReaKiller
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".img", ".dcm", ".dicom", ".jpg", ".jpeg", ".tif", ".tiff"
    };

    private sealed record Options(
        string Source, string Destination, bool Recursive, int Threads,
        int? MaxWidth, int? MaxHeight, bool Overwrite, bool DryRun);

    private sealed record Result(string Source, string Output, string Status, string Detail);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return args.Length == 0 ? 2 : 0;
        }

        Options options;
        try { options = Parse(args); }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine("Argument error: " + ex.Message);
            PrintUsage();
            return 2;
        }

        if (!File.Exists(options.Source) && !Directory.Exists(options.Source))
        {
            Console.Error.WriteLine("Source does not exist: " + options.Source);
            return 2;
        }

        var sourceIsFile = File.Exists(options.Source);
        var sourceRoot = sourceIsFile
            ? Path.GetDirectoryName(Path.GetFullPath(options.Source))!
            : Path.GetFullPath(options.Source);
        var destination = Path.GetFullPath(options.Destination);
        if (!sourceIsFile && IsUnderOrEqual(destination, sourceRoot))
        {
            Console.Error.WriteLine("Output directory must be outside the input directory to avoid accidental recursive processing.");
            return 2;
        }

        var inputFiles = sourceIsFile
            ? new[] { Path.GetFullPath(options.Source) }
            : Directory.EnumerateFiles(
                sourceRoot, "*",
                options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(p => Supported.Contains(Path.GetExtension(p)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (sourceIsFile && !Supported.Contains(Path.GetExtension(options.Source)))
        {
            Console.Error.WriteLine("Unsupported extension: " + Path.GetExtension(options.Source));
            return 2;
        }

        if (!options.DryRun) Directory.CreateDirectory(destination);
        Console.WriteLine($"ReaKiller: {inputFiles.Length} input file(s), {options.Threads} worker(s).");
        var results = new ConcurrentBag<Result>();
        var total = 0;

        await Parallel.ForEachAsync(inputFiles,
            new ParallelOptions { MaxDegreeOfParallelism = options.Threads },
            (file, _) =>
            {
                try
                {
                    var relative = Path.GetRelativePath(sourceRoot, file);
                    var destinationSubfolder = Path.Combine(destination,
                        Path.GetDirectoryName(relative) ?? "");
                    // Include the original extension to avoid collisions such as image.jpg and image.tif.
                    var stem = Path.GetFileNameWithoutExtension(file);
                    var extensionTag = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                    var outputStem = $"{stem}_{extensionTag}";
                    if (options.DryRun)
                    {
                        results.Add(new Result(file, Path.Combine(destinationSubfolder,
                            outputStem + ".png"), "DRY_RUN", ""));
                        return ValueTask.CompletedTask;
                    }

                    Directory.CreateDirectory(destinationSubfolder);
                    var readSettings = new MagickReadSettings();
                    if (Path.GetExtension(file).Equals(".img", StringComparison.OrdinalIgnoreCase))
                    {
                        var signature = ReadSignature(file);
                        if (signature == "JPEG")
                            readSettings.Format = MagickFormat.Jpeg;
                        else if (signature == "TIFF")
                            readSettings.Format = MagickFormat.Tiff;
                        else if (signature == "DICOM")
                            readSettings.Format = MagickFormat.Dcm;
                        else
                            throw new NotSupportedException(
                                "Unrecognized .IMG signature. This file is not a JPEG/TIFF/DICOM variant supported by the initial decoder.");
                    }

                    using var pages = new MagickImageCollection();
                    pages.Read(file, readSettings);
                    if (pages.Count == 0) throw new InvalidDataException("No image pages decoded.");

                    var created = new List<string>();
                    try
                    {
                        for (var index = 0; index < pages.Count; index++)
                        {
                            var image = pages[index];
                            var pageSuffix = pages.Count > 1 ? $"_p{index + 1:0000}" : "";
                            var output = Path.Combine(destinationSubfolder, outputStem + pageSuffix + ".png");
                            if (File.Exists(output) && !options.Overwrite)
                            {
                                results.Add(new Result(file, output, "SKIPPED", "Output exists (use --overwrite)."));
                                continue;
                            }

                            // Optional downscaling. Without these flags, preserve source dimensions.
                            if (options.MaxWidth.HasValue || options.MaxHeight.HasValue)
                            {
                                var widthLimit = options.MaxWidth ?? int.MaxValue;
                                var heightLimit = options.MaxHeight ?? int.MaxValue;
                                var ratio = Math.Min(1d, Math.Min(
                                    (double)widthLimit / image.Width, (double)heightLimit / image.Height));
                                if (ratio < 1d)
                                {
                                    var width = Math.Max(1, (uint)Math.Round(image.Width * ratio));
                                    var height = Math.Max(1, (uint)Math.Round(image.Height * ratio));
                                    image.FilterType = FilterType.Lanczos;
                                    image.Resize(width, height);
                                }
                            }

                            // Write atomically so failed conversions never leave misleading PNG outputs.
                            var temporary = output + "." + Guid.NewGuid().ToString("N") + ".tmp";
                            try
                            {
                                image.Strip();
                                image.Write(temporary, MagickFormat.Png);
                                if (File.Exists(output))
                                    File.Move(temporary, output, true);
                                else
                                    File.Move(temporary, output);
                                created.Add(output);
                                results.Add(new Result(file, output, "OK",
                                    $"{image.Width}x{image.Height}; {new FileInfo(output).Length} bytes"));
                            }
                            finally
                            {
                                if (File.Exists(temporary)) File.Delete(temporary);
                            }
                        }
                    }
                    catch
                    {
                        // No source images are ever modified.
                        foreach (var path in created) { try { File.Delete(path); } catch { } }
                        throw;
                    }
                }
                catch (Exception ex)
                {
                    results.Add(new Result(file, "", "FAILED", ex.GetType().Name + ": " + ex.Message));
                    Console.Error.WriteLine("FAILED " + file + ": " + ex.Message);
                }
                finally
                {
                    var done = Interlocked.Increment(ref total);
                    Console.WriteLine($"[{done}/{inputFiles.Length}] {Path.GetFileName(file)}");
                }
                return ValueTask.CompletedTask;
            });

        var ordered = results.OrderBy(r => r.Source, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.Output, StringComparer.OrdinalIgnoreCase).ToArray();
        if (!options.DryRun)
        {
            var report = Path.Combine(destination, "reakiller-report.csv");
            await File.WriteAllLinesAsync(report,
                new[] { "Source,Output,Status,Detail" }.Concat(ordered.Select(r =>
                    string.Join(",", Escape(r.Source), Escape(r.Output),
                        Escape(r.Status), Escape(r.Detail)))), Encoding.UTF8);
            Console.WriteLine("Report: " + report);
        }

        var failed = ordered.Count(r => r.Status == "FAILED");
        var succeeded = ordered.Count(r => r.Status == "OK");
        var skipped = ordered.Count(r => r.Status == "SKIPPED");
        Console.WriteLine($"Complete: {succeeded} outputs, {skipped} skipped, {failed} failed.");
        return failed > 0 ? 1 : 0;
    }

    private static string Escape(string value) =>
        "\"" + value.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"";

    private static bool IsUnderOrEqual(string path, string directory)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        return full.Equals(root, StringComparison.OrdinalIgnoreCase)
            || full.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string ReadSignature(string path)
    {
        Span<byte> buffer = stackalloc byte[132];
        using var input = File.OpenRead(path);
        var count = input.Read(buffer);
        if (count >= 3 && buffer[0] == 0xFF && buffer[1] == 0xD8 && buffer[2] == 0xFF)
            return "JPEG";
        if (count >= 4 &&
            ((buffer[0] == 0x49 && buffer[1] == 0x49 && buffer[2] == 0x2A && buffer[3] == 0x00) ||
             (buffer[0] == 0x4D && buffer[1] == 0x4D && buffer[2] == 0x00 && buffer[3] == 0x2A)))
            return "TIFF";
        if (count >= 132 && buffer[128] == (byte)'D' && buffer[129] == (byte)'I'
            && buffer[130] == (byte)'C' && buffer[131] == (byte)'M')
            return "DICOM";
        return "UNKNOWN";
    }

    private static Options Parse(string[] args)
    {
        var offset = args[0].Equals("convert", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        if (args.Length <= offset) throw new ArgumentException("Missing input path.");
        var source = args[offset];
        string? output = null;
        bool recursive = false, overwrite = false, dryRun = false;
        int threads = Math.Min(4, Environment.ProcessorCount);
        int? maxWidth = null, maxHeight = null;

        for (var index = offset + 1; index < args.Length; index++)
        {
            var flag = args[index];
            string Value()
            {
                if (++index >= args.Length)
                    throw new ArgumentException("Missing value for " + flag);
                return args[index];
            }
            switch (flag.ToLowerInvariant())
            {
                case "-o":
                case "--output": output = Value(); break;
                case "-r":
                case "--recursive": recursive = true; break;
                case "--overwrite": overwrite = true; break;
                case "--dry-run": dryRun = true; break;
                case "--threads": threads = ParsePositive(Value(), flag); break;
                case "--max-width": maxWidth = ParsePositive(Value(), flag); break;
                case "--max-height": maxHeight = ParsePositive(Value(), flag); break;
                case "--compression":
                    var compression = Value().ToLowerInvariant();
                    if (compression is not ("lossless" or "max"))
                        throw new ArgumentException("--compression must be lossless or max.");
                    // Both currently use ImageMagick lossless PNG encoding.
                    break;
                default: throw new ArgumentException("Unknown option: " + flag);
            }
        }

        if (string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Specify an output directory using --output.");
        return new Options(source, output, recursive, threads,
            maxWidth, maxHeight, overwrite, dryRun);
    }

    private static int ParsePositive(string value, string name)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result)
            || result <= 0)
            throw new ArgumentException(name + " must be a positive integer.");
        return result;
    }

    private static void PrintUsage()
    {
        Console.WriteLine("""
            ReaKiller: IMG, DICOM, JPEG, TIFF to PNG.
            Usage:
              ReaKiller.exe convert <file-or-directory> --output <directory> [options]
            Options:
              --recursive, -r         Traverse subdirectories
              --threads <n>          Parallel workers (default up to 4)
              --max-width <pixels>   Optional maximum output width
              --max-height <pixels>  Optional maximum output height
              --compression <mode>   lossless|max (both currently lossless)
              --overwrite           Replace existing output files
              --dry-run             Preview input/output paths
              --help                Show help

            Outputs use the source extension as a suffix (e.g. image_img.png).
            TIFF and multi-frame DICOM pages get _p0001, _p0002, etc.
            Source files are never changed or deleted.
            """);
    }
}
