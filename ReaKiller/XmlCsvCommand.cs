using Transflux.Core;

internal static class XmlCsvCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("xml-to-csv <file-or-folder> --output <folder> [--record-path <path>] [--column <name>] [--recursive] [--overwrite] [--threads N] [--preview] [--summary-json]");
            return args.Length == 1 ? 2 : 0;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            string? output = null, recordPath = null;
            var columns = new List<string>();
            bool recursive = false, overwrite = false, preview = false, summary = false;
            int workers = 1;
            for (int i = 2; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
                switch (args[i])
                {
                    case "--preview": preview = true; break;
                    case "--summary-json": summary = true; break;
                    case "--output": output = Value(); break;
                    case "--record-path": recordPath = Value(); break;
                    case "--column": columns.Add(Value()); break;
                    case "--recursive": recursive = true; break;
                    case "--overwrite": overwrite = true; break;
                    case "--threads": workers = int.Parse(Value(), System.Globalization.CultureInfo.InvariantCulture); break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            if (preview)
            {
                if (!File.Exists(args[1])) throw new ArgumentException("Preview requires a single XML file.");
                var data = new XmlCsvConverter(recordPath, columns.Count == 0 ? null : columns).Preview(args[1], cancellation.Token);
                var json = System.Text.Json.JsonSerializer.Serialize(data);
                if (json.Length > 4 * 1024 * 1024) throw new InvalidDataException("Preview exceeds size limit.");
                Console.WriteLine(json);
                return 0;
            }
            if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("--output is required.");
            var job = new ConversionJob(ConversionOperation.XmlToCsv, args[1], output, recursive,
                overwrite ? ExistingOutputPolicy.Overwrite : ExistingOutputPolicy.Skip, Workers: workers);
            var results = await ConversionRunner.RunAsync(job, new XmlCsvConverter(recordPath, columns.Count == 0 ? null : columns),
                cancellationToken: cancellation.Token);
            if (summary) Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(ConversionSummary.From(results)));
            else foreach (var result in results)
                Console.WriteLine($"{result.Status}: {result.Item.Source} -> {result.Item.Destination} {result.Error}");
            return results.Any(r => r.Status == ConversionStatus.Failed) ? 1 : 0;
        }
        catch (OperationCanceledException) { return 3; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        finally { Console.CancelKeyPress -= handler; }
    }
}
