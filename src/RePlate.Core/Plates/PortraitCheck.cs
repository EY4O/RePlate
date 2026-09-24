namespace RePlate.Core.Plates;

/// <summary>Compares what the editor shows with what was asked for.</summary>
public static class PortraitCheck
{
    // The game keeps vectors as half floats, and the pose keeps playing for a frame or so after a jump.
    private const float VectorTolerance = 0.01f;
    private const float AnimationTolerance = 1f;

    /// <summary>The parts that don't match, by name. Empty when everything took.</summary>
    public static List<string> Differences(PortraitSettings wanted, PortraitSettings actual)
    {
        var parts = new List<string>();
        if (wanted.Pose != actual.Pose) parts.Add("pose");
        else if (Math.Abs(wanted.AnimationProgress - actual.AnimationProgress) > AnimationTolerance) parts.Add("pose timing");
        if (wanted.Expression != actual.Expression) parts.Add("expression");
        // Only X, Y and Z place the camera. The fourth number is 0 on a saved plate and 1 in the open editor.
        if (!Close(wanted.CameraPosition[..3], actual.CameraPosition[..3]) || !Close(wanted.CameraTarget[..3], actual.CameraTarget[..3]) ||
            wanted.CameraZoom != actual.CameraZoom || wanted.ImageRotation != actual.ImageRotation)
            parts.Add("camera");
        if (!Close(wanted.HeadDirection, actual.HeadDirection)) parts.Add("head direction");
        if (!Close(wanted.EyeDirection, actual.EyeDirection)) parts.Add("eye direction");
        if (wanted.DirectionalRed != actual.DirectionalRed || wanted.DirectionalGreen != actual.DirectionalGreen ||
            wanted.DirectionalBlue != actual.DirectionalBlue || wanted.DirectionalBrightness != actual.DirectionalBrightness ||
            wanted.DirectionalVerticalAngle != actual.DirectionalVerticalAngle ||
            wanted.DirectionalHorizontalAngle != actual.DirectionalHorizontalAngle)
            parts.Add("directional light");
        if (wanted.AmbientRed != actual.AmbientRed || wanted.AmbientGreen != actual.AmbientGreen ||
            wanted.AmbientBlue != actual.AmbientBlue || wanted.AmbientBrightness != actual.AmbientBrightness)
            parts.Add("ambient light");
        if (wanted.Background != actual.Background) parts.Add("background");
        if (wanted.Frame != actual.Frame) parts.Add("frame");
        if (wanted.Accent != actual.Accent) parts.Add("accent");
        return parts;
    }

    private static bool Close(float[] a, float[] b) =>
        a.Length == b.Length && a.Zip(b).All(p => Math.Abs(p.First - p.Second) <= VectorTolerance);
}
