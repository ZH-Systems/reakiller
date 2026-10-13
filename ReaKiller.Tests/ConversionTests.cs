using ImageMagick;
using Xunit;

public sealed class ConversionTests
{
    private static string NewFolder()
    {
        var folder = Path.Combine(Path.GetTempPath(), "ReaKiller-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        return folder;
    }

    private static void WriteJpeg(string file)
    {
        using var image = new MagickImage(MagickColors.White, 32, 24);
        image.Write(file, MagickFormat.Jpeg);
    }

    [Fact]
    public async Task ImgJpegToPngPreservesSourceByDefault()
    {
        var root = NewFolder();
        try
        {
            var source = Path.Combine(root, "x.img");
            var destination = Path.Combine(root, "output");
            WriteJpeg(source);
            var code = await ReaKiller.RunAsync(["convert", source, "--output", destination, "--compression", "max"]);
            Assert.Equal(0, code);
            Assert.True(File.Exists(source));
            using var png = new MagickImage(Path.Combine(destination, "x_img.png"));
            Assert.Equal(32u, png.Width);
            Assert.Equal(24u, png.Height);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task InPlaceConversionRemovesOnlyConvertedOriginal()
    {
        var root = NewFolder();
        try
        {
            var source = Path.Combine(root, "x.img");
            WriteJpeg(source);
            var code = await ReaKiller.RunAsync(["convert", source, "--in-place"]);
            Assert.Equal(0, code);
            Assert.False(File.Exists(source));
            Assert.True(File.Exists(Path.Combine(root, "x_img.png")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task ExistingOutputWithoutOverwriteKeepsOriginal()
    {
        var root = NewFolder();
        try
        {
            var source = Path.Combine(root, "x.img");
            var output = Path.Combine(root, "x_img.png");
            WriteJpeg(source);
            File.WriteAllText(output, "DO NOT REPLACE");
            var code = await ReaKiller.RunAsync(["convert", source, "--in-place"]);
            Assert.Equal(0, code);
            Assert.True(File.Exists(source));
            Assert.Equal("DO NOT REPLACE", File.ReadAllText(output));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CorruptSourceDoesNotDeleteInput()
    {
        var root = NewFolder();
        try
        {
            var source = Path.Combine(root, "x.img");
            await File.WriteAllBytesAsync(source, [1, 2, 3, 4]);
            var code = await ReaKiller.RunAsync(["convert", source, "--in-place"]);
            Assert.Equal(1, code);
            Assert.True(File.Exists(source));
            Assert.False(File.Exists(Path.Combine(root, "x_img.png")));
        }
        finally { Directory.Delete(root, true); }
    }
}
