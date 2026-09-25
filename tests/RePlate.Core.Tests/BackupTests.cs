using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using RePlate.Core.Images;
using RePlate.Core.Plates;
using Xunit;

namespace RePlate.Core.Tests;

public class BackupTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"replate-backup-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    private static PlatePreset Plate(ulong owner, string name, PresetKind kind = PresetKind.Plate) => new()
    {
        Name = name,
        Owner = owner,
        Kind = kind,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
        Portrait = PresetTests.Portrait(),
        Design = kind == PresetKind.Plate
            ? new PlateDesign { BasePlate = 111, TopBorder = 0, BottomBorder = 3, Decorations = [262, 4], InvertPortraitPlacement = false }
            : null,
        ClassJob = kind == PresetKind.Portrait ? (byte)19 : (byte)0,
    };

    private static byte[] Png(uint size)
    {
        var bytes = new byte[40];
        new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), size);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), size);
        return bytes;
    }

    private async Task<MemoryStream> Export(IReadOnlyList<PlatePreset> presets, ImageFiles files)
    {
        var zip = new MemoryStream();
        await LibraryBackup.ExportAsync(zip, presets, files, TestContext.Current.CancellationToken);
        zip.Position = 0;
        return zip;
    }

    [Fact]
    public async Task ABackupComesBackWithItsPictures()
    {
        var files = new ImageFiles(Path.Combine(folder, "from"));
        var plate = Plate(1, "Raid night");
        var portrait = Plate(1, "Victory", PresetKind.Portrait);
        await files.SaveAsync(plate.Id, Png(20), TestContext.Current.CancellationToken);

        var backup = LibraryBackup.Read(await Export([plate, portrait], files));

        Assert.Equal(["Raid night", "Victory"], backup.Presets.Select(p => p.Name));
        Assert.Equal(PresetKind.Portrait, backup.Presets[1].Kind);
        Assert.Equal((byte)19, backup.Presets[1].ClassJob);
        Assert.Equal(Png(20), backup.Pictures[plate.Id]);
        Assert.False(backup.Pictures.ContainsKey(portrait.Id));
    }

    [Fact]
    public async Task ImportingGivesEverythingToThisCharacter()
    {
        var files = new ImageFiles(Path.Combine(folder, "to"));
        var store = new PresetStore(Path.Combine(folder, "plates.json"));
        var mine = Plate(2, "Already mine");
        var theirs = Plate(1, "From the main");
        var mains = Plate(1, "Main's own");
        store.Add(mine);
        store.Add(mains);
        var sameIdAsMain = Plate(1, "Same id as the main's");
        sameIdAsMain.Id = mains.Id;
        var backup = new BackupContents([mine, theirs, sameIdAsMain], new() { [theirs.Id] = Png(10) });

        var result = await LibraryBackup.AddToAsync(store, files, backup, 2, TestContext.Current.CancellationToken);

        Assert.Equal(new BackupImport(2, 1), result);
        var alt = store.For(2);
        Assert.Equal(3, alt.Count);
        Assert.All(alt, p => Assert.Equal(2ul, p.Owner));
        Assert.Equal(1ul, store.Get(mains.Id)!.Owner);
        Assert.NotEqual(mains.Id, alt.Single(p => p.Name == "Same id as the main's").Id);
        Assert.True(File.Exists(files.PathFor(theirs.Id)));
    }

    [Fact]
    public void ThingsThatArentBackupsAreRefused()
    {
        Assert.ThrowsAny<InvalidDataException>(() => LibraryBackup.Read(new MemoryStream(Encoding.UTF8.GetBytes("not a zip"))));

        var empty = new MemoryStream();
        using (new ZipArchive(empty, ZipArchiveMode.Create, true)) { }
        empty.Position = 0;
        Assert.Throws<InvalidDataException>(() => LibraryBackup.Read(empty));

        var broken = new MemoryStream();
        using (var zip = new ZipArchive(broken, ZipArchiveMode.Create, true))
        using (var stream = zip.CreateEntry("plates.json").Open())
            stream.Write("""{ "Version": 1, "Presets": [ { "Owner": 0 } ] }"""u8);
        broken.Position = 0;
        Assert.Throws<InvalidDataException>(() => LibraryBackup.Read(broken));
    }

    [Fact]
    public async Task APictureThatIsntAPngSpoilsTheBackup()
    {
        var plate = Plate(1, "Raid night");
        var zip = await Export([plate], new ImageFiles(folder));
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Update, true))
        using (var stream = archive.CreateEntry($"images/{plate.Id:N}.png").Open())
            stream.Write("GIF89a pretending"u8);
        zip.Position = 0;

        Assert.Throws<InvalidDataException>(() => LibraryBackup.Read(zip));
    }

    [Fact]
    public void PlatesAndPortraitsAreListedApart()
    {
        var store = new PresetStore(Path.Combine(folder, "plates.json"));
        store.Add(Plate(1, "Plate"));
        store.Add(Plate(1, "Portrait", PresetKind.Portrait));

        Assert.Equal("Plate", Assert.Single(store.For(1)).Name);
        Assert.Equal("Portrait", Assert.Single(store.For(1, PresetKind.Portrait)).Name);
        Assert.Equal(2, store.All(1).Count);
    }

    [Fact]
    public void ASharedPortraitStaysAPortrait()
    {
        var shared = ShareCode.Decode(ShareCode.Encode(Plate(1, "Victory", PresetKind.Portrait)), out _)!;
        var preset = ShareCode.ToPreset(shared, 5);

        Assert.Equal(PresetKind.Portrait, preset.Kind);
        Assert.Equal((byte)19, preset.ClassJob);
        Assert.Null(preset.Design);
    }
}
