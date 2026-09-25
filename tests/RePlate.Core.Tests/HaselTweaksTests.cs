using System.Buffers.Binary;
using System.Text.Json;
using RePlate.Core.Images;
using RePlate.Core.Plates;
using Xunit;

namespace RePlate.Core.Tests;

public class HaselTweaksTests : IDisposable
{
    private readonly string folder = Path.Combine(Path.GetTempPath(), $"replate-hasel-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, true);
    }

    // A HaselTweaks string put together by hand in its field order, to check against rather than our own writer.
    private static string ByHand(int magic = 0x53505448, ushort version = 1, Half cameraX = default)
    {
        var bytes = new byte[58];
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span, magic);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], version);
        BinaryPrimitives.WriteHalfLittleEndian(span[6..], cameraX == default ? (Half)(-0.84375f) : cameraX);
        BinaryPrimitives.WriteHalfLittleEndian(span[8..], (Half)0.26757812f);
        BinaryPrimitives.WriteHalfLittleEndian(span[10..], (Half)1.7480469f);
        BinaryPrimitives.WriteHalfLittleEndian(span[14..], (Half)0.0836792f);
        BinaryPrimitives.WriteHalfLittleEndian(span[16..], (Half)(-0.57714844f));
        BinaryPrimitives.WriteHalfLittleEndian(span[18..], (Half)0.19421387f);
        bytes[24] = 200;                                                  // zoom
        BinaryPrimitives.WriteUInt16LittleEndian(span[25..], 50);         // pose
        BinaryPrimitives.WriteSingleLittleEndian(span[27..], 165.6f);     // animation
        BinaryPrimitives.WriteHalfLittleEndian(span[36..], (Half)0.3474121f);
        BinaryPrimitives.WriteHalfLittleEndian(span[38..], (Half)0.08673096f);
        new byte[] { 212, 141, 255, 105 }.CopyTo(span[40..]);             // directional light
        BinaryPrimitives.WriteInt16LittleEndian(span[44..], 133);
        BinaryPrimitives.WriteInt16LittleEndian(span[46..], 133);
        new byte[] { 51, 51, 51, 168 }.CopyTo(span[48..]);                // ambient light
        BinaryPrimitives.WriteUInt16LittleEndian(span[52..], 158);        // background
        BinaryPrimitives.WriteUInt16LittleEndian(span[54..], 3);          // frame
        BinaryPrimitives.WriteUInt16LittleEndian(span[56..], 105);        // accent
        return Convert.ToBase64String(bytes);
    }

    [Fact]
    public void OurStringIsTheSameAsOneMadeByHand()
    {
        var code = HaselTweaksCode.Encode(PresetTests.Portrait());

        Assert.Equal(ByHand(), code);
        Assert.Equal(80, code.Length);
        Assert.StartsWith("SFRQUw", code);
    }

    [Fact]
    public void AHaselTweaksStringComesInAsAPortrait()
    {
        var shared = ShareCode.Decode(" " + ByHand() + "\n", out var problem);

        Assert.Equal("", problem);
        Assert.NotNull(shared);
        Assert.Equal(PresetKind.Portrait, shared.Kind);
        Assert.Null(shared.Design);
        Assert.Empty(PortraitCheck.Differences(PresetTests.Portrait(), shared.Portrait!));
        Assert.Equal((158, 3, 105), (shared.Portrait!.Background, shared.Portrait.Frame, shared.Portrait.Accent));
        Assert.Equal("Shared portrait", ShareCode.ToPreset(shared, 1).Name);
    }

    [Fact]
    public void StringsThatArentQuiteRightAreRefused()
    {
        Assert.Null(HaselTweaksCode.Decode(ByHand(magic: 0x12345678)));
        Assert.Null(HaselTweaksCode.Decode(ByHand(version: 2)));
        Assert.Null(HaselTweaksCode.Decode(ByHand()[..76]));
        Assert.Null(HaselTweaksCode.Decode(ByHand() + "AAAA"));
        Assert.Null(ShareCode.Decode(ByHand(cameraX: Half.NaN), out var nan));
        Assert.Null(ShareCode.Decode(ByHand(cameraX: (Half)60000), out var far));
        Assert.NotEqual("", nan);
        Assert.NotEqual("", far);
    }

    private static string Settings(params object[] presets) => JsonSerializer.Serialize(new
    {
        Version = 9,
        Tweaks = new { PortraitHelper = new { Presets = presets, PresetTags = Array.Empty<object>() } },
    });

    [Fact]
    public void SavedPortraitsAreReadWithTheirNames()
    {
        var first = Guid.NewGuid();
        var json = Settings(
            new { Id = first, Name = "Victory", Preset = ByHand(), Tags = Array.Empty<Guid>() },
            new { Id = Guid.NewGuid(), Name = "", Preset = HaselTweaksCode.Encode(PresetTests.Portrait(pose: 60)), Tags = Array.Empty<Guid>() },
            new { Id = Guid.NewGuid(), Name = "Broken", Preset = "not a preset", Tags = Array.Empty<Guid>() },
            new { Id = Guid.NewGuid(), Name = "Empty", Preset = (string?)null, Tags = Array.Empty<Guid>() },
            new { Id = first, Name = "Twice", Preset = ByHand(), Tags = Array.Empty<Guid>() });

        var found = HaselTweaksLibrary.Parse(json);

        Assert.Equal(3, found.Unreadable);
        Assert.Equal(["Victory", "HaselTweaks portrait"], found.Contents.Presets.Select(p => p.Name));
        Assert.Equal(first, found.Contents.Presets[0].Id);
        Assert.All(found.Contents.Presets, p => Assert.True(p is { Kind: PresetKind.Portrait, Imported: true, Design: null }));
    }

    [Fact]
    public void SettingsWithoutPortraitsHaveNothingToBringOver()
    {
        Assert.Empty(HaselTweaksLibrary.Parse("""{ "Version": 9, "Tweaks": {} }""").Contents.Presets);
        Assert.Throws<InvalidDataException>(() => HaselTweaksLibrary.Parse("not json"));
    }

    [Fact]
    public void PicturesComeFromHaselTweaksPortraitsFolder()
    {
        var id = Guid.NewGuid();
        Directory.CreateDirectory(Path.Combine(folder, "HaselTweaks", "Portraits"));
        File.WriteAllText(Path.Combine(folder, "HaselTweaks.json"), Settings(new { Id = id, Name = "Victory", Preset = ByHand(), Tags = Array.Empty<Guid>() }));
        var png = BackupTests.Png(30);
        File.WriteAllBytes(Path.Combine(folder, "HaselTweaks", "Portraits", id.ToString("D") + ".png"), png);

        var found = HaselTweaksLibrary.Read(folder);

        Assert.Equal(png, found.Contents.Pictures[id]);
        Assert.Throws<InvalidDataException>(() => HaselTweaksLibrary.Read(Path.Combine(folder, "nowhere")));
    }

    [Fact]
    public void BringingThemOverTwiceAddsNothingNew()
    {
        var store = new PresetStore(Path.Combine(folder, "plates.json"));
        var files = new ImageFiles(Path.Combine(folder, "replate"));
        var id = Guid.NewGuid();
        var json = Settings(
            new { Id = id, Name = "Victory", Preset = ByHand(), Tags = Array.Empty<Guid>() },
            new { Id = Guid.NewGuid(), Name = "Casual", Preset = HaselTweaksCode.Encode(PresetTests.Portrait(pose: 60)), Tags = Array.Empty<Guid>() });
        var first = HaselTweaksLibrary.Parse(json).Contents with { Pictures = new() { [id] = BackupTests.Png(30) } };

        Assert.Equal(new BackupImport(2, 0), HaselTweaksLibrary.AddTo(store, files, first, 7));
        Assert.Equal(new BackupImport(0, 2), HaselTweaksLibrary.AddTo(store, files, HaselTweaksLibrary.Parse(json).Contents, 7));
        Assert.Equal(new BackupImport(2, 0), HaselTweaksLibrary.AddTo(store, files, HaselTweaksLibrary.Parse(json).Contents, 8));

        var mine = store.For(7, PresetKind.Portrait);
        Assert.Equal(2, mine.Count);
        Assert.DoesNotContain(mine, p => p.Id == id);
        Assert.True(File.Exists(files.PathFor(mine.Single(p => p.Name == "Victory").Id)));
    }
}
