using System.Buffers.Binary;

namespace RePlate.Core.Images;

/// <summary>A plate's reference picture, stored as images/&lt;id&gt;.png next to the library.</summary>
public sealed class ImageFiles(string directory)
{
    public const int MaxBytes = 8 * 1024 * 1024;
    public const int MaxDimension = 4096;

    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public string PathFor(Guid id) => Path.Combine(directory, "images", id.ToString("N") + ".png");

    /// <summary>Checks the PNG signature and size before anything decodes it.</summary>
    public static (int Width, int Height) ReadHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 33 || bytes.Length > MaxBytes || !bytes[..8].SequenceEqual(Signature) ||
            BinaryPrimitives.ReadUInt32BigEndian(bytes[8..12]) != 13 || !bytes[12..16].SequenceEqual("IHDR"u8))
            throw new InvalidDataException("Choose a PNG image no larger than 8 MB.");
        var width = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
        var height = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
        if (width == 0 || height == 0 || width > MaxDimension || height > MaxDimension)
            throw new InvalidDataException("The image must be at most 4096 pixels on each side.");
        return ((int)width, (int)height);
    }

    public static async Task<byte[]> ReadAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
        if (stream.Length > MaxBytes) throw new InvalidDataException("Choose a PNG image no larger than 8 MB.");
        var bytes = new byte[stream.Length];
        await stream.ReadExactlyAsync(bytes, token).ConfigureAwait(false);
        ReadHeader(bytes);
        return bytes;
    }

    /// <summary>Writes the picture for a plate, replacing any earlier one.</summary>
    public async Task SaveAsync(Guid id, byte[] png, CancellationToken token)
    {
        ReadHeader(png);
        var path = PathFor(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".tmp";
        await File.WriteAllBytesAsync(temporary, png, token).ConfigureAwait(false);
        File.Move(temporary, path, true);
    }

    /// <summary>The same, all at once, for adding a backup.</summary>
    public void Save(Guid id, byte[] png)
    {
        ReadHeader(png);
        var path = PathFor(id);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path + ".tmp", png);
        File.Move(path + ".tmp", path, true);
    }

    public void Delete(Guid id)
    {
        var path = PathFor(id);
        if (File.Exists(path)) File.Delete(path);
    }

    /// <summary>
    /// A fixed-size buffer for PNG encoding. Dalamud's encoder only handles IOException from the stream, so running
    /// out of room has to throw that rather than MemoryStream's usual NotSupportedException.
    /// </summary>
    public static MemoryStream CreateEncodingBuffer()
    {
        var stream = new EncodingBuffer();
        stream.SetLength(0);
        return stream;
    }

    private sealed class EncodingBuffer() : MemoryStream(new byte[MaxBytes], writable: true)
    {
        private void Check(long count)
        {
            if (Position + count > MaxBytes) throw new IOException("The picture is larger than 8 MB.");
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            Check(count);
            base.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            Check(buffer.Length);
            base.Write(buffer);
        }

        public override void WriteByte(byte value)
        {
            Check(1);
            base.WriteByte(value);
        }

        public override void SetLength(long value)
        {
            if (value is < 0 or > MaxBytes) throw new IOException("The picture is larger than 8 MB.");
            base.SetLength(value);
        }
    }
}
