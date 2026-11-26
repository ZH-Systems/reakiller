using System.Text;
using UglyToad.PdfPig;

namespace Transflux.Core;

public sealed record PdfCsvOptions(int FirstPage = 1, int? LastPage = null,
    double RowTolerance = 3, double ColumnGap = 12,
    bool RemoveRepeatedHeaders = true, IReadOnlyList<double>? ColumnBoundaries = null,
    IReadOnlyList<string>? Headers = null);

public sealed class PdfCsvConverter(PdfCsvOptions? options = null, Func<string, int, double, double, IReadOnlyList<OcrWord>>? ocr = null) : IFileConverter
{
    private readonly PdfCsvOptions settings = options ?? new();
    public ConversionOperation Operation => ConversionOperation.PdfToCsv;
    public IReadOnlyCollection<string> SourceExtensions => new[] { ".pdf" };
    public string OutputExtension => ".csv";
    private sealed record PositionedWord(string Text, double Left, double Right, double Y);

    private IEnumerable<(int Page, string[] Cells)> ReadRows(string source, CancellationToken token)
    {
        if (!double.IsFinite(settings.RowTolerance) || settings.RowTolerance <= 0 ||
            !double.IsFinite(settings.ColumnGap) || settings.ColumnGap <= 0)
            throw new ArgumentException("Row tolerance and column gap must be positive.");
        var boundaries = settings.ColumnBoundaries?.ToArray();
        if (boundaries is not null && (boundaries.Any(x => !double.IsFinite(x) || x <= 0) ||
            !boundaries.SequenceEqual(boundaries.Distinct().OrderBy(x => x))))
            throw new ArgumentException("Column boundaries must be positive, distinct and ascending.");
        if (new FileInfo(source).Length > 256L * 1024 * 1024) throw new InvalidDataException("PDF exceeds input limit.");
        using var document = PdfDocument.Open(source);
        if (document.NumberOfPages > 5000) throw new InvalidDataException("PDF exceeds page limit.");
        var last = settings.LastPage ?? document.NumberOfPages;
        if (settings.FirstPage < 1 || last < settings.FirstPage || last > document.NumberOfPages)
            throw new ArgumentException("Invalid PDF page range.");
        for (var number = settings.FirstPage; number <= last; number++)
        {
            token.ThrowIfCancellationRequested();
            var page = document.GetPage(number);
            var words = page.GetWords()
                .Where(w => !string.IsNullOrWhiteSpace(w.Text))
                .Select(w => new PositionedWord(w.Text, (double)w.BoundingBox.Left,
                    (double)w.BoundingBox.Right, (double)w.BoundingBox.Bottom))
                .Take(200001).OrderByDescending(w => w.Y).ThenBy(w => w.Left).ToArray();
            if (words.Length > 200000) throw new InvalidDataException("Page exceeds word limit.");
            if (words.Length == 0 && ocr is not null)
                words = ocr(source, number, (double)page.Width, (double)page.Height)
                    .Select(w => new PositionedWord(w.Text, w.Left, w.Right, w.Y))
                    .OrderByDescending(w => w.Y).ThenBy(w => w.Left).ToArray();
            if (words.Length == 0)
                throw new InvalidDataException($"Page {number} has no extractable text. OCR is required.");
            var index = 0;
            while (index < words.Length)
            {
                token.ThrowIfCancellationRequested();
                var y = words[index].Y;
                var row = new List<PositionedWord>();
                do { row.Add(words[index++]); }
                while (index < words.Length && y - words[index].Y <= settings.RowTolerance);
                row.Sort((a,b) => a.Left.CompareTo(b.Left));
                if (boundaries is not null)
                {
                    var cells = new List<string>[boundaries.Length + 1];
                    for (var i = 0; i < cells.Length; i++) cells[i] = new();
                    foreach (var word in row)
                    {
                        var column = 0;
                        while (column < boundaries.Length && word.Left >= boundaries[column]) column++;
                        cells[column].Add(word.Text);
                    }
                    yield return (number, cells.Select(c => string.Join(" ", c)).ToArray());
                }
                else
                {
                    var cells = new List<string>();
                    var cell = new List<string>();
                    var right = double.NegativeInfinity;
                    foreach (var word in row)
                    {
                        if (cell.Count > 0 && word.Left - right >= settings.ColumnGap)
                        { cells.Add(string.Join(" ", cell)); cell.Clear(); }
                        cell.Add(word.Text);
                        right = Math.Max(right, word.Right);
                    }
                    if (cell.Count > 0) cells.Add(string.Join(" ", cell));
                    yield return (number, cells.ToArray());
                }
            }
        }
    }

    private IEnumerable<string[]> Table(string source, CancellationToken token)
    {
        string[]? first = null;
        int? previousPage = null;
        foreach (var (page, cells) in ReadRows(source, token))
        {
            if (first is null)
            {
                if (cells.Length < 2) throw new InvalidDataException("First row is not a multi-column table. Select pages or explicit column boundaries.");
                first = cells;
                if (settings.Headers is not null && settings.Headers.Count != first.Length)
                    throw new ArgumentException("Header count does not match detected columns.");
                yield return settings.Headers?.ToArray() ?? cells;
            }
            else
            {
                if (cells.Length != first.Length)
                    throw new InvalidDataException($"Ambiguous column count on page {page}. Use explicit column boundaries or adjust extraction settings.");
                if (!(settings.RemoveRepeatedHeaders && previousPage != page && cells.SequenceEqual(first)))
                    yield return cells;
            }
            previousPage = page;
        }
        if (first is null) throw new InvalidDataException("No table rows found.");
    }

    public IReadOnlyList<string[]> Preview(string source, int maxRows = 20, CancellationToken token = default)
    {
        if (maxRows < 1 || maxRows > 1000) throw new ArgumentOutOfRangeException(nameof(maxRows));
        return Table(source, token).Take(maxRows).ToArray();
    }

    public async Task ConvertAsync(string source, string temporaryOutput, CancellationToken cancellationToken)
    {
        await using var writer = new StreamWriter(temporaryOutput, false, new UTF8Encoding(false));
        foreach (var row in Table(source, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var line = string.Join(",", row.Select(value => "\"" + value.Replace("\"", "\"\"") + "\""));
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken);
        }
    }
}
