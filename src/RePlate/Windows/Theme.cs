using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace RePlate.Windows;

public enum AccentChoice { GilGold, Rose, AetherBlue, Jade, MoogleViolet, Custom }

public enum Tone { Neutral, Accent, Good, Warning, Bad, Info }

/// <summary>
/// RePlate's look, applied to its own windows only: its own dark panels with an accent colour, or the game's style,
/// with the game's own font, warm charcoal panels, bronze borders and gold highlights.
/// </summary>
internal static class Theme
{
    private const uint DefaultAccent = 0xE0A63A;
    private const uint GameGold = 0xD6B26E;
    private static Configuration? config;
    private static IFontHandle? gameFont;

    public static void Use(Configuration configuration) => config = configuration;

    /// <summary>The game's UI font, loaded once for the game style.</summary>
    public static void LoadFonts(IFontAtlas atlas) => gameFont = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis12));

    public static void UnloadFonts()
    {
        gameFont?.Dispose();
        gameFont = null;
    }

    public static bool On => config?.UseTheme ?? false;

    /// <summary>The theme is on and made to look like the game's own windows.</summary>
    public static bool Game => On && config!.GameStyle;

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
        _ when Game => Rgb(GameGold),
        { Accent: AccentChoice.Custom } c => Rgb(c.CustomAccent),
        var c => Rgb(Accents.FirstOrDefault(a => a.Choice == c.Accent).Rgb is var rgb and not 0 ? rgb : DefaultAccent),
    };

    private sealed record Palette(Vector4 Background, Vector4 Surface, Vector4 Frame, Vector4 FrameHover, Vector4 FramePress,
        Vector4 Line, Vector4 ButtonFill, Vector4 ButtonHover, Vector4 ButtonPress);

    private static readonly Palette Own = new(Rgb(0x15161B, 0.98f), Rgb(0x1D1E25), Rgb(0x24252D), Rgb(0x2E2F39), Rgb(0x383A45),
        Rgb(0x2C2D36), Rgb(0x2A2B34), Rgb(0x353642), Rgb(0x404150));

    // Warm charcoal like the game's dark windows, with bronze edges.
    private static readonly Palette GameLook = new(Rgb(0x1B1A18, 0.95f), Rgb(0x25231F), Rgb(0x2E2B27), Rgb(0x3A3630), Rgb(0x46403A),
        Rgb(0x6B5C45), Rgb(0x34302B), Rgb(0x4A4237), Rgb(0x5A4F40));

    private static readonly Vector4 GameText = Rgb(0xEEE7D8), GameTextDim = Rgb(0xA79E8E);
    private static readonly Vector4 DangerFill = Rgb(0xC9433F), White = new(1, 1, 1, 1);

    private static Palette Look => Game ? GameLook : Own;
    private static Vector4 Background => Look.Background;
    private static Vector4 Surface => Look.Surface;
    private static Vector4 Frame => Look.Frame;
    private static Vector4 FrameHover => Look.FrameHover;
    private static Vector4 FramePress => Look.FramePress;
    private static Vector4 Line => Look.Line;
    private static Vector4 ButtonFill => Look.ButtonFill;
    private static Vector4 ButtonHover => Look.ButtonHover;
    private static Vector4 ButtonPress => Look.ButtonPress;

    public static Vector4 Color(Tone tone) => tone switch
    {
        Tone.Accent => On ? Accent : ImGuiColors.DalamudOrange,
        Tone.Good => On ? Rgb(0x8FD19E) : ImGuiColors.HealerGreen,
        Tone.Warning => On ? Rgb(0xF0C36B) : ImGuiColors.DalamudYellow,
        Tone.Bad => On ? Rgb(0xE5605C) : ImGuiColors.DalamudRed,
        Tone.Info => On ? Rgb(0x7FB7E8) : ImGuiColors.ParsedBlue,
        _ => Game ? GameTextDim : On ? Rgb(0x9A9BA3) : ImGuiColors.DalamudGrey,
    };

    public static Vector4 AccentText => Color(Tone.Accent);
    public static Vector4 Good => Color(Tone.Good);
    public static Vector4 Warning => Color(Tone.Warning);
    public static Vector4 Bad => Color(Tone.Bad);
    public static Vector4 Muted => Color(Tone.Neutral);

    public readonly record struct Pushed(int Colors, int Vars, IDisposable? Font = null);

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
        // The game style also has the game's warm text.
        (ImGuiCol, Vector4)[] text = Game ? [(ImGuiCol.Text, GameText), (ImGuiCol.TextDisabled, GameTextDim)] : [];
        foreach (var (column, value) in colors.Concat(text)) ImGui.PushStyleColor(column, value);
        var scale = ImGuiHelpers.GlobalScale;
        // The game's windows have thin edges and pill-shaped buttons.
        (ImGuiStyleVar, float)[] vars = Game
            ? [
                (ImGuiStyleVar.WindowRounding, 6 * scale), (ImGuiStyleVar.ChildRounding, 4 * scale),
                (ImGuiStyleVar.FrameRounding, 12 * scale), (ImGuiStyleVar.PopupRounding, 6 * scale),
                (ImGuiStyleVar.ScrollbarRounding, 8 * scale), (ImGuiStyleVar.GrabRounding, 10 * scale),
                (ImGuiStyleVar.TabRounding, 5 * scale), (ImGuiStyleVar.FrameBorderSize, 1), (ImGuiStyleVar.WindowBorderSize, 1),
            ]
            : [
                (ImGuiStyleVar.WindowRounding, 8 * scale), (ImGuiStyleVar.ChildRounding, 6 * scale),
                (ImGuiStyleVar.FrameRounding, 5 * scale), (ImGuiStyleVar.PopupRounding, 6 * scale),
                (ImGuiStyleVar.ScrollbarRounding, 8 * scale), (ImGuiStyleVar.GrabRounding, 4 * scale),
                (ImGuiStyleVar.TabRounding, 5 * scale), (ImGuiStyleVar.FrameBorderSize, 0),
            ];
        foreach (var (variable, value) in vars) ImGui.PushStyleVar(variable, value);
        return new(colors.Length + text.Length, vars.Length, Game ? gameFont?.Push() : null);
    }

    public static void Pop(Pushed pushed)
    {
        pushed.Font?.Dispose();
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
        // The game titles its sections in gold; the RePlate look uses small capitals.
        using (ImRaii.PushColor(ImGuiCol.Text, Game ? Accent : Muted, On)) ImGui.TextUnformatted(On && !Game ? title.ToUpperInvariant() : title);
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
