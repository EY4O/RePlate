using System.Text.Json;
using RePlate.Core.Plates;
using Xunit;

namespace RePlate.Core.Tests;

public class LegacyLibraryTests
{
    // Shaped like library.json from earlier builds: one plate capture, one editor capture, one broken entry.
    private const string Library = """
        {
          "Version": 2,
          "Portraits": [
            {
              "Name": "Current plate",
              "SavedAtUtc": "2026-09-24T02:13:57+00:00",
              "Capture": {
                "Id": "3e7626aa-b9be-4836-a174-af028f107143", "Owner": 18014498568585221, "Race": 3, "Tribe": 6, "Sex": 0,
                "CapturedAtUtc": "2026-09-24T02:13:22+00:00", "GameVersion": "2026.09.15.0000.0000", "DalamudVersion": "15.0.3.5",
                "ClientStructsVersion": "7.56.2.9089", "SourceKind": "OwnCardPortrait",
                "Portrait": {
                  "Source": "x", "VectorOrder": "x",
                  "CameraPosition": [-0.84375, 0.26757812, 1.7480469, 0], "CameraTarget": [0.0836792, -0.57714844, 0.19421387, 0],
                  "ImageRotation": 0, "CameraZoom": 200, "BannerTimeline": 50, "AnimationProgress": 165.6, "Expression": 0,
                  "HeadDirection": [0, 0], "EyeDirection": [0.3474121, 0.08673096],
                  "DirectionalLightingColorRed": 212, "DirectionalLightingColorGreen": 141, "DirectionalLightingColorBlue": 255,
                  "DirectionalLightingBrightness": 105, "DirectionalLightingVerticalAngle": 133, "DirectionalLightingHorizontalAngle": 133,
                  "AmbientLightingColorRed": 51, "AmbientLightingColorGreen": 51, "AmbientLightingColorBlue": 51, "AmbientLightingBrightness": 168,
                  "PayloadBackground": 150, "CardBackground": 158, "Frame": 3, "Accent": 105
                }
              }
            },
            {
              "Name": "From the editor",
              "SavedAtUtc": "2026-09-24T03:00:00+00:00",
              "Capture": {
                "Id": "44cbd34c-f0f6-4b29-a3cc-d04142ae477d", "Owner": 18014498568585221, "Race": 3, "Tribe": 6, "Sex": 0,
                "CapturedAtUtc": "2026-09-24T03:00:00+00:00", "SourceKind": "AdventurerPlateEditor", "Portrait": null,
                "EditorPortrait": {
                  "CameraPosition": [0, 1, 2, 0], "CameraTarget": [0, 0, 0, 0], "ImageRotation": -5, "CameraZoom": 100,
                  "BannerTimeline": 12, "AnimationProgress": 0, "Expression": 7, "HeadDirection": [0.1, 0.2], "EyeDirection": [0, 0],
                  "DirectionalLightingColorRed": 1, "DirectionalLightingColorGreen": 2, "DirectionalLightingColorBlue": 3,
                  "DirectionalLightingBrightness": 4, "DirectionalLightingVerticalAngle": -5, "DirectionalLightingHorizontalAngle": 6,
                  "AmbientLightingColorRed": 7, "AmbientLightingColorGreen": 8, "AmbientLightingColorBlue": 9, "AmbientLightingBrightness": 10,
                  "Background": 108, "Frame": 2, "Accent": 7
                }
              }
            },
            { "Name": "Broken", "SavedAtUtc": "2026-09-24T03:00:00+00:00", "Capture": { "Id": "not-a-guid" } }
          ]
        }
        """;

    [Fact]
    public void OldEntriesBecomePortraitOnlyPlates()
    {
        var presets = LegacyLibrary.Read(Library, out var skipped);

        Assert.Equal(1, skipped);
        Assert.Equal(2, presets.Count);

        var card = presets[0];
        Assert.Equal(Guid.Parse("3e7626aa-b9be-4836-a174-af028f107143"), card.Id);
        Assert.Equal("Current plate", card.Name);
        Assert.Equal(18014498568585221ul, card.Owner);
        Assert.Equal((3u, 6u, (byte)0), (card.Race, card.Tribe, card.Sex));
        Assert.Null(card.Design);
        Assert.Equal(50, card.Portrait!.Pose);
        Assert.Equal(158, card.Portrait.Background); // the card's own background, not the payload's
        Assert.Equal(165.6f, card.Portrait.AnimationProgress);
        Assert.Equal([0.3474121f, 0.08673096f], card.Portrait.EyeDirection);

        var editor = presets[1];
        Assert.Equal(108, editor.Portrait!.Background);
        Assert.Equal(-5, editor.Portrait.ImageRotation);
        Assert.Equal(7, editor.Portrait.Expression);
    }

    [Fact]
    public void OtherFilesAreRejected()
    {
        Assert.Throws<JsonException>(() => LegacyLibrary.Read("""{ "Version": 3, "Portraits": [] }""", out _));
        Assert.Throws<JsonException>(() => LegacyLibrary.Read("""{ "Presets": [] }""", out _));
    }
}
