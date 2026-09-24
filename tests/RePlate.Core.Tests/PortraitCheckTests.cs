using RePlate.Core.Plates;
using Xunit;

namespace RePlate.Core.Tests;

public class PortraitCheckTests
{
    [Fact]
    public void AMatchingPortraitHasNoDifferences()
    {
        var portrait = PresetTests.Portrait();
        Assert.Empty(PortraitCheck.Differences(portrait, portrait with { }));
    }

    [Fact]
    public void HalfFloatRoundingAndASmallPoseDriftAreAllowed()
    {
        var wanted = PresetTests.Portrait();
        var actual = wanted with
        {
            CameraPosition = wanted.CameraPosition.Select(v => (float)(Half)v).ToArray(),
            EyeDirection = [0.3474f, 0.0867f],
            AnimationProgress = 166.1f,
        };
        Assert.Empty(PortraitCheck.Differences(wanted, actual));
    }

    [Fact]
    public void TheCamerasFourthNumberIsIgnored()
    {
        var saved = PresetTests.Portrait();
        var editor = saved with
        {
            CameraPosition = [.. saved.CameraPosition[..3], 1],
            CameraTarget = [.. saved.CameraTarget[..3], 1],
        };
        Assert.Empty(PortraitCheck.Differences(saved, editor));
        Assert.Equal(["camera"], PortraitCheck.Differences(saved, editor with { CameraPosition = [0, 0, 0, 1] }));
    }

    [Fact]
    public void EachChangedPartIsNamed()
    {
        var wanted = PresetTests.Portrait();
        var actual = wanted with
        {
            Pose = 2,
            CameraZoom = 150,
            AmbientBrightness = 10,
            HeadDirection = [0.5f, 0],
            Frame = 9,
        };
        Assert.Equal(["pose", "camera", "head direction", "ambient light", "frame"], PortraitCheck.Differences(wanted, actual));
    }

    [Fact]
    public void PoseTimingIsOnlyCheckedForTheSamePose()
    {
        var wanted = PresetTests.Portrait();
        Assert.Equal(["pose timing"], PortraitCheck.Differences(wanted, wanted with { AnimationProgress = 10 }));
        Assert.Equal(["pose"], PortraitCheck.Differences(wanted, wanted with { Pose = 3, AnimationProgress = 10 }));
    }
}
