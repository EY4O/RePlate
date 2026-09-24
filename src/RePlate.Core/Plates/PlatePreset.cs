namespace RePlate.Core.Plates;

/// <summary>
/// Everything the portrait editor saves: camera, pose, expression, head and eyes, lighting, background, frame and
/// accent. Vectors are X,Y,Z,W for the camera and X,Y for head and eyes.
/// </summary>
public sealed record PortraitSettings
{
    public required float[] CameraPosition { get; init; }
    public required float[] CameraTarget { get; init; }
    public required short ImageRotation { get; init; }
    public required byte CameraZoom { get; init; }
    public required ushort Pose { get; init; }
    public required float AnimationProgress { get; init; }
    public required byte Expression { get; init; }
    public required float[] HeadDirection { get; init; }
    public required float[] EyeDirection { get; init; }
    public required byte DirectionalRed { get; init; }
    public required byte DirectionalGreen { get; init; }
    public required byte DirectionalBlue { get; init; }
    public required byte DirectionalBrightness { get; init; }
    public required short DirectionalVerticalAngle { get; init; }
    public required short DirectionalHorizontalAngle { get; init; }
    public required byte AmbientRed { get; init; }
    public required byte AmbientGreen { get; init; }
    public required byte AmbientBlue { get; init; }
    public required byte AmbientBrightness { get; init; }
    public required ushort Background { get; init; }
    public required ushort Frame { get; init; }
    public required ushort Accent { get; init; }

    public bool IsValid() =>
        Finite(CameraPosition, 4) && Finite(CameraTarget, 4) && Finite(HeadDirection, 2) && Finite(EyeDirection, 2) &&
        float.IsFinite(AnimationProgress);

    private static bool Finite(float[]? values, int length) =>
        values != null && values.Length == length && values.All(float.IsFinite);
}

/// <summary>The plate itself: base plate, borders, decorations and which side the portrait sits on.</summary>
public sealed record PlateDesign
{
    public required ushort BasePlate { get; init; }
    public required byte TopBorder { get; init; }
    public required byte BottomBorder { get; init; }

    /// <summary>Decoration ids as the game stores them, zeros included. Their kind comes from the decoration sheet.</summary>
    public required ushort[] Decorations { get; init; }

    public required bool InvertPortraitPlacement { get; init; }
}

public sealed class PlatePreset
{
    public const int MaxNameLength = 64;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The character the plate belongs to (content id).</summary>
    public ulong Owner { get; set; }

    public uint Race { get; set; }
    public uint Tribe { get; set; }
    public byte Sex { get; set; }

    public PortraitSettings? Portrait { get; set; }
    public PlateDesign? Design { get; set; }

    public static string CleanName(string name)
    {
        var trimmed = new string(name.Select(c => char.IsControl(c) ? ' ' : c).ToArray()).Trim();
        return trimmed.Length > MaxNameLength ? trimmed[..MaxNameLength].TrimEnd() : trimmed;
    }
}
