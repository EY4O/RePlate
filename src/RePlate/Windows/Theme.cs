using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace RePlate.Windows;

public enum AccentChoice { GilGold, Rose, AetherBlue, Jade, MoogleViolet, Custom }

public enum Tone { Neutral, Accent, Good, Warning, Bad, Info }

/// <summary>Dark panels, rounded frames and an accent colour, applied to RePlate's own windows only.</summary>
internal static class Theme
{
    private const uint DefaultAccent = 0xE0A63A;
    private static Configuration? config;

    public static void Use(Configuration configuration) => config = configuration;

    public static bool On => config?.UseTheme ?? false;

    public static readonly (AccentChoice Choice, string Name, uint Rgb)[] Accents =
    [
        (AccentChoice.GilGold, "Gil gold", DefaultAccent),
        (AccentChoice.Rose, "Rose", 0xE07A8C),
        (AccentChoice.AetherBlue, "Aether blue", 0x5AA9E6),
        (AccentChoice.Jade, "Jade", 0x4FBF8F),
        (AccentChoice.MoogleViolet, "Moogle violet", 0xA58BE8),
        (AccentChoice.Custom, "Custom", 0),
    ];

    public static Vector4 Accent => config switch
    {
        null => Rgb(DefaultAccent),
        { Accent: AccentChoice.Custom } c => Rgb(c.CustomAccent),
        var c => Rgb(Accents.FirstOrDefault(a => a.Choice == c.Accent).Rgb is var rgb and not 0 ? rgb : DefaultAccent),
    };

    private static readonly Vector4 Background = Rgb(0x15161B, 0.98f), Surface = Rgb(0x1D1E25), Frame = Rgb(0x24252D),
        FrameHover = Rgb(0x2E2F39), FramePress = Rgb(0x383A45), Line = Rgb(0x2C2D36), ButtonFill = Rgb(0x2A2B34),
        ButtonHover = Rgb(0x353642), ButtonPress = Rgb(0x404150), DangerFill = Rgb(0xC9433F), White = new(1, 1, 1, 1);

    public static Vector4 Color(Tone tone) => tone switch
    {
        Tone.Accent => On ? Accent : ImGuiColors.DalamudOrange,
        Tone.Good => On ? Rgb(0x8FD19E) : ImGuiColors.HealerGreen,
        Tone.Warning => On ? Rgb(0xF0C36B) : ImGuiColors.DalamudYellow,
        Tone.Bad => On ? Rgb(0xE5605C) : ImGuiColors.DalamudRed,
        Tone.Info => On ? Rgb(0x7FB7E8) : ImGuiColors.ParsedBlue,
        _ => On ? Rgb(0x9A9BA3) : ImGuiColors.DalamudGrey,
    };

    public static Vector4 AccentText => Color(Tone.Accent);
    public static Vector4 Good => Color(Tone.Good);
    public static Vector4 Warning => Color(Tone.Warning);
    public static Vector4 Bad => Color(Tone.Bad);
    public static Vector4 Muted => Color(Tone.Neutral);

    public readonly record struct Pushed(int Colors, int Vars);

