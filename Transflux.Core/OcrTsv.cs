using System.Globalization;

namespace Transflux.Core;

public sealed record OcrWord(string Text, double Left, double Right, double Y, double Confidence);

public static class OcrTsv
{
    public static IReadOnlyList<OcrWord> Parse(TextReader reader, double minimumConfidence = 80)
    {
        if (!double.IsFinite(minimumConfidence) || minimumConfidence < 0 || minimumConfidence > 100)
            throw new ArgumentOutOfRangeException(nameof(minimumConfidence));
        if (reader.ReadLine() != "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext")
            throw new InvalidDataException("Invalid OCR output header.");
        var words = new List<OcrWord>();
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            if (line.Length > 65536) throw new InvalidDataException("OCR line too large.");
            var fields = line.Split('\t', 12);
            if (fields.Length != 12) throw new InvalidDataException("Invalid OCR row.");
            if (fields[0] != "5" || string.IsNullOrWhiteSpace(fields[11])) continue;
            double Number(int i) => double.Parse(fields[i], CultureInfo.InvariantCulture);
            var confidence = Number(10);
            var left = Number(6); var top = Number(7);
            var width = Number(8); var height = Number(9);
            if (!double.IsFinite(confidence) || confidence < minimumConfidence || confidence > 100)
                throw new InvalidDataException("OCR confidence below required threshold; manual review required.");
            if (new[] { left, top, width, height }.Any(x => !double.IsFinite(x) || x < 0 || x > 100000))
                throw new InvalidDataException("Invalid OCR coordinates.");
            if (words.Count >= 200000) throw new InvalidDataException("OCR word limit exceeded.");
            // 300 DPI pixels to PDF points; negate vertical position to sort top-to-bottom.
            words.Add(new OcrWord(fields[11], left * .24, (left + width) * .24,
                -(top + height) * .24, confidence));
        }
        if (words.Count == 0) throw new InvalidDataException("OCR produced no words.");
        return words;
    }
}
