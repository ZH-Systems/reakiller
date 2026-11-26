using System.Globalization;
using System.Text.Json;
using Transflux.Core;

internal static class PdfCsvCommand
{
    public static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 1 || args.Contains("--help") || args.Contains("-h"))
        {
            Console.WriteLine("pdf-to-csv <file-or-folder> --output <folder> [--first-page N] [--last-page N] [--row-tolerance N] [--column-gap N] [--boundary X] [--header NAME] [--keep-headers] [--recursive] [--overwrite] [--threads N] [--preview] [--ocr-config <json>]");
            return args.Length == 1 ? 2 : 0;
        }
        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler handler = (_, e) => { e.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += handler;
        try
        {
            string? output = null, ocrConfig = null;
            int first = 1, workers = 1;
            int? last = null;
            double tolerance = 3, gap = 12;
            bool recursive = false, overwrite = false, repeated = true, preview = false;
            var boundaries = new List<double>();
            var headers = new List<string>();
            for (int i = 2; i < args.Length; i++)
            {
                string Value() => ++i < args.Length ? args[i] : throw new ArgumentException("Missing option value.");
                int Integer() => int.Parse(Value(), CultureInfo.InvariantCulture);
                double Number() => double.Parse(Value(), CultureInfo.InvariantCulture);
                switch (args[i])
                {
                    case "--ocr-config": ocrConfig = Value(); break;
                    case "--output": output = Value(); break;
                    case "--first-page": first = Integer(); break;
                    case "--last-page": last = Integer(); break;
                    case "--row-tolerance": tolerance = Number(); break;
                    case "--column-gap": gap = Number(); break;
                    case "--boundary": boundaries.Add(Number()); break;
                    case "--header": headers.Add(Value()); break;
                    case "--keep-headers": repeated = false; break;
                    case "--recursive": recursive = true; break;
                    case "--overwrite": overwrite = true; break;
                    case "--threads": workers = Integer(); break;
                    case "--preview": preview = true; break;
                    default: throw new ArgumentException("Unknown option: " + args[i]);
                }
            }
            if (workers < 1 || workers > 4) throw new ArgumentException("PDF workers must be between 1 and 4.");
            OcrConfiguration? ocr = null;
            if (ocrConfig is not null)
            {
                if (new FileInfo(ocrConfig).Length > 65536) throw new InvalidDataException("OCR configuration too large.");
                ocr = JsonSerializer.Deserialize<OcrConfiguration>(File.ReadAllText(ocrConfig)) ?? throw new InvalidDataException("Invalid OCR configuration.");
                if (workers != 1) throw new ArgumentException("OCR requires one worker.");
            }
            var converter = new IsolatedPdfConverter(new PdfCsvOptions(first, last, tolerance, gap, repeated,
                boundaries.Count == 0 ? null : boundaries, headers.Count == 0 ? null : headers), ocr);
            if (preview)
            {
                if (!File.Exists(args[1])) throw new ArgumentException("Preview requires a single PDF.");
                Console.WriteLine(JsonSerializer.Serialize(await converter.PreviewAsync(args[1], cancellation.Token)));
                return 0;
            }
            if (string.IsNullOrWhiteSpace(output)) throw new ArgumentException("--output is required.");
            var job = new ConversionJob(ConversionOperation.PdfToCsv, args[1], output, recursive,
                overwrite ? ExistingOutputPolicy.Overwrite : ExistingOutputPolicy.Skip, Workers: workers);
            var results = await ConversionRunner.RunAsync(job, converter, cancellationToken: cancellation.Token);
            foreach (var result in results)
                Console.WriteLine($"{result.Status}: {result.Item.Source} -> {result.Item.Destination} {result.Error}");
            return results.Any(r => r.Status == ConversionStatus.Failed) ? 1 : 0;
        }
        catch (OperationCanceledException) { return 3; }
        catch (Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        finally { Console.CancelKeyPress -= handler; }
    }
}
