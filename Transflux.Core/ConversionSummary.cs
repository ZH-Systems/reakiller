namespace Transflux.Core;

public sealed record ConversionSummary(int Succeeded, int Skipped, int Failed)
{
    public int Total => checked(Succeeded + Skipped + Failed);
    public static ConversionSummary From(IEnumerable<ConversionResult> results)
    {
        int succeeded = 0, skipped = 0, failed = 0;
        foreach (var result in results)
        {
            switch (result.Status)
            {
                case ConversionStatus.Succeeded: succeeded++; break;
                case ConversionStatus.Skipped: skipped++; break;
                case ConversionStatus.Failed: failed++; break;
                default: throw new InvalidDataException("Unknown conversion status.");
            }
        }
        return new(succeeded, skipped, failed);
    }
    public void Validate()
    {
        if (Succeeded < 0 || Skipped < 0 || Failed < 0 || Total > 250000)
            throw new InvalidDataException("Invalid conversion summary.");
    }
}
