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
    public async Task ExplicitFileListConvertsOnlySelectedImages()
    {
        var root = NewFolder();
        try
        {
            var first = Path.Combine(root, "first.img");
            var second = Path.Combine(root, "second.jpg");
            var ignored = Path.Combine(root, "ignored.tif");
            WriteJpeg(first);
            WriteJpeg(second);
            WriteJpeg(ignored);
            var list = Path.Combine(root, "selection.txt");
            await File.WriteAllLinesAsync(list, [first, second]);
            var output = Path.Combine(root, "converted");
            var code = await ReaKiller.RunAsync(["convert", first, "--files-from", list,
                "--output", output, "--name-pattern", "{index}_{name}"]);
            Assert.Equal(0, code);
            Assert.True(File.Exists(Path.Combine(output, "00001_first.png")));
            Assert.True(File.Exists(Path.Combine(output, "00002_second.png")));
            Assert.False(File.Exists(Path.Combine(output, "00003_ignored.png")));
            Assert.True(File.Exists(first));
            Assert.True(File.Exists(second));
            Assert.True(File.Exists(ignored));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task CustomNamingPatternIncludesIndexAndOriginalName()
    {
        var root = NewFolder();
        try
        {
            var source = Path.Combine(root, "example.img");
            var output = Path.Combine(root, "converted");
            WriteJpeg(source);
            var result = await ReaKiller.RunAsync(
                ["convert", source, "--output", output,
                 "--name-pattern", "dental_{index}_{name}_{ext}"]);
            Assert.Equal(0, result);
            Assert.True(File.Exists(Path.Combine(output, "dental_00001_example_img.png")));
            Assert.True(File.Exists(source));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DuplicatePatternIsRejectedBeforeAnyOutputIsWritten()
    {
        var root = NewFolder();
        try
        {
            WriteJpeg(Path.Combine(root, "first.img"));
            WriteJpeg(Path.Combine(root, "second.jpg"));
            var output = Path.Combine(Path.GetTempPath(), "ReaKiller-test-output", Guid.NewGuid().ToString("N"));
            try
            {
                var result = await ReaKiller.RunAsync(
                    ["convert", root, "--output", output, "--name-pattern", "same"]);
                Assert.Equal(2, result);
                Assert.False(File.Exists(Path.Combine(output, "same.png")));
                Assert.True(File.Exists(Path.Combine(root, "first.img")));
                Assert.True(File.Exists(Path.Combine(root, "second.jpg")));
            }
            finally { if (Directory.Exists(output)) Directory.Delete(output, true); }
        }
        finally { Directory.Delete(root, true); }
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
    public async Task OptionalEnhancementsProducePngAndPreserveOriginal()
    {
        var root = NewFolder();
        try
        {
            var source = Path.Combine(root, "enhance.jpg");
            var outputFolder = Path.Combine(root, "converted");
            WriteJpeg(source);
            var result = await ReaKiller.RunAsync(
                ["convert", source, "--output", outputFolder,
                 "--contrast", "15", "--sharpen", "0.6", "--auto-contrast"]);
            Assert.Equal(0, result);
            Assert.True(File.Exists(source));
            using var resultImage = new MagickImage(Path.Combine(outputFolder, "enhance_jpg.png"));
            Assert.Equal(32u, resultImage.Width);
            Assert.Equal(24u, resultImage.Height);
        }
        finally { Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConvertedPngInheritsSourceTimestamps(bool inPlace)
    {
        var root = NewFolder();
        try
        {
            var source = Path.Combine(root, "dated.img");
            WriteJpeg(source);
            var created = new DateTime(2015, 4, 3, 12, 0, 0, DateTimeKind.Utc);
            var modified = new DateTime(2016, 5, 4, 13, 0, 0, DateTimeKind.Utc);
            var accessed = new DateTime(2017, 6, 5, 14, 0, 0, DateTimeKind.Utc);
            File.SetCreationTimeUtc(source, created);
            File.SetLastWriteTimeUtc(source, modified);
            File.SetLastAccessTimeUtc(source, accessed);
            var expectedCreated = File.GetCreationTimeUtc(source);
            var expectedModified = File.GetLastWriteTimeUtc(source);
            var expectedAccessed = File.GetLastAccessTimeUtc(source);
            var outputFolder = inPlace ? root : Path.Combine(root, "converted");
            var arguments = inPlace
                ? new[] { "convert", source, "--in-place" }
                : new[] { "convert", source, "--output", outputFolder };
            Assert.Equal(0, await ReaKiller.RunAsync(arguments));
            var output = Path.Combine(outputFolder, "dated_img.png");
            Assert.True(File.Exists(output));
            Assert.Equal(expectedCreated, File.GetCreationTimeUtc(output));
            Assert.Equal(expectedModified, File.GetLastWriteTimeUtc(output));
            Assert.Equal(expectedAccessed, File.GetLastAccessTimeUtc(output));
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
