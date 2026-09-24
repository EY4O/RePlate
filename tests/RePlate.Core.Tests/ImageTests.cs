using System.Buffers.Binary;
using System.Numerics;
using RePlate.Core.Images;
using Xunit;

namespace RePlate.Core.Tests;

public class ImageTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"replate-images-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    // Enough of a PNG for the header check: signature, IHDR length and type, width, height.
    private static byte[] Png(uint width, uint height)
    {
        var bytes = new byte[40];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), height);
        return bytes;
    }

    [Fact]
    public void HeaderGivesTheSizeAndRejectsOthers()
    {
        Assert.Equal((740, 420), ImageFiles.ReadHeader(Png(740, 420)));
        Assert.Throws<InvalidDataException>(() => ImageFiles.ReadHeader(Png(5000, 10)));
        Assert.Throws<InvalidDataException>(() => ImageFiles.ReadHeader(Png(0, 10)));
        Assert.Throws<InvalidDataException>(() => ImageFiles.ReadHeader("GIF89a not a png at all, padded out"u8));
    }

    [Fact]
    public async Task PicturesAreSavedReplacedAndDeleted()
    {
        var files = new ImageFiles(folder);
        var id = Guid.NewGuid();
        await files.SaveAsync(id, Png(10, 10), TestContext.Current.CancellationToken);
        await files.SaveAsync(id, Png(20, 20), TestContext.Current.CancellationToken);

        var saved = await ImageFiles.ReadAsync(files.PathFor(id), TestContext.Current.CancellationToken);
        Assert.Equal((20, 20), ImageFiles.ReadHeader(saved));
        Assert.EndsWith(Path.Combine("images", id.ToString("N") + ".png"), files.PathFor(id));

        files.Delete(id);
        Assert.False(File.Exists(files.PathFor(id)));
    }

    [Fact]
    public void EncodingBufferFailsWithAnIoErrorWhenFull()
    {
        using var buffer = ImageFiles.CreateEncodingBuffer();
        buffer.Write(new byte[ImageFiles.MaxBytes]);
        Assert.Throws<IOException>(() => buffer.WriteByte(1));
        Assert.Throws<IOException>(() => buffer.SetLength(ImageFiles.MaxBytes + 1L));
    }

    [Fact]
    public void DragsBecomeWholePixelsInEitherDirection()
    {
        var forward = ImageCrop.FromSelection(new(0.1f, 0.2f), new(0.5f, 0.6f), 1000, 500);
        var backward = ImageCrop.FromSelection(new(0.5f, 0.6f), new(0.1f, 0.2f), 1000, 500);

        Assert.Equal(new ImageCrop(100, 100, 400, 200), forward);
        Assert.Equal(forward, backward);
        Assert.Equal(new ImageCrop(0, 0, 1000, 500), ImageCrop.FromSelection(new(-1, -1), new(2, 2), 1000, 500));
        Assert.Null(ImageCrop.FromSelection(new(0.3f, 0.3f), new(0.3f, 0.8f), 1000, 500));
        Assert.Null(ImageCrop.FromSelection(new(float.NaN, 0), new(1, 1), 1000, 500));
    }

    [Fact]
    public void PlateWindowIsSuggestedOnlyWhenItStayedPutAndFits()
    {
        Vector2 position = new(100.4f, 50.2f), size = new(740, 420), screen = new(2560, 1440);

        Assert.Equal(new ImageCrop(100, 50, 741, 421), ImageCrop.FromWindow(position, size, size, position, screen, 2560, 1440));
        Assert.Null(ImageCrop.FromWindow(position, size, size, position + Vector2.One, screen, 2560, 1440)); // moved
        Assert.Null(ImageCrop.FromWindow(position, size, null, null, screen, 2560, 1440));                  // closed
        Assert.Null(ImageCrop.FromWindow(position, size, size, position, screen, 1280, 720));               // scaled capture
        Assert.Null(ImageCrop.FromWindow(new(2000, 50), size, size, new(2000, 50), screen, 2560, 1440));    // off screen
    }
}
