using RePlate.Core.Plates;
using Xunit;

namespace RePlate.Core.Tests;

public class PresetTests : IDisposable
{
    private readonly string path = Path.Combine(Path.GetTempPath(), $"replate-{Guid.NewGuid():N}.json");

    public void Dispose()
    {
        if (File.Exists(path)) File.Delete(path);
    }

    internal static PortraitSettings Portrait(ushort pose = 50) => new()
    {
        CameraPosition = [-0.84375f, 0.26757812f, 1.7480469f, 0],
        CameraTarget = [0.0836792f, -0.57714844f, 0.19421387f, 0],
        ImageRotation = 0,
        CameraZoom = 200,
        Pose = pose,
        AnimationProgress = 165.6f,
        Expression = 0,
        HeadDirection = [0, 0],
        EyeDirection = [0.3474121f, 0.08673096f],
        DirectionalRed = 212,
        DirectionalGreen = 141,
        DirectionalBlue = 255,
        DirectionalBrightness = 105,
        DirectionalVerticalAngle = 133,
        DirectionalHorizontalAngle = 133,
        AmbientRed = 51,
        AmbientGreen = 51,
        AmbientBlue = 51,
        AmbientBrightness = 168,
        Background = 158,
        Frame = 3,
        Accent = 105,
    };

    private static PlatePreset Preset(ulong owner, string name, DateTimeOffset updated) => new()
    {
        Name = name,
        Owner = owner,
        CreatedAt = updated,
        UpdatedAt = updated,
        Portrait = Portrait(),
        Design = new PlateDesign { BasePlate = 111, TopBorder = 0, BottomBorder = 3, Decorations = [262, 4, 21, 0, 265], InvertPortraitPlacement = true },
    };

    [Fact]
    public void NamesAreTrimmedAndShortened()
    {
        Assert.Equal("My plate", PlatePreset.CleanName("  My\tplate \n"));
        Assert.Equal(PlatePreset.MaxNameLength, PlatePreset.CleanName(new string('a', 200)).Length);
        Assert.Equal("", PlatePreset.CleanName("   "));
    }

    [Fact]
    public void PortraitsWithBrokenNumbersAreInvalid()
    {
        Assert.True(Portrait().IsValid());
        Assert.False((Portrait() with { CameraPosition = [0, 0, 0] }).IsValid());
        Assert.False((Portrait() with { EyeDirection = [float.NaN, 0] }).IsValid());
        Assert.False((Portrait() with { AnimationProgress = float.PositiveInfinity }).IsValid());
    }

    [Fact]
    public void PresetsSurviveASaveAndReload()
    {
        var store = new PresetStore(path);
        var original = Preset(1, "Raid night", DateTimeOffset.UnixEpoch);
        store.Add(original);
        store.Save();

        var loaded = Assert.Single(new PresetStore(path).For(1));
        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal("Raid night", loaded.Name);
        Assert.Equal(original.Portrait!.EyeDirection, loaded.Portrait!.EyeDirection);
        Assert.Equal(158, loaded.Portrait.Background);
        Assert.Equal([262, 4, 21, 0, 265], loaded.Design!.Decorations);
        Assert.True(loaded.Design.InvertPortraitPlacement);
    }

    [Fact]
    public void EachCharacterSeesItsOwnPlatesNewestFirst()
    {
        var store = new PresetStore(path);
        store.Add(Preset(1, "Old", DateTimeOffset.UnixEpoch));
        store.Add(Preset(1, "New", DateTimeOffset.UnixEpoch.AddDays(1)));
        store.Add(Preset(2, "Other", DateTimeOffset.UnixEpoch));

        Assert.Equal(["New", "Old"], store.For(1).Select(p => p.Name));
        Assert.Equal("Other", Assert.Single(store.For(2)).Name);
        Assert.True(store.Remove(store.For(1)[0].Id));
        Assert.Equal("Old", Assert.Single(store.For(1)).Name);
    }

    [Fact]
    public void AnUnreadableFileIsLeftAlone()
    {
        File.WriteAllText(path, "{ broken");
        var store = new PresetStore(path);
        store.Add(Preset(1, "New", DateTimeOffset.UnixEpoch));
        store.Save();

        Assert.False(store.CanWrite);
        Assert.NotNull(store.Error);
        Assert.Equal("{ broken", File.ReadAllText(path));
    }
}
