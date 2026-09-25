namespace RePlate.Core.Plates;

/// <summary>
/// HaselTweaks' portrait preset strings, so portraits can go back and forth between the two plugins. A string is 58
/// bytes in plain base64: "HTPS", version 1, then the portrait with its frame and accent, the camera, head and eyes as
/// half-floats the way the game keeps them. It has no name, body or plate design.
/// </summary>
public static class HaselTweaksCode
{
    private const int Magic = 0x53505448; // "HTPS" once written little-endian
    private const ushort Version = 1;
    private const int Length = 58;

    public static string Encode(PortraitSettings p)
    {
        using var bytes = new MemoryStream(Length);
        using (var writer = new BinaryWriter(bytes))
        {
            writer.Write(Magic);
            writer.Write(Version);
            WriteHalves(writer, p.CameraPosition);
            WriteHalves(writer, p.CameraTarget);
            writer.Write(p.ImageRotation);
            writer.Write(p.CameraZoom);
            writer.Write(p.Pose);
            writer.Write(p.AnimationProgress);
            writer.Write(p.Expression);
            WriteHalves(writer, p.HeadDirection);
            WriteHalves(writer, p.EyeDirection);
            writer.Write(p.DirectionalRed);
            writer.Write(p.DirectionalGreen);
            writer.Write(p.DirectionalBlue);
            writer.Write(p.DirectionalBrightness);
            writer.Write(p.DirectionalVerticalAngle);
            writer.Write(p.DirectionalHorizontalAngle);
            writer.Write(p.AmbientRed);
            writer.Write(p.AmbientGreen);
            writer.Write(p.AmbientBlue);
            writer.Write(p.AmbientBrightness);
            writer.Write(p.Background);
            writer.Write(p.Frame);
            writer.Write(p.Accent);
        }
        return Convert.ToBase64String(bytes.ToArray());
    }

    /// <summary>The portrait in a HaselTweaks string, or null when the text isn't exactly one.</summary>
    public static PortraitSettings? Decode(string text)
    {
        // Room for a little more than one string, so anything longer fails to fit and is refused.
        var data = new byte[Length + 6];
        if (!Convert.TryFromBase64String(text, data, out var written) || written != Length) return null;

        using var reader = new BinaryReader(new MemoryStream(data, 0, Length));
        if (reader.ReadInt32() != Magic || reader.ReadUInt16() != Version) return null;
        return new PortraitSettings
        {
            CameraPosition = ReadHalves(reader, 4),
            CameraTarget = ReadHalves(reader, 4),
            ImageRotation = reader.ReadInt16(),
            CameraZoom = reader.ReadByte(),
            Pose = reader.ReadUInt16(),
            AnimationProgress = reader.ReadSingle(),
            Expression = reader.ReadByte(),
            HeadDirection = ReadHalves(reader, 2),
            EyeDirection = ReadHalves(reader, 2),
            DirectionalRed = reader.ReadByte(),
            DirectionalGreen = reader.ReadByte(),
            DirectionalBlue = reader.ReadByte(),
            DirectionalBrightness = reader.ReadByte(),
            DirectionalVerticalAngle = reader.ReadInt16(),
            DirectionalHorizontalAngle = reader.ReadInt16(),
            AmbientRed = reader.ReadByte(),
            AmbientGreen = reader.ReadByte(),
            AmbientBlue = reader.ReadByte(),
            AmbientBrightness = reader.ReadByte(),
            Background = reader.ReadUInt16(),
            Frame = reader.ReadUInt16(),
            Accent = reader.ReadUInt16(),
        };
    }

    private static void WriteHalves(BinaryWriter writer, float[] values)
    {
        foreach (var value in values) writer.Write((Half)value);
    }

    private static float[] ReadHalves(BinaryReader reader, int count)
    {
        var values = new float[count];
        for (var i = 0; i < count; i++) values[i] = (float)reader.ReadHalf();
        return values;
    }
}
