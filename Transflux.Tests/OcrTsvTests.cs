using Transflux.Core;
using Xunit;

public sealed class OcrTsvTests
{
    private const string Header = "level\tpage_num\tblock_num\tpar_num\tline_num\tword_num\tleft\ttop\twidth\theight\tconf\ttext\n";
    [Fact]
    public void MapsWordCoordinates()
    {
        using var reader = new StringReader(Header + "5\t1\t1\t1\t1\t1\t100\t200\t50\t25\t95\tExample\n");
        var word = Assert.Single(OcrTsv.Parse(reader));
        Assert.Equal("Example", word.Text);
        Assert.Equal(24d, word.Left, 5);
        Assert.Equal(36d, word.Right, 5);
        Assert.Equal(-54d, word.Y, 5);
        Assert.Equal(95d, word.Confidence);
    }
    [Theory]
    [InlineData("79")]
    [InlineData("NaN")]
    [InlineData("101")]
    public void RejectsInvalidOrLowConfidence(string confidence)
    {
        using var reader = new StringReader(Header + "5\t1\t1\t1\t1\t1\t100\t200\t50\t25\t" + confidence + "\tExample\n");
        Assert.Throws<InvalidDataException>(() => OcrTsv.Parse(reader));
    }
    [Fact]
    public void RejectsEmptyOrMalformedOutput()
    {
        Assert.Throws<InvalidDataException>(() => OcrTsv.Parse(new StringReader(Header)));
        Assert.Throws<InvalidDataException>(() => OcrTsv.Parse(new StringReader("invalid")));
    }
}
