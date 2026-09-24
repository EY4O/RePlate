using System;
using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace RePlate.Windows;

public sealed class SettingsWindow : ThemedWindow
{
    private readonly Plugin plugin;

    public SettingsWindow(Plugin plugin) : base("RePlate Settings###RePlateSettings", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(500, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(420, 300), MaximumSize = new Vector2(900, 1000) };
    }

    public override void Draw()
    {
        var c = plugin.Configuration;
        var changed = false;

        Theme.Section("Restoring");
        using (var grid = Grid("##restore"))
        {
            if (grid.Success)
                changed |= Row("Pause before saving",
                    "Restore puts the portrait or design in and stops with the editor open, so you can look it over and press " +
                    "Save yourself. Press Restore again afterwards for the other part.",
                    () => Checkbox("##pause", c.PauseBeforeSave, v => c.PauseBeforeSave = v));
        }

        Theme.Section("Look");
        using (var grid = Grid("##look"))
        {
            if (grid.Success)
            {
                changed |= Row("Use the RePlate theme",
                    "Dark panels, rounded corners and one accent colour. Off gives RePlate's windows Dalamud's own style.",
                    () => Checkbox("##theme", c.UseTheme, v => c.UseTheme = v));
                changed |= Row("Accent colour", "The colour of main buttons, checkmarks and the selected tab.", () =>
                {
                    var names = Theme.Accents.Select(a => a.Name).ToArray();
                    var index = Math.Max(0, Array.FindIndex(Theme.Accents, a => a.Choice == c.Accent));
                    if (!ImGui.Combo("##accent", ref index, names)) return false;
                    c.Accent = Theme.Accents[index].Choice;
                    return true;
                });
                if (c.Accent == AccentChoice.Custom)
                    changed |= Row("Custom colour", "Text on it turns dark or light by itself so it stays readable.", () =>
                    {
                        var rgb = Theme.Rgb(c.CustomAccent);
                        var colour = new Vector3(rgb.X, rgb.Y, rgb.Z);
                        if (!ImGui.ColorEdit3("##custom", ref colour, ImGuiColorEditFlags.DisplayHex)) return false;
                        c.CustomAccent = Theme.ToRgb(colour);
                        return true;
                    });
            }
        }
        ImGui.Spacing();
        Theme.PrimaryButton("Main button##preview");
        ImGui.SameLine();
        ImGui.Button("Other button##preview");

        ImGui.Spacing();
        ImGui.Separator();
        if (ImGui.Button("Open the welcome guide")) plugin.OpenWelcome();

        if (changed) plugin.MarkDirty();
    }

    private static ImRaii.TableDisposable Grid(string id)
    {
        var table = ImRaii.Table(id, 2, ImGuiTableFlags.SizingFixedFit);
        if (!table.Success) return table;
        ImGui.TableSetupColumn("##label", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##value", ImGuiTableColumnFlags.WidthFixed, 170 * ImGuiHelpers.GlobalScale);
        return table;
    }

    private static bool Row(string label, string help, Func<bool> control)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.AlignTextToFramePadding();
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker(help);
        ImGui.TableNextColumn();
        ImGui.SetNextItemWidth(-1);
        return control();
    }

    private static bool Checkbox(string id, bool value, Action<bool> set)
    {
        var v = value;
        if (!ImGui.Checkbox(id, ref v)) return false;
        set(v);
        return true;
    }
}