    public static Pushed Push()
    {
        if (!On) return default;
        var accent = Accent;
        (ImGuiCol, Vector4)[] colors =
        [
            (ImGuiCol.WindowBg, Background), (ImGuiCol.ChildBg, new(0, 0, 0, 0)), (ImGuiCol.PopupBg, Surface with { W = 0.98f }),
            (ImGuiCol.Border, Line), (ImGuiCol.BorderShadow, new(0, 0, 0, 0)),
            (ImGuiCol.FrameBg, Frame), (ImGuiCol.FrameBgHovered, FrameHover), (ImGuiCol.FrameBgActive, FramePress),
            (ImGuiCol.TitleBg, Background), (ImGuiCol.TitleBgActive, Surface), (ImGuiCol.TitleBgCollapsed, Background),
            (ImGuiCol.MenuBarBg, Surface),
            (ImGuiCol.ScrollbarBg, Background with { W = 0.6f }), (ImGuiCol.ScrollbarGrab, FrameHover),
            (ImGuiCol.ScrollbarGrabHovered, FramePress), (ImGuiCol.ScrollbarGrabActive, accent with { W = 0.8f }),
            (ImGuiCol.CheckMark, accent), (ImGuiCol.SliderGrab, accent), (ImGuiCol.SliderGrabActive, Lighter(accent)),
            (ImGuiCol.Button, ButtonFill), (ImGuiCol.ButtonHovered, ButtonHover), (ImGuiCol.ButtonActive, ButtonPress),
            (ImGuiCol.Header, accent with { W = 0.22f }), (ImGuiCol.HeaderHovered, accent with { W = 0.32f }),
            (ImGuiCol.HeaderActive, accent with { W = 0.45f }),
            (ImGuiCol.Separator, Line), (ImGuiCol.SeparatorHovered, accent with { W = 0.6f }), (ImGuiCol.SeparatorActive, accent),
            (ImGuiCol.ResizeGrip, accent with { W = 0.2f }), (ImGuiCol.ResizeGripHovered, accent with { W = 0.55f }),
            (ImGuiCol.ResizeGripActive, accent with { W = 0.85f }),
            (ImGuiCol.Tab, Surface), (ImGuiCol.TabHovered, accent with { W = 0.4f }), (ImGuiCol.TabActive, Mix(Surface, accent, 0.35f)),
            (ImGuiCol.TabUnfocused, Background), (ImGuiCol.TabUnfocusedActive, Mix(Surface, accent, 0.2f)),
            (ImGuiCol.PlotHistogram, accent), (ImGuiCol.PlotHistogramHovered, Lighter(accent)),
            (ImGuiCol.TableHeaderBg, Surface), (ImGuiCol.TableBorderStrong, Line), (ImGuiCol.TableBorderLight, Frame),
            (ImGuiCol.TableRowBg, new(0, 0, 0, 0)), (ImGuiCol.TableRowBgAlt, new(1, 1, 1, 0.025f)),
            (ImGuiCol.TextSelectedBg, accent with { W = 0.35f }), (ImGuiCol.NavHighlight, accent),
        ];
        foreach (var (column, value) in colors) ImGui.PushStyleColor(column, value);
        var scale = ImGuiHelpers.GlobalScale;
        (ImGuiStyleVar, float)[] vars =
        [
            (ImGuiStyleVar.WindowRounding, 8 * scale), (ImGuiStyleVar.ChildRounding, 6 * scale),
            (ImGuiStyleVar.FrameRounding, 5 * scale), (ImGuiStyleVar.PopupRounding, 6 * scale),
            (ImGuiStyleVar.ScrollbarRounding, 8 * scale), (ImGuiStyleVar.GrabRounding, 4 * scale),
            (ImGuiStyleVar.TabRounding, 5 * scale), (ImGuiStyleVar.FrameBorderSize, 0),
        ];
        foreach (var (variable, value) in vars) ImGui.PushStyleVar(variable, value);
        return new(colors.Length, vars.Length);
    }

    public static void Pop(Pushed pushed)
    {
        if (pushed.Vars > 0) ImGui.PopStyleVar(pushed.Vars);
        if (pushed.Colors > 0) ImGui.PopStyleColor(pushed.Colors);
    }

    /// <summary>The main button on a screen, filled with the accent. A plain button with the theme off.</summary>
    public static bool PrimaryButton(string label, Vector2 size = default)
    {
        using var colors = Filled(Accent, OnAccent, On);
        return ImGui.Button(label, size);
    }

    /// <summary>Filled with the accent even with the theme off, for the one button that should always stand out.</summary>
    public static bool AccentIconButton(FontAwesomeIcon icon, string text)
    {
        using var colors = Filled(Accent, OnAccent, true);
        return ImGuiComponents.IconButtonWithText(icon, text);
    }

