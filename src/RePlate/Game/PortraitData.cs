using System;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Common.Math;
using RePlate.Core.Plates;

namespace RePlate.Game;

/// <summary>Converts between the game's portrait data and saved settings.</summary>
public static class PortraitData
{
    public static PortraitSettings FromGame(in ExportedPortraitData p, ushort background, ushort frame, ushort accent) => new()
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
        Background = background,
        Frame = frame,
        Accent = accent,
    };

    /// <summary>A gear set's saved portrait.</summary>
    public static unsafe PortraitSettings FromBanner(BannerModuleEntry* b) => new()
    {
        CameraPosition = [(float)b->CameraPosition.X, (float)b->CameraPosition.Y, (float)b->CameraPosition.Z, (float)b->CameraPosition.W],
        CameraTarget = [(float)b->CameraTarget.X, (float)b->CameraTarget.Y, (float)b->CameraTarget.Z, (float)b->CameraTarget.W],
        ImageRotation = b->ImageRotation,
        CameraZoom = b->CameraZoom,
        Pose = b->BannerTimeline,
        AnimationProgress = b->AnimationProgress,
        Expression = b->Expression,
        HeadDirection = [(float)b->HeadDirection.X, (float)b->HeadDirection.Y],
        EyeDirection = [(float)b->EyeDirection.X, (float)b->EyeDirection.Y],
        DirectionalRed = b->DirectionalLightingColorRed,
        DirectionalGreen = b->DirectionalLightingColorGreen,
        DirectionalBlue = b->DirectionalLightingColorBlue,
        DirectionalBrightness = b->DirectionalLightingBrightness,
        DirectionalVerticalAngle = b->DirectionalLightingVerticalAngle,
        DirectionalHorizontalAngle = b->DirectionalLightingHorizontalAngle,
        AmbientRed = b->AmbientLightingColorRed,
        AmbientGreen = b->AmbientLightingColorGreen,
        AmbientBlue = b->AmbientLightingColorBlue,
        AmbientBrightness = b->AmbientLightingBrightness,
        Background = b->BannerBg,
        Frame = b->BannerFrame,
        Accent = b->BannerDecoration,
    };

    /// <summary>Everything but the frame and accent, which the editor sets separately.</summary>
    public static ExportedPortraitData ToGame(PortraitSettings p) => new()
    {
        CameraPosition = new HalfVector4((Half)p.CameraPosition[0], (Half)p.CameraPosition[1], (Half)p.CameraPosition[2], (Half)p.CameraPosition[3]),
        CameraTarget = new HalfVector4((Half)p.CameraTarget[0], (Half)p.CameraTarget[1], (Half)p.CameraTarget[2], (Half)p.CameraTarget[3]),
        ImageRotation = p.ImageRotation,
        CameraZoom = p.CameraZoom,
        BannerTimeline = p.Pose,
        AnimationProgress = p.AnimationProgress,
        Expression = p.Expression,
        HeadDirection = new HalfVector2((Half)p.HeadDirection[0], (Half)p.HeadDirection[1]),
        EyeDirection = new HalfVector2((Half)p.EyeDirection[0], (Half)p.EyeDirection[1]),
        DirectionalLightingColorRed = p.DirectionalRed,
        DirectionalLightingColorGreen = p.DirectionalGreen,
        DirectionalLightingColorBlue = p.DirectionalBlue,
        DirectionalLightingBrightness = p.DirectionalBrightness,
        DirectionalLightingVerticalAngle = p.DirectionalVerticalAngle,
        DirectionalLightingHorizontalAngle = p.DirectionalHorizontalAngle,
        AmbientLightingColorRed = p.AmbientRed,
        AmbientLightingColorGreen = p.AmbientGreen,
        AmbientLightingColorBlue = p.AmbientBlue,
        AmbientLightingBrightness = p.AmbientBrightness,
        BannerBg = p.Background,
    };
}
