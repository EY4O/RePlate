using System.Text.Json;

namespace RePlate.Core.Plates;

/// <summary>Reads library.json from earlier RePlate builds, which saved portraits only.</summary>
public static class LegacyLibrary
{
    public static IReadOnlyList<PlatePreset> Read(string json, out int skipped)
    {
        skipped = 0;
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;
        if (!root.TryGetProperty("Version", out var version) || version.GetInt32() is not (1 or 2) ||
            !root.TryGetProperty("Portraits", out var entries) || entries.ValueKind != JsonValueKind.Array)
            throw new JsonException("Not a RePlate library from an earlier version.");

        var presets = new List<PlatePreset>();
        foreach (var entry in entries.EnumerateArray())
        {
            try
            {
                presets.Add(ToPreset(entry));
            }
            catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
            {
                skipped++;
            }
        }
        return presets;
    }

    private static PlatePreset ToPreset(JsonElement entry)
    {
        var capture = entry.GetProperty("Capture");
        var fromEditor = capture.TryGetProperty("EditorPortrait", out var editor) && editor.ValueKind == JsonValueKind.Object;
        var portrait = fromEditor ? editor : capture.GetProperty("Portrait");
        var saved = entry.GetProperty("SavedAtUtc").GetDateTimeOffset();
        var settings = new PortraitSettings
        {
            CameraPosition = Floats(portrait, "CameraPosition"),
            CameraTarget = Floats(portrait, "CameraTarget"),
            ImageRotation = portrait.GetProperty("ImageRotation").GetInt16(),
            CameraZoom = portrait.GetProperty("CameraZoom").GetByte(),
            Pose = portrait.GetProperty("BannerTimeline").GetUInt16(),
            AnimationProgress = portrait.GetProperty("AnimationProgress").GetSingle(),
            Expression = portrait.GetProperty("Expression").GetByte(),
            HeadDirection = Floats(portrait, "HeadDirection"),
            EyeDirection = Floats(portrait, "EyeDirection"),
            DirectionalRed = portrait.GetProperty("DirectionalLightingColorRed").GetByte(),
            DirectionalGreen = portrait.GetProperty("DirectionalLightingColorGreen").GetByte(),
            DirectionalBlue = portrait.GetProperty("DirectionalLightingColorBlue").GetByte(),
            DirectionalBrightness = portrait.GetProperty("DirectionalLightingBrightness").GetByte(),
            DirectionalVerticalAngle = portrait.GetProperty("DirectionalLightingVerticalAngle").GetInt16(),
            DirectionalHorizontalAngle = portrait.GetProperty("DirectionalLightingHorizontalAngle").GetInt16(),
            AmbientRed = portrait.GetProperty("AmbientLightingColorRed").GetByte(),
            AmbientGreen = portrait.GetProperty("AmbientLightingColorGreen").GetByte(),
            AmbientBlue = portrait.GetProperty("AmbientLightingColorBlue").GetByte(),
            AmbientBrightness = portrait.GetProperty("AmbientLightingBrightness").GetByte(),
            // Own-card captures kept the card's background separately; that's the one the plate shows.
            Background = portrait.GetProperty(fromEditor ? "Background" : "CardBackground").GetUInt16(),
            Frame = portrait.GetProperty("Frame").GetUInt16(),
            Accent = portrait.GetProperty("Accent").GetUInt16(),
        };
        if (!settings.IsValid()) throw new JsonException("Portrait values are out of range.");

        var owner = capture.GetProperty("Owner").GetUInt64();
        if (owner == 0) throw new JsonException("Missing owner.");
        return new PlatePreset
        {
            Id = capture.GetProperty("Id").GetGuid(),
            Name = PlatePreset.CleanName(entry.GetProperty("Name").GetString() ?? ""),
            CreatedAt = capture.GetProperty("CapturedAtUtc").GetDateTimeOffset(),
            UpdatedAt = saved,
            Owner = owner,
            Race = capture.GetProperty("Race").GetUInt32(),
            Tribe = capture.GetProperty("Tribe").GetUInt32(),
            Sex = capture.GetProperty("Sex").GetByte(),
            Portrait = settings,
        };
    }

    private static float[] Floats(JsonElement parent, string name) =>
        parent.GetProperty(name).EnumerateArray().Select(v => v.GetSingle()).ToArray();
}
