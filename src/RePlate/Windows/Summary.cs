using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using RePlate.Core.Plates;
using RePlate.Game;

namespace RePlate.Windows;

/// <summary>What a plate or portrait holds, by name, the same way wherever it's shown.</summary>
public static class Summary
{
    public static void Portrait(PortraitSettings? portrait)
    {
        Theme.Section("Portrait");
        if (portrait == null)
        {
            ImGui.TextDisabled("Not saved with this plate.");
            return;
        }
        Line("Pose", Names.Pose(portrait.Pose));
        Line("Expression", Names.Expression(portrait.Expression));
        Line("Background", Names.Background(portrait.Background));
        Line("Frame", Names.Frame(portrait.Frame));
        Line("Accent", Names.Accent(portrait.Accent));
    }

    public static void Design(PlateDesign? design)
    {
        Theme.Section("Plate design");
        if (design == null)
        {
            ImGui.TextDisabled("Not saved with this plate.");
            return;
        }
        // Every part is listed, None included, so plates line up when you flick between them.
        Line("Base plate", Names.BasePlate(design.BasePlate));
        Line("Pattern overlay", Names.Decoration(design, DecorationKind.Pattern));
        Line("Backing", Names.Decoration(design, DecorationKind.Backing));
        Line("Top border", Names.Border(design.TopBorder));
        Line("Bottom border", Names.Border(design.BottomBorder));
        Line("Portrait frame", Names.Decoration(design, DecorationKind.PortraitFrame));
        Line("Plate frame", Names.Decoration(design, DecorationKind.PlateFrame));
        Line("Accent", Names.Decoration(design, DecorationKind.Accent));
        Line("Layout", design.InvertPortraitPlacement ? "Flipped" : "Standard");
    }

    private static void Line(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(110 * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted(value);
    }

    /// <summary>A small star, a little under the text's size and centred on the line, with the cursor left after it.</summary>
    public static void Star()
    {
        const float Scale = 0.7f;
        var start = ImGui.GetCursorScreenPos();
        var line = ImGui.GetTextLineHeight();
        var icon = FontAwesomeIcon.Star.ToIconString();
        float width;
        using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
        {
            width = ImGui.CalcTextSize(icon).X * Scale;
            var size = ImGui.GetFontSize() * Scale;
            ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), size, start + new Vector2(0, (line - size) / 2),
                ImGui.GetColorU32(Theme.AccentText), icon);
        }
        ImGui.Dummy(new Vector2(width, line));
        ImGui.SameLine();
    }
}