    /// <summary>The same with only the icon.</summary>
    public static bool AccentIconButton(string id, FontAwesomeIcon icon)
    {
        using var colors = Filled(Accent, OnAccent, true);
        return ImGuiComponents.IconButton(id, icon);
    }

    /// <summary>Filled with a brand's own colour (0xRRGGBB), theme or not, with text that stays readable on it.</summary>
    public static bool BrandButton(FontAwesomeIcon icon, string text, uint rgb)
    {
        var fill = Rgb(rgb);
        using var colors = Filled(fill, TextOn(fill), true);
        return ImGuiComponents.IconButtonWithText(icon, text);
    }

    /// <summary>For removing things: red with the theme, red text without.</summary>
    public static bool DangerButton(string label)
    {
        using var colors = On ? Filled(DangerFill, White, true) : ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudRed);
        return ImGui.SmallButton(label);
    }

    private static ImRaii.ColorDisposable Filled(Vector4 fill, Vector4 text, bool condition) =>
        ImRaii.PushColor(ImGuiCol.Button, fill, condition)
            .Push(ImGuiCol.ButtonHovered, Lighter(fill), condition)
            .Push(ImGuiCol.ButtonActive, Darker(fill), condition)
            .Push(ImGuiCol.Text, text, condition);

    private static Vector4 OnAccent => TextOn(Accent);

    // Dark text on a light fill, white on a dark one.
    private static Vector4 TextOn(Vector4 fill) =>
        0.2126f * fill.X + 0.7152f * fill.Y + 0.0722f * fill.Z > 0.5f ? Rgb(0x1E1606) : White;

    /// <summary>A short status in a tinted, rounded label. Plain coloured text with the theme off.</summary>
    public static void Pill(string text, Tone tone)
    {
        var color = Color(tone);
        if (!On)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, color)) ImGui.TextUnformatted(text);
            return;
        }
        var pad = new Vector2(8 * ImGuiHelpers.GlobalScale, ImGui.GetStyle().FramePadding.Y * 0.5f);
        var size = ImGui.CalcTextSize(text) + pad * 2;
        var start = ImGui.GetCursorScreenPos();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(start, start + size, ImGui.GetColorU32(color with { W = 0.16f }), size.Y / 2);
        draw.AddText(start + pad, ImGui.GetColorU32(color), text);
        ImGui.Dummy(size);
    }

    /// <summary>A small heading for a group of settings.</summary>
    public static void Section(string title)
    {
        ImGui.Spacing();
        using (ImRaii.PushColor(ImGuiCol.Text, Muted, On)) ImGui.TextUnformatted(On ? title.ToUpperInvariant() : title);
        ImGui.Separator();
    }

    public static Vector4 Rgb(uint rgb, float alpha = 1f) =>
        new(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f, alpha);

    public static uint ToRgb(Vector3 color) =>
        ((uint)Math.Round(Math.Clamp(color.X, 0, 1) * 255) << 16) |
        ((uint)Math.Round(Math.Clamp(color.Y, 0, 1) * 255) << 8) |
        (uint)Math.Round(Math.Clamp(color.Z, 0, 1) * 255);

    private static Vector4 Mix(Vector4 from, Vector4 to, float amount) => (from + (to - from) * amount) with { W = 1 };
    private static Vector4 Lighter(Vector4 color) => Mix(color, White, 0.15f);
    private static Vector4 Darker(Vector4 color) => Mix(color, new Vector4(0, 0, 0, 1), 0.15f);
}

/// <summary>A window that wears the RePlate theme while it draws.</summary>
public abstract class ThemedWindow(string name, ImGuiWindowFlags flags = ImGuiWindowFlags.None) : Window(name, flags)
{
    private Theme.Pushed pushed;

    public override void PreDraw() => pushed = Theme.Push();

    public override void PostDraw()
    {
        Theme.Pop(pushed);
        pushed = default;
    }
}
