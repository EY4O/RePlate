using System.IO.Compression;
using System.Text;
using System.Text.Json;
using RePlate.Core.Images;

namespace RePlate.Core.Plates;

/// <summary>A backup read back in: the plates and portraits, and the pictures that came with them.</summary>
public sealed record BackupContents(List<PlatePreset> Presets, Dictionary<Guid, byte[]> Pictures);

/// <summary>What adding a backup did.</summary>
public sealed record BackupImport(int Added, int AlreadyHere);

/// <summary>
/// One zip file with the library (plates.json) and its pictures (images/&lt;id&gt;.png), for moving to a new PC or to
/// another character. Reading one is strict, the same as the library file itself; a bad backup is refused whole.
/// </summary>
public static class LibraryBackup
{
    private const string LibraryEntry = "plates.json";
    private const int MaxEntries = 2000;
    private const int MaxJsonBytes = 16 * 1024 * 1024;

    public static async Task ExportAsync(Stream output, IReadOnlyList<PlatePreset> presets, ImageFiles files, CancellationToken token)
    {
        using var zip = new ZipArchive(output, ZipArchiveMode.Create, true);
        var library = zip.CreateEntry(LibraryEntry, CompressionLevel.Optimal);
        await using (var stream = library.Open())
            await stream.WriteAsync(Encoding.UTF8.GetBytes(PresetStore.Write(presets)), token).ConfigureAwait(false);

        foreach (var preset in presets)
        {
            var path = files.PathFor(preset.Id);
            if (!File.Exists(path)) continue;
            // PNGs are already compressed; storing them keeps the backup quick to make.
            var entry = zip.CreateEntry($"images/{preset.Id:N}.png", CompressionLevel.NoCompression);
            await using var stream = entry.Open();
            await stream.WriteAsync(await ImageFiles.ReadAsync(path, token).ConfigureAwait(false), token).ConfigureAwait(false);
        }
    }

    public static BackupContents Read(Stream input)
    {
        try
        {
            using var zip = new ZipArchive(input, ZipArchiveMode.Read, true);
            if (zip.Entries.Count > MaxEntries) throw new InvalidDataException("That backup has more in it than RePlate expects.");
            var library = zip.GetEntry(LibraryEntry) ?? throw new InvalidDataException("That isn't a RePlate backup.");
            var presets = PresetStore.Read(Encoding.UTF8.GetString(ReadEntry(library, MaxJsonBytes)));
            if (presets.Select(p => p.Id).Distinct().Count() != presets.Count) throw new InvalidDataException("That backup lists a plate twice.");

            var pictures = new Dictionary<Guid, byte[]>();
            foreach (var preset in presets)
            {
                if (zip.GetEntry($"images/{preset.Id:N}.png") is not { } entry) continue;
                var png = ReadEntry(entry, ImageFiles.MaxBytes);
                ImageFiles.ReadHeader(png);
                pictures[preset.Id] = png;
            }
            return new BackupContents(presets, pictures);
        }
        catch (JsonException)
        {
            throw new InvalidDataException("That backup's plates couldn't be read.");
        }
    }

    /// <summary>
    /// Adds a backup to this character. Anything already here is left alone; a plate that belongs to another character
    /// comes in as a copy with a new id, so both keep their own.
    /// </summary>
    public static async Task<BackupImport> AddToAsync(PresetStore store, ImageFiles files, BackupContents backup, ulong owner, CancellationToken token)
    {
        int added = 0, alreadyHere = 0;
        foreach (var preset in backup.Presets)
        {
            var id = preset.Id;
            if (store.Get(id) is { } existing)
            {
                if (existing.Owner == owner)
                {
                    alreadyHere++;
                    continue;
                }
                preset.Id = Guid.NewGuid();
            }
            preset.Owner = owner;
            if (backup.Pictures.TryGetValue(id, out var png)) await files.SaveAsync(preset.Id, png, token).ConfigureAwait(false);
            store.Add(preset);
            added++;
        }
        return new BackupImport(added, alreadyHere);
    }

    // Zip entries say how big they are, but that can't be trusted, so reading stops at the limit either way.
    private static byte[] ReadEntry(ZipArchiveEntry entry, int limit)
    {
        if (entry.Length > limit) throw new InvalidDataException("Something in that backup is too large.");
        using var stream = entry.Open();
        using var copy = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            if (copy.Length + read > limit) throw new InvalidDataException("Something in that backup is too large.");
            copy.Write(buffer, 0, read);
        }
        return copy.ToArray();
    }
}
