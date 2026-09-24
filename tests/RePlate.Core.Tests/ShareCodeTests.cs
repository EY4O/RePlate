using RePlate.Core.Plates;
using Xunit;

namespace RePlate.Core.Tests;

public class ShareCodeTests
{
    private static PlatePreset Plate() => new()
    {
        Name = "Raid night",
        Owner = 18014498568585221,
        Race = 5,
        Tribe = 10,
        Sex = 1,
        Portrait = PresetTests.Portrait(),
        Design = new PlateDesign { BasePlate = 111, TopBorder = 0, BottomBorder = 3, Decorations = [262, 4, 21, 0, 265], InvertPortraitPlacement = true },
    };

    [Fact]
    public void APlateSurvivesTheTrip()
    {
        var code = ShareCode.Encode(Plate());
        var shared = ShareCode.Decode(code, out var problem);

        Assert.StartsWith(ShareCode.Prefix, code);
        Assert.True(code.Length < 1000, $"code is {code.Length} characters");
        Assert.Equal("", problem);
        Assert.NotNull(shared);
        Assert.Equal("Raid night", shared.Name);
        Assert.Equal((5u, 10u, (byte)1), (shared.Race, shared.Tribe, shared.Sex));
        Assert.Empty(PortraitCheck.Differences(Plate().Portrait!, shared.Portrait!));
        Assert.Equal([262, 4, 21, 0, 265], shared.Design!.Decorations);
        Assert.True(shared.Design.InvertPortraitPlacement);
    }

    [Fact]
    public void TheCodeSaysNothingAboutWhoSharedIt()
    {
        var shared = ShareCode.Decode(ShareCode.Encode(Plate()), out _)!;
        var preset = ShareCode.ToPreset(shared, 42);

        Assert.Equal(42ul, preset.Owner);
        Assert.True(preset.Imported);
        Assert.NotEqual(Plate().Id, preset.Id);
    }

    [Fact]
    public void SpacesAndLineBreaksFromChatAreIgnored()
    {
        var code = ShareCode.Encode(Plate());
        var broken = code[..20] + "\n  " + code[20..40] + " " + code[40..];
        Assert.NotNull(ShareCode.Decode(broken, out _));
    }

    [Theory]
    [InlineData("hello there")]
    [InlineData("RePlate1:")]
    [InlineData("RePlate1:not!base64")]
    [InlineData("RePlate1:AAAA")]
    public void AnythingElseIsRefusedWithAReason(string code)
    {
        Assert.Null(ShareCode.Decode(code, out var problem));
        Assert.NotEqual("", problem);
    }

    [Fact]
    public void ImpossibleValuesAreRefused()
    {
        var plate = Plate();
        plate.Portrait = plate.Portrait! with { CameraPosition = [1e6f, 0, 0, 0] };
        Assert.Null(ShareCode.Decode(ShareCode.Encode(plate), out _));

        plate = Plate();
        plate.Design = plate.Design! with { Decorations = new ushort[40] };
        Assert.Null(ShareCode.Decode(ShareCode.Encode(plate), out _));

        Assert.Null(ShareCode.Decode(ShareCode.Prefix + new string('A', 5000), out var problem));
        Assert.Contains("too long", problem);
    }
}
