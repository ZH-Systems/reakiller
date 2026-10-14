using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using ImageMagick;
using FellowOakDicom;

return await ReaKiller.RunAsync(args);

public static class ReaKiller
{
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
    {
        ".img", ".dcm", ".dicom", ".jpg", ".jpeg", ".png", ".tif", ".tiff"
    };

    private sealed record Options(
        string Source, string Destination, bool Recursive, int Threads,
        int? MaxWidth, int? MaxHeight, bool Overwrite, bool DryRun, bool InPlace, string Compression, int CompressionEffort, int Contrast, double Sharpen, bool AutoContrast, string NamePattern, string? StopFile, string? FilesFrom, string OutputFormat, bool OptimizeInPlace);

    private sealed record Result(string Source, string Output, string Status, string Detail);

    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
            return await RunInteractiveAsync();

        if (args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return 0;
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
        var destination = options.InPlace || options.OptimizeInPlace ? sourceRoot : Path.GetFullPath(options.Destination);
        if (!options.InPlace && !options.OptimizeInPlace && !sourceIsFile && IsUnderOrEqual(destination, sourceRoot))
        {
            Console.Error.WriteLine("Output directory must be outside the input directory to avoid accidental recursive processing.");
            return 2;
        }

        var inputFiles = options.FilesFrom is not null
            ? File.ReadAllLines(options.FilesFrom).Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray()
            : sourceIsFile
            ? new[] { Path.GetFullPath(options.Source) }
            : Directory.EnumerateFiles(
                sourceRoot, "*",
                options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly)
                .Where(p => Supported.Contains(Path.GetExtension(p)))
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (options.FilesFrom is not null && (inputFiles.Length == 0 || inputFiles.Any(path => !File.Exists(path) || !Supported.Contains(Path.GetExtension(path)))))
        {
            Console.Error.WriteLine("File selection contains missing or unsupported images.");
            return 2;
        }

        if (options.FilesFrom is null && sourceIsFile && !Supported.Contains(Path.GetExtension(options.Source)))
        {
            Console.Error.WriteLine("Unsupported extension: " + Path.GetExtension(options.Source));
            return 2;
        }

        // Resolve names before conversion so jobs cannot overwrite each other's outputs.
        var outputNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var assignedNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < inputFiles.Length; i++)
        {
            var file = inputFiles[i];
            string stem;
            try { stem = ResolveName(options.NamePattern, file, i + 1); }
            catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine("Naming error for " + file + ": " + ex.Message);
                return 2;
            }
            var relativeFolder = options.FilesFrom is not null ? "" : Path.GetDirectoryName(Path.GetRelativePath(sourceRoot, file)) ?? "";
            var targetFolder = (options.InPlace || options.OptimizeInPlace) && options.FilesFrom is not null
                ? Path.GetDirectoryName(file)! : Path.GetFullPath(Path.Combine(destination, relativeFolder));
            var target = Path.Combine(targetFolder, stem + (options.OutputFormat == "jpg" ? ".jpg" : ".png"));
            if (assignedNames.TryGetValue(target, out var other))
            {
                Console.Error.WriteLine($"Naming collision: {file} and {other} both produce {target}. Add {{index}} or {{name}} to the pattern.");
                return 2;
            }
            assignedNames.Add(target, file);
            outputNames.Add(file, stem);
        }

        if (!options.DryRun) Directory.CreateDirectory(destination);
        Console.WriteLine($"ReaKiller: {inputFiles.Length} input file(s), {options.Threads} worker(s).");
        if (options.InPlace) Console.WriteLine("IN-PLACE MODE: originals will be deleted ONLY after successful PNG conversion and verification.");
        var results = new ConcurrentBag<Result>();
        var total = 0;
        var progressLock = new object();

        await Parallel.ForEachAsync(inputFiles,
            new ParallelOptions { MaxDegreeOfParallelism = options.Threads },
            (file, _) =>
            {
                var fileResults = new List<Result>();
                void Record(Result result) { results.Add(result); fileResults.Add(result); }
                try
                {
                    if (options.StopFile is not null && File.Exists(options.StopFile))
                    {
                        Record(new Result(file, "", "STOPPED", "Stopped by user."));
                        return ValueTask.CompletedTask;
                    }
                    var relative = Path.GetRelativePath(sourceRoot, file);
                    var destinationSubfolder = (options.InPlace || options.OptimizeInPlace) && options.FilesFrom is not null
                        ? Path.GetDirectoryName(file)!
                        : options.FilesFrom is not null ? destination
                        : Path.Combine(destination, Path.GetDirectoryName(relative) ?? "");
                    if (options.OptimizeInPlace)
                    {
                        try
                        {
                            OptimizeOriginalFile(file, options, result => Record(result));
                        }
                        catch (Exception ex)
                        {
                            Record(new Result(file, "", "FAILED", ex.Message));
                        }
                        return ValueTask.CompletedTask;
                    }
                    // Include the original extension to avoid collisions such as image.jpg and image.tif.
                    var stem = Path.GetFileNameWithoutExtension(file);
                    var extensionTag = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
                    var outputStem = outputNames[file];
                    if (options.DryRun)
                    {
                        Record(new Result(file, Path.Combine(destinationSubfolder,
                            outputStem + (options.OutputFormat == "jpg" ? ".jpg" : ".png")), "DRY_RUN", ""));
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

                    if (Path.GetExtension(file).Equals(".dcm", StringComparison.OrdinalIgnoreCase)
                        || Path.GetExtension(file).Equals(".dicom", StringComparison.OrdinalIgnoreCase)
                        || (Path.GetExtension(file).Equals(".img", StringComparison.OrdinalIgnoreCase)
                            && ReadSignature(file) == "DICOM"))
                    {
                        // Confirm this is a valid DICOM dataset before attempting PNG rendering.
                        // ImageMagick performs display rendering; clinically accurate VOI handling
                        // and raw high-bit-depth pixel export still require separate validation.
                        var dataset = DicomFile.Open(file);
                        if (!dataset.Dataset.Contains(DicomTag.PixelData))
                            throw new InvalidDataException("DICOM file contains no PixelData tag.");
                    }

                    // Capture timestamps before decoding so reads do not alter last-access metadata.
                    var sourceCreatedUtc = File.GetCreationTimeUtc(file);
                    var sourceModifiedUtc = File.GetLastWriteTimeUtc(file);
                    var sourceAccessedUtc = File.GetLastAccessTimeUtc(file);

                    using var pages = new MagickImageCollection();
                    pages.Read(file, readSettings);
                    if (pages.Count == 0) throw new InvalidDataException("No image pages decoded.");

                    var staged = new List<(string Temporary, string Output)>();
                    var skipped = false;
                    try
                    {
                        for (var index = 0; index < pages.Count; index++)
                        {
                            var image = pages[index];
                            var pageSuffix = pages.Count > 1 ? $"_p{index + 1:0000}" : "";
                            var output = Path.Combine(destinationSubfolder, outputStem + pageSuffix + (options.OutputFormat == "jpg" ? ".jpg" : ".png"));
                            if (File.Exists(output) && !options.Overwrite)
                            {
                                Record(new Result(file, output, "SKIPPED", "Output exists (use --overwrite)."));
                                skipped = true;
                                continue;
                            }

                            if (options.AutoContrast) image.AutoLevel();
                            if (options.Contrast != 0)
                                image.BrightnessContrast(new Percentage(0), new Percentage(options.Contrast));
                            if (options.Sharpen > 0)
                                image.UnsharpMask(0, 1.0, options.Sharpen, 0.02);

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
                                image.Settings.SetDefine(MagickFormat.Png, "compression-level", Math.Clamp((int)Math.Round(options.CompressionEffort * 9.0 / 100), 0, 9).ToString(CultureInfo.InvariantCulture));
                                image.Settings.SetDefine(MagickFormat.Png, "compression-filter", "5");
                                if (options.OutputFormat == "jpg") image.Quality = 90;
                                image.Write(temporary, options.OutputFormat == "jpg" ? MagickFormat.Jpeg : MagickFormat.Png);
                                using (var check = new MagickImage())
                                {
                                    check.Read(temporary, options.OutputFormat == "jpg" ? MagickFormat.Jpeg : MagickFormat.Png);
                                    if (check.Width != image.Width || check.Height != image.Height)
                                        throw new InvalidDataException("Output PNG validation failed: dimensions mismatch.");
                                }
                                staged.Add((temporary, output));
                            }
                            catch
                            {
                                if (File.Exists(temporary)) File.Delete(temporary);
                                throw;
                            }
                        }
                        // All pages must be valid before committing any output.
                        // For in-place mode, any existing page means we skip the entire source,
                        // rather than deleting the source when only some pages were converted.
                        if (options.StopFile is not null && File.Exists(options.StopFile))
                        {
                            Record(new Result(file, "", "STOPPED", "Stopped before committing PNG output."));
                        }
                        else if (options.InPlace && skipped)
                        {
                            foreach (var item in staged) File.Delete(item.Temporary);
                            staged.Clear();
                        }
                        else
                        {
                            var backups = new List<(string Backup, string Original)>();
                            var committed = new List<string>();
                            try
                            {
                                // Back up all pre-existing outputs before the first replacement.
                                foreach (var item in staged)
                                {
                                    if (File.Exists(item.Output))
                                    {
                                        if (!options.Overwrite)
                                            throw new IOException("Destination appeared during conversion: " + item.Output);
                                        var backup = item.Output + "." + Guid.NewGuid().ToString("N") + ".bak";
                                        File.Move(item.Output, backup);
                                        backups.Add((backup, item.Output));
                                    }
                                }
                                foreach (var item in staged)
                                {
                                    File.Move(item.Temporary, item.Output);
                                    committed.Add(item.Output);
                                    // Apply source timestamps before deleting it in in-place mode.
                                    File.SetCreationTimeUtc(item.Output, sourceCreatedUtc);
                                    File.SetLastWriteTimeUtc(item.Output, sourceModifiedUtc);
                                    File.SetLastAccessTimeUtc(item.Output, sourceAccessedUtc);
                                }
                                if (options.InPlace)
                                    File.Delete(file);
                                // Backup cleanup must not trigger rollback after the source was deleted.
                                foreach (var backup in backups)
                                {
                                    try { File.Delete(backup.Backup); }
                                    catch (IOException cleanupError)
                                    { Console.Error.WriteLine("Backup cleanup warning: " + cleanupError.Message); }
                                }
                                foreach (var item in staged)
                                    Record(new Result(file, item.Output, "OK",
                                        new FileInfo(item.Output).Length + " bytes"));
                            }
                            catch
                            {
                                foreach (var path in committed)
                                    if (File.Exists(path)) File.Delete(path);
                                foreach (var backup in backups)
                                    if (File.Exists(backup.Backup)) File.Move(backup.Backup, backup.Original, true);
                                throw;
                            }
                        }
                    }
                    finally
                    {
                        foreach (var item in staged)
                            if (File.Exists(item.Temporary)) File.Delete(item.Temporary);
                    }
                }
                catch (Exception ex)
                {
                    Record(new Result(file, "", "FAILED", ex.GetType().Name + ": " + ex.Message));
                    Console.Error.WriteLine("FAILED " + file + ": " + ex.Message);
                }
                finally
                {
                    var done = Interlocked.Increment(ref total);
                    var status = fileResults.Any(r => r.Status == "FAILED") ? "FAILED" :
                        fileResults.Any(r => r.Status == "STOPPED") ? "STOPPED" :
                        fileResults.Any(r => r.Status == "SKIPPED") ? "SKIPPED" :
                        fileResults.Any(r => r.Status == "OK") ? "SUCCEEDED" : "DRY_RUN";
                    var detail = fileResults.FirstOrDefault(r => r.Status == "FAILED")?.Detail ?? "";
                    lock (progressLock)
                    {
                        Console.WriteLine("REAKILLER_EVENT:" + JsonSerializer.Serialize(new { source = file, status, detail, done, total = inputFiles.Length }));
                        DrawProgress(done, inputFiles.Length);
                    }
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

        Console.WriteLine();
        var failed = ordered.Count(r => r.Status == "FAILED");
        var stopped = ordered.Count(r => r.Status == "STOPPED");
        var succeeded = ordered.Count(r => r.Status == "OK");
        var skipped = ordered.Count(r => r.Status == "SKIPPED");
        Console.WriteLine($"Complete: {succeeded} outputs, {skipped} skipped, {failed} failed, {stopped} stopped.");
        return stopped > 0 ? 3 : failed > 0 ? 1 : 0;
    }

    private static void OptimizeOriginalFile(string file, Options options, Action<Result> record)
    {
        var ext = Path.GetExtension(file).ToLowerInvariant();
        // Conservative optimization: PNG only. JPEG re-encoding changes pixels;
        // DICOM and TIFF may contain medical data and metadata not safely round-trippable.
        if (ext != ".png")
        {
            record(new Result(file, "", "SKIPPED", "Safe in-place optimization currently supports PNG only; JPEG/DICOM/TIFF are left unchanged."));
            return;
        }
        var sourceSize = new FileInfo(file).Length;
        var temp = file + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using var image = new MagickImage(file);
            var width = image.Width;
            var height = image.Height;
            image.Settings.SetDefine(MagickFormat.Png, "compression-level", "9");
            image.Write(temp, MagickFormat.Png);
            using (var check = new MagickImage(temp))
            {
                if (check.Width != width || check.Height != height)
                    throw new InvalidDataException("Optimized output dimensions differ.");
            }
            if (new FileInfo(temp).Length >= sourceSize)
            {
                record(new Result(file, "", "SKIPPED", "Already smaller than optimized output."));
                return;
            }
            if (options.StopFile is not null && File.Exists(options.StopFile))
            {
                record(new Result(file, "", "STOPPED", "Stopped before replacement."));
                return;
            }
            // Atomic replacement with a backup; original filename and timestamps retained.
            var created = File.GetCreationTimeUtc(file);
            var modified = File.GetLastWriteTimeUtc(file);
            var accessed = File.GetLastAccessTimeUtc(file);
            File.Replace(temp, file, null);
            File.SetCreationTimeUtc(file, created);
            File.SetLastWriteTimeUtc(file, modified);
            File.SetLastAccessTimeUtc(file, accessed);
            record(new Result(file, file, "OK", "Lossless PNG optimization completed."));
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }

    private static async Task<int> RunInteractiveAsync()
    {
        Console.WriteLine("========================================");
        Console.WriteLine("       ReaKiller PNG Converter");
        Console.WriteLine("========================================");
        Console.WriteLine("Supported: IMG, DCM, DICOM, JPG, JPEG, TIF, TIFF");
        Console.WriteLine();

        static string Ask(string prompt)
        {
            Console.Write(prompt);
            return (Console.ReadLine() ?? "").Trim().Trim('"');
        }

        var source = Ask("Source file or folder: ");
        if (string.IsNullOrWhiteSpace(source)) return 2;
        var inPlace = Ask("Replace source files with PNGs in place? [y/N]: ")
            .Equals("y", StringComparison.OrdinalIgnoreCase);
        string? destination = null;
        if (!inPlace)
            destination = Ask("Output folder: ");
        var recursiveAnswer = Ask("Include subfolders? [Y/n]: ");
        var recursive = !recursiveAnswer.Equals("n", StringComparison.OrdinalIgnoreCase);
        var threadsText = Ask("Parallel workers [4]: ");
        var threads = int.TryParse(threadsText, out var selectedThreads) && selectedThreads > 0
            ? selectedThreads : 4;
        var widthText = Ask("Optional max width, blank to preserve resolution: ");
        var overwrite = Ask("Overwrite existing output PNGs? [y/N]: ")
            .Equals("y", StringComparison.OrdinalIgnoreCase);

        if (inPlace)
        {
            Console.WriteLine();
            Console.WriteLine("WARNING: Successfully converted source files will be DELETED.");
            Console.Write("Type REPLACE to confirm in-place conversion: ");
            if (Console.ReadLine()?.Trim() != "REPLACE")
            {
                Console.WriteLine("Cancelled, no changes made.");
                return 0;
            }
        }

        var cli = new List<string> { "convert", source };
        if (inPlace) cli.Add("--in-place");
        else { cli.Add("--output"); cli.Add(destination ?? ""); }
        if (recursive) cli.Add("--recursive");
        cli.Add("--threads"); cli.Add(threads.ToString(CultureInfo.InvariantCulture));
        if (int.TryParse(widthText, out var width) && width > 0)
        { cli.Add("--max-width"); cli.Add(width.ToString(CultureInfo.InvariantCulture)); }
        if (overwrite) cli.Add("--overwrite");
        Console.WriteLine();
        return await RunAsync(cli.ToArray());
    }

    private static void DrawProgress(int done, int total)
    {
        var ratio = total == 0 ? 1.0 : (double)done / total;
        const int barLength = 28;
        var filled = Math.Clamp((int)Math.Round(ratio * barLength), 0, barLength);
        var message = $"[{new string('#', filled)}{new string('-', barLength - filled)}] {ratio:P0} ({done}/{total})";
        if (Console.IsOutputRedirected) Console.WriteLine(message);
        else
        {
            Console.Write("\r" + message.PadRight(Math.Max(message.Length, 65)));
            if (done == total) Console.WriteLine();
        }
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
        bool recursive = false, overwrite = false, dryRun = false, inPlace = false;
        string compressionMode = "lossless";
        string outputFormat = "png";
        bool optimizeInPlace = false;
        string namePattern = "{name}_{ext}";
        string? stopFile = null;
        string? filesFrom = null;
        int? compressionEffort = null;
        int contrast = 0;
        double sharpen = 0;
        bool autoContrast = false;
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
                case "--in-place": inPlace = true; break;
                case "--optimize-in-place": optimizeInPlace = true; break;
                case "--format": outputFormat = Value().ToLowerInvariant(); break;
                case "--threads": threads = ParsePositive(Value(), flag); break;
                case "--auto-contrast": autoContrast = true; break;
                case "--name-pattern": namePattern = Value(); break;
                case "--stop-file": stopFile = Value(); break;
                case "--files-from": filesFrom = Value(); break;
                case "--compression-effort":
                    if (!int.TryParse(Value(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var effort) || effort < 0 || effort > 100)
                        throw new ArgumentException("--compression-effort must be 0 to 100.");
                    compressionEffort = effort;
                    break;
                case "--contrast":
                    if (!int.TryParse(Value(), NumberStyles.Integer, CultureInfo.InvariantCulture, out contrast) || contrast < -100 || contrast > 100)
                        throw new ArgumentException("--contrast must be between -100 and 100.");
                    break;
                case "--sharpen":
                    if (!double.TryParse(Value(), NumberStyles.Float, CultureInfo.InvariantCulture, out sharpen) || !double.IsFinite(sharpen) || sharpen < 0 || sharpen > 2)
                        throw new ArgumentException("--sharpen must be between 0 and 2.");
                    break;
                case "--max-width": maxWidth = ParsePositive(Value(), flag); break;
                case "--max-height": maxHeight = ParsePositive(Value(), flag); break;
                case "--compression":
                    var compression = Value().ToLowerInvariant();
                    if (compression is not ("lossless" or "max"))
                        throw new ArgumentException("--compression must be lossless or max.");
                    compressionMode = compression;
                    break;
                default: throw new ArgumentException("Unknown option: " + flag);
            }
        }

        if (!inPlace && !optimizeInPlace && string.IsNullOrWhiteSpace(output))
            throw new ArgumentException("Specify an output directory using --output, or select --in-place.");
        if ((inPlace || optimizeInPlace) && output is not null)
            throw new ArgumentException("--in-place and --output are mutually exclusive.");
        if (outputFormat is not ("png" or "jpg")) throw new ArgumentException("--format must be png or jpg.");
        if (optimizeInPlace && inPlace) throw new ArgumentException("Select either --optimize-in-place or --in-place.");
        if (optimizeInPlace && (maxWidth is not null || maxHeight is not null || contrast != 0 || sharpen != 0 || autoContrast)) throw new ArgumentException("Optimization mode cannot resize or enhance images.");
        ValidateNamePattern(namePattern);
        return new Options(source, output ?? "", recursive, threads,
            maxWidth, maxHeight, overwrite, dryRun, inPlace, compressionMode, compressionEffort ?? (compressionMode == "max" ? 100 : 65), contrast, sharpen, autoContrast, namePattern, stopFile, filesFrom, outputFormat, optimizeInPlace);
    }

    private static void ValidateNamePattern(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
            throw new ArgumentException("Naming pattern cannot be empty.");
        var removed = System.Text.RegularExpressions.Regex.Replace(
            pattern, @"\{(?:name|ext|index|date)\}", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (removed.Contains('{') || removed.Contains('}'))
            throw new ArgumentException("Unknown naming token. Supported: {name}, {ext}, {index}, {date}.");
        if (pattern.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || pattern.Contains('/') || pattern.Contains('\\'))
            throw new ArgumentException("Naming pattern contains invalid filename characters.");
    }

    private static string ResolveName(string pattern, string file, int index)
    {
        var name = Path.GetFileNameWithoutExtension(file);
        var ext = Path.GetExtension(file).TrimStart('.').ToLowerInvariant();
        var date = File.GetLastWriteTime(file).ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var output = System.Text.RegularExpressions.Regex.Replace(pattern, @"\{(name|ext|index|date)\}",
            match => match.Groups[1].Value.ToLowerInvariant() switch
            {
                "name" => name,
                "ext" => ext,
                "index" => index.ToString("D5", CultureInfo.InvariantCulture),
                "date" => date,
                _ => throw new InvalidOperationException("Invalid token")
            }, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (string.IsNullOrWhiteSpace(output) || output is "." or ".." ||
            output.EndsWith('.') || output.EndsWith(' ') ||
            output.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            output.Contains('/') || output.Contains('\\'))
            throw new ArgumentException("Resolved filename is invalid: " + output);
        return output;
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
              --format png|jpg        Output format (default png)\n              --optimize-in-place     Conservative same-format optimization (PNG only)\n              --in-place              Write converted image beside source, then delete converted source
              --recursive, -r         Traverse subdirectories
              --threads <n>          Parallel workers (default up to 4)
              --max-width <pixels>   Optional maximum output width
              --max-height <pixels>  Optional maximum output height
              --compression <mode>   lossless|max (presets, overridden by effort)
              --compression-effort <0..100> PNG lossless zlib effort; 100 is slowest
              --contrast <-100..100> Optional contrast adjustment, 0 is off
              --sharpen <0..2>       Optional unsharp-mask strength, 0 is off
              --auto-contrast        Stretch tonal range automatically
              --name-pattern <text>  Tokens: {name}, {ext}, {index}, {date}; extension .png is automatic
              --files-from <path>    Read explicitly selected image paths, one per line
              --stop-file <path>     Stop after current file when this marker exists
              --overwrite           Replace existing output files
              --dry-run             Preview input/output paths
              --help                Show help

            Outputs use the source extension as a suffix (e.g. image_img.png).
            TIFF and multi-frame DICOM pages get _p0001, _p0002, etc.
            By default, source files are preserved. --in-place deletes each source
            only after all of its PNG outputs have been written and validated.
            """);
    }
}
