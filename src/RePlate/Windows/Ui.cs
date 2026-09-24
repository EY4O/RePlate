using System;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;

namespace RePlate.Windows;

/// <summary>Small drawing helpers shared by the windows.</summary>
internal static class Ui
{
    public static void Tip(string text)
    {
        if (ImGui.IsItemHovered()) ImGui.SetTooltip(text);
    }

    /// <summary>Like <see cref="Tip"/>, but also shows on a disabled control.</summary>
    public static void TipAlways(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip(text);
    }

    public static string Ago(DateTimeOffset time)
    {
        var age = DateTimeOffset.UtcNow - time;
        return age.TotalMinutes < 1 ? "just now"
            : age.TotalHours < 1 ? $"{age.TotalMinutes:0} min ago"
            : age.TotalDays < 1 ? $"{age.TotalHours:0} h ago"
            : $"{age.TotalDays:0} d ago";
    }

    public static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, $"Couldn't open {url}");
        }
    }

    public static void Centered(string text)
    {
        var width = ImGui.CalcTextSize(text).X;
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2) + ImGui.GetCursorPosX());
        ImGui.TextUnformatted(text);
    }

    /// <summary>The plugin's logo, centred.</summary>
    public static void Logo(float size)
    {
        var path = Path.Combine(Plugin.PluginInterface.AssemblyLocation.DirectoryName ?? "", "images", "logo.png");
        if (Plugin.TextureProvider.GetFromFile(path).GetWrapOrDefault() is not { } icon) return;
        var scaled = new Vector2(size, size) * ImGuiHelpers.GlobalScale;
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - scaled.X) / 2) + ImGui.GetCursorPosX());
        ImGui.Image(icon.Handle, scaled);
    }
}
