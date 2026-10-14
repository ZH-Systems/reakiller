using System.IO;
using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace ReaKiller.Desktop;

/// <summary>
/// Local PDF rewriting and lossless stream optimization.
/// Does not rasterize pages: selectable text and vector contents remain PDF objects.
/// </summary>
internal static class NativePdfOptimizer
{
    public static (long OriginalBytes, long OutputBytes, int Pages) Compress(
        string source, string temporaryOutput, string preset, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!File.Exists(source)) throw new FileNotFoundException("Input PDF missing.", source);
        long originalBytes = new FileInfo(source).Length;
        if (originalBytes < 8) throw new InvalidDataException("Source PDF is too short.");

        // PDFsharp 6.2.4 (MIT). Modify mode retains the existing page tree,
        // rather than rendering each page to a bitmap.
        using (var document = PdfReader.Open(source, PdfDocumentOpenMode.Modify))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (document.PageCount == 0) throw new InvalidDataException("PDF contains no pages.");
            document.Options.CompressContentStreams = true;
            document.Options.NoCompression = false;
            document.Options.FlateEncodeMode = PdfFlateEncodeMode.BestCompression;
            // The 3 preset names remain for UI compatibility. Native v1 intentionally
            // uses lossless stream compression for all three: no fake image-quality claims.
            document.Save(temporaryOutput);
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var verified = PdfReader.Open(temporaryOutput, PdfDocumentOpenMode.Import);
        using var original = PdfReader.Open(source, PdfDocumentOpenMode.Import);
        if (verified.PageCount != original.PageCount)
            throw new InvalidDataException("Page count changed during optimization.");
        return (originalBytes, new FileInfo(temporaryOutput).Length, verified.PageCount);
    }
}
