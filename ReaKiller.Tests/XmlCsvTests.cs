using Transflux.Core;
using Xunit;

public sealed class XmlCsvTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "transflux-xml-" + Guid.NewGuid().ToString("N"));
    public XmlCsvTests() => Directory.CreateDirectory(root);
    public void Dispose() => Directory.Delete(root, true);
    private async Task<string> Convert(string xml, string? path = null, IReadOnlyList<string>? columns = null)
    {
        var source = Path.Combine(root, "input.xml");
        var output = Path.Combine(root, "output.csv");
        File.WriteAllText(source, xml);
        await new XmlCsvConverter(path, columns).ConvertAsync(source, output, CancellationToken.None);
        return File.ReadAllText(output).Replace("\r\n", "\n");
    }
    [Fact]
    public async Task FlattensAttributesNestedFieldsAndLateColumns()
    {
        var csv = await Convert("<Patients><Patient id='1'><Name>A,B</Name><Address><City>X</City></Address></Patient><Patient id='2'><Name>C</Name><Extra>Y</Extra></Patient></Patients>");
        Assert.Equal("\"@id\",\"Name\",\"Address.City\",\"Extra\"\n\"1\",\"A,B\",\"X\",\"\"\n\"2\",\"C\",\"\",\"Y\"\n", csv);
    }
    [Fact]
    public async Task SelectsNestedNamespacedRowsAndColumns()
    {
        var csv = await Convert("<r xmlns='urn:test'><group><item id='7'><name>A&quot;B</name></item></group></r>",
            "{urn:test}r/{urn:test}group/{urn:test}item", new[] { "@id", "{urn:test}name" });
        Assert.Equal("\"@id\",\"{urn:test}name\"\n\"7\",\"A\"\"B\"\n", csv);
    }
    [Fact]
    public async Task RejectsDtd()
    {
        await Assert.ThrowsAsync<System.Xml.XmlException>(() => Convert("<!DOCTYPE r [<!ENTITY x SYSTEM 'file:///secret'>]><r><item>&x;</item></r>"));
    }
    [Fact]
    public async Task RejectsMissingRows()
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => Convert("<r><item>1</item></r>", "r/missing"));
    }
    [Fact]
    public async Task KeepsRepeatedSiblingValues()
    {
        var csv = await Convert("<r><item><v>1</v><v>2</v></item></r>");
        Assert.Equal("\"v\",\"v[2]\"\n\"1\",\"2\"\n", csv);
    }
}
