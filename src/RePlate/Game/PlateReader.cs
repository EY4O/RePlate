using System;
using System.Numerics;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using RePlate.Core.Plates;

namespace RePlate.Game;

public sealed record CaptureResult(PlatePreset? Preset, string Message);

/// <summary>Reads your own adventurer plate while it's open. Call from the framework thread.</summary>
public sealed unsafe class PlateReader
{
    public CaptureResult Capture(ulong owner, uint race, uint tribe, byte sex)
    {
        var problem = GetOwnCard(owner, out var card);
        if (problem != null) return new(null, problem);
        if (EditorOpen()) return new(null, "Close the portrait and design editors first, so the saved plate is what gets copied.");

        var design = &card->PlateDesign;
        if (design->NumDecorations > design->Decorations.Length) return new(null, "The plate's decorations couldn't be read.");
        var plate = new PlateDesign
        {
            BasePlate = design->BasePlate,
            TopBorder = design->TopBorder,
            BottomBorder = design->BottomBorder,
            Decorations = design->Decorations[..design->NumDecorations].ToArray(),
            InvertPortraitPlacement = card->InvertPortraitPlacement,
        };

        // A plate reset by a Fantasia (or never made) has no portrait worth keeping.
        PortraitSettings? portrait = null;
        if (!card->IsNotCreated && !card->WasResetDueToFantasia)
        {
            var p = card->PortraitData;
            portrait = new PortraitSettings
            {
                CameraPosition = [(float)p.CameraPosition.X, (float)p.CameraPosition.Y, (float)p.CameraPosition.Z, (float)p.CameraPosition.W],
                CameraTarget = [(float)p.CameraTarget.X, (float)p.CameraTarget.Y, (float)p.CameraTarget.Z, (float)p.CameraTarget.W],
                ImageRotation = p.ImageRotation,
                CameraZoom = p.CameraZoom,
                Pose = p.BannerTimeline,
                AnimationProgress = p.AnimationProgress,
                Expression = p.Expression,
                HeadDirection = [(float)p.HeadDirection.X, (float)p.HeadDirection.Y],
                EyeDirection = [(float)p.EyeDirection.X, (float)p.EyeDirection.Y],
                DirectionalRed = p.DirectionalLightingColorRed,
                DirectionalGreen = p.DirectionalLightingColorGreen,
                DirectionalBlue = p.DirectionalLightingColorBlue,
                DirectionalBrightness = p.DirectionalLightingBrightness,
                DirectionalVerticalAngle = p.DirectionalLightingVerticalAngle,
                DirectionalHorizontalAngle = p.DirectionalLightingHorizontalAngle,
                AmbientRed = p.AmbientLightingColorRed,
                AmbientGreen = p.AmbientLightingColorGreen,
                AmbientBlue = p.AmbientLightingColorBlue,
                AmbientBrightness = p.AmbientLightingBrightness,
                Background = card->BannerBg,
                Frame = card->BannerFrame,
                Accent = card->BannerDecoration,
            };
            if (!portrait.IsValid()) return new(null, "The portrait couldn't be read.");
        }

        var now = DateTimeOffset.UtcNow;
        var preset = new PlatePreset
        {
            CreatedAt = now,
            UpdatedAt = now,
            Owner = owner,
            Race = race,
            Tribe = tribe,
            Sex = sex,
            Portrait = portrait,
            Design = plate,
        };
        return new(preset, portrait == null
            ? "Saved the plate design. The portrait was reset by the game, so it wasn't saved."
            : "Saved your plate.");
    }

    /// <summary>Opens your own plate, as the game does when you view it from the character menu.</summary>
    public string? OpenOwnPlate(ulong owner)
    {
        if (owner == 0) return "Log in first.";
        var agent = AgentCharaCard.Instance();
        if (agent == null) return "The adventurer plate isn't available right now.";
        agent->OpenCharaCard(owner);
        return null;
    }

    /// <summary>Where your open plate window is on screen, for suggesting a crop.</summary>
    public (Vector2 Position, Vector2 Size)? PlateWindow(ulong owner)
    {
        if (GetOwnCard(owner, out _) != null) return null;
        var addon = Plugin.GameGui.GetAddonByName("CharaCard");
        return addon.IsNull ? null : (addon.Position, addon.ScaledSize);
    }

    private static string? GetOwnCard(ulong owner, out AgentCharaCard.Storage* card)
    {
        card = null;
        if (owner == 0) return "Log in first.";
        var addon = Plugin.GameGui.GetAddonByName("CharaCard");
        if (addon.IsNull || !addon.IsReady || !addon.IsVisible) return "Open your adventurer plate first.";
        var pointer = Plugin.GameGui.GetAgentById((int)AgentId.CharaCard);
        if (pointer.IsNull) return "The adventurer plate isn't available right now.";
        var agent = (AgentCharaCard*)pointer.Address;
        if (agent->AddonId != addon.Id || agent->Data == null) return "The adventurer plate is still loading.";
        if (agent->Data->ContentId != owner || !agent->Data->CanEdit) return "That's someone else's plate. Open your own.";
        card = agent->Data;
        return null;
    }

    private static bool EditorOpen()
    {
        var portrait = Plugin.GameGui.GetAddonByName("BannerEditor");
        if (!portrait.IsNull && portrait.IsVisible) return true;
        var design = Plugin.GameGui.GetAgentById((int)AgentId.CharaCardDesignSetting);
        return !design.IsNull && ((AgentInterface*)design.Address)->IsAgentActive();
    }
}
