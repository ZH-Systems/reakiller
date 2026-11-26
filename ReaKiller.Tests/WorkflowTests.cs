using Transflux.Core;
using Xunit;

public sealed class WorkflowTests
{
    [Fact]
    public void XmlPreviewDiscoversLateColumnsWithoutRetainingAllRows()
    {
        var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(source, "<root>" + string.Concat(Enumerable.Range(0, 25)
                .Select(i => $"<record><Id>{i}</Id>" + (i == 24 ? "<Late>x</Late>" : "") + "</record>")) + "</root>");
            var rows = new XmlCsvConverter().Preview(source);
            Assert.Equal(20, rows.Count);
            Assert.Equal(new[] { "Id", "Late" }, rows[0]);
            Assert.Equal(new[] { "0", "" }, rows[1]);
            Assert.Equal(new[] { "18", "" }, rows[19]);
        }
        finally { File.Delete(source); }
    }

    [Fact]
    public void XmlPreviewRejectsDtd()
    {
        var source = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            File.WriteAllText(source, "<!DOCTYPE root [<!ENTITY x 'test'>]><root><record>&x;</record></root>");
            Assert.Throws<System.Xml.XmlException>(() => new XmlCsvConverter().Preview(source));
        }
        finally { File.Delete(source); }
    }

    [Fact]
    public void SummaryContainsCountsNotPaths()
    {
        var item = new ConversionItem("private.xml", "private.csv");
        var summary = ConversionSummary.From(new[] { new ConversionResult(item, ConversionStatus.Succeeded),
            new ConversionResult(item, ConversionStatus.Failed, "private detail"), new ConversionResult(item, ConversionStatus.Skipped) });
        Assert.Equal(new ConversionSummary(1, 1, 1), summary);
        Assert.Equal(3, summary.Total);
        Assert.DoesNotContain("private", System.Text.Json.JsonSerializer.Serialize(summary));
    }

    [Fact]
    public void PresetRejectsUnsafeOrInvalidSettings()
    {
        var preset = new DataPreset(1, true, true, 1, "", "", "1", "3", "150,300", "", false);
        preset.Validate();
        Assert.Throws<InvalidDataException>(() => (preset with { Workers = 5 }).Validate());
        Assert.Throws<InvalidDataException>(() => (preset with { Boundaries = "300,150" }).Validate());
        Assert.Throws<InvalidDataException>(() => (preset with { LastPage = "0" }).Validate());
        Assert.Throws<InvalidDataException>(() => (preset with { Version = 2 }).Validate());
    }
}
