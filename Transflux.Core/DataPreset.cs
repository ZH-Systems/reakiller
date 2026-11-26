namespace Transflux.Core;

public sealed record DataPreset(int Version, bool Pdf, bool Recursive, int Workers,
    string RecordPath, string XmlColumns, string FirstPage, string LastPage, string Boundaries,
    string PdfHeaders, bool KeepHeaders)
{
    public void Validate()
    {
        if (Version != 1 || Workers < 1 || Workers > (Pdf ? 4 : 16))
            throw new InvalidDataException("Invalid preset version or worker count.");
        foreach (var value in new[] { RecordPath, XmlColumns, FirstPage, LastPage, Boundaries, PdfHeaders })
            if (value is null || value.Length > 16384 || value.Contains('\0'))
                throw new InvalidDataException("Invalid preset settings.");
        if (Pdf)
        {
            if (!int.TryParse(FirstPage, out var first) || first < 1 || first > 5000 ||
                (!string.IsNullOrWhiteSpace(LastPage) && (!int.TryParse(LastPage, out var last) || last < first || last > 5000)))
                throw new InvalidDataException("Invalid preset page range.");
            var boundaries = Boundaries.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            double previous = 0;
            foreach (var boundary in boundaries)
            {
                if (!double.TryParse(boundary, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var value) ||
                    !double.IsFinite(value) || value <= previous) throw new InvalidDataException("Invalid preset boundaries.");
                previous = value;
            }
            if (boundaries.Length > 511) throw new InvalidDataException("Too many preset boundaries.");
        }
    }
}
