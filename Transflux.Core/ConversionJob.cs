namespace Transflux.Core;

public enum ConversionOperation { Image, PdfCompression, XmlToCsv, PdfToCsv }
public enum ExistingOutputPolicy { Skip, Overwrite, Fail }
public enum ConversionStatus { Succeeded, Skipped, Failed }
public sealed record ConversionJob(ConversionOperation Operation, string Source, string Destination,
    bool Recursive = true, ExistingOutputPolicy ExistingOutput = ExistingOutputPolicy.Skip,
    bool PreserveTimestamps = true, int Workers = 1);
public sealed record ConversionItem(string Source, string Destination);
public sealed record ConversionResult(ConversionItem Item, ConversionStatus Status, string? Error = null);
public sealed record ConversionProgress(int Completed, int Total, ConversionResult Result);

// Implementations write only to the supplied temporary output and close it before returning.
public interface IFileConverter
{
    ConversionOperation Operation { get; }
    IReadOnlyCollection<string> SourceExtensions { get; }
    string OutputExtension { get; }
    Task ConvertAsync(string source, string temporaryOutput, CancellationToken cancellationToken);
}
