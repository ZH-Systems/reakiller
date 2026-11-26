using Transflux.Core;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

public sealed class PdfCsvTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "transflux-pdf-" + Guid.NewGuid().ToString("N"));
    public PdfCsvTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);

    private string Fixture(bool blank = false, bool missing = false)
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        for (int i = 0; i < 2; i++)
        {
            var page = builder.AddPage(PageSize.A4);
            if (blank && i == 1) continue;
            page.AddText("ID", 12, new PdfPoint(30, 700), font);
            page.AddText("Name", 12, new PdfPoint(200, 700), font);
            page.AddText((i + 1).ToString(), 12, new PdfPoint(30, 680), font);
            if (!missing || i != 1)
                page.AddText(i == 0 ? "Smith, John" : "Jane Doe", 12, new PdfPoint(200, 680), font);
        }
        var source = Path.Combine(root, "input.pdf");
        File.WriteAllBytes(source, builder.Build());
        return source;
    }

    [Fact]
    public async Task ExtractsMultiplePagesAndQuotesCommas()
    {
        var output = Path.Combine(root, "output.csv");
        await new PdfCsvConverter().ConvertAsync(Fixture(), output, CancellationToken.None);
        Assert.Equal(new[] { "\"ID\",\"Name\"", "\"1\",\"Smith, John\"", "\"2\",\"Jane Doe\"" }, File.ReadAllLines(output));
    }

    [Fact]
    public void SelectsPagesAndRenamesHeaders()
    {
        var result = new PdfCsvConverter(new PdfCsvOptions(FirstPage: 2, LastPage: 2,
            Headers: new[] { "PatientId", "FullName" })).Preview(Fixture());
        Assert.Equal(2, result.Count);
        Assert.Equal(new[] { "PatientId", "FullName" }, result[0]);
        Assert.Equal(new[] { "2", "Jane Doe" }, result[1]);
    }

    [Fact]
    public void ExplicitBoundariesPreserveEmptyCells()
    {
        var result = new PdfCsvConverter(new PdfCsvOptions(ColumnBoundaries: new[] { 150d })).Preview(Fixture(missing: true));
        Assert.Equal(new[] { "2", "" }, result.Last());
    }

    [Fact]
    public async Task BlankPageFailsWithoutPublishingPartialCsv()
    {
        var source = Fixture(blank: true);
        var destination = Path.Combine(root, "csv");
        var result = Assert.Single(await ConversionRunner.RunAsync(
            new ConversionJob(ConversionOperation.PdfToCsv, source, destination), new PdfCsvConverter()));
        Assert.Equal(ConversionStatus.Failed, result.Status);
        Assert.Contains("OCR", result.Error!);
        Assert.False(File.Exists(Path.Combine(destination, "input.csv")));
    }

    [Fact]
    public void AmbiguousRowsFailRatherThanShiftColumns()
    {
        Assert.Throws<InvalidDataException>(() => new PdfCsvConverter().Preview(Fixture(missing: true)));
    }
}
