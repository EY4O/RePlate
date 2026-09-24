using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using RePlate.Core.Plates;
using RePlate.Game;

namespace RePlate.Windows;

/// <summary>Your saved plates on the left, the selected one on the right.</summary>
public sealed class PlatesTab(Plugin plugin, PlateImages images)
{
    private Task<CaptureResult>? capturing;
    private Task<string?>? opening;
    private string newName = "";
    private string filter = "";
    private string status = "";
    private string rename = "";
    private bool renaming;
    private bool confirmDelete;
    private Guid selected;

    public void Draw()
    {
        var store = plugin.Store;
        if (store.Error is { } error) ImGui.TextColored(Theme.Bad, error);
        var owner = plugin.CharacterId;
        if (owner == 0)
        {
            ImGui.TextDisabled("Log in to see your plates.");
            return;
        }

        FinishCapture(owner);
        DrawToolbar();
        ImGui.Separator();

        var presets = store.For(owner)
            .Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        Names.Race(p.Race, p.Sex).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (presets.All(p => p.Id != selected)) Select(presets.FirstOrDefault()?.Id ?? Guid.Empty);

        var listWidth = 250 * ImGuiHelpers.GlobalScale;
        using (var list = ImRaii.Child("##list", new Vector2(listWidth, 0), true))
        {
            if (list.Success) DrawList(presets);
        }
        ImGui.SameLine();
        using (var detail = ImRaii.Child("##detail", Vector2.Zero, false))
        {
            if (detail.Success) DrawDetail(store.Get(selected));
        }
    }

    private void DrawToolbar()
    {
        ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##newName", "Name for this plate", ref newName, PlatePreset.MaxNameLength);
        ImGui.SameLine();
        using (ImRaii.Disabled(capturing != null || !plugin.Store.CanWrite))
        {
            if (Theme.PrimaryButton("Save current plate")) StartCapture();
        }
        Ui.TipAlways("Copies the portrait and design from your open adventurer plate.");
        ImGui.SameLine();
        using (ImRaii.Disabled(opening != null))
        {
            if (ImGui.Button("Open my plate"))
            {
                var owner = plugin.CharacterId;
                opening = Plugin.Framework.RunOnFrameworkThread(() => plugin.Reader.OpenOwnPlate(owner));
            }
        }
        if (status.Length > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(status);
        }
    }

    private void StartCapture()
    {
        var owner = plugin.CharacterId;
        var race = Plugin.PlayerState.Race.RowId;
        var tribe = Plugin.PlayerState.Tribe.RowId;
        var sex = (byte)Plugin.PlayerState.Sex;
        status = "Reading your plate...";
        capturing = Plugin.Framework.RunOnFrameworkThread(() => plugin.Reader.Capture(owner, race, tribe, sex));
    }

    private void FinishCapture(ulong owner)
    {
        if (opening is { IsCompleted: true } open)
        {
            status = open.IsCompletedSuccessfully ? open.Result ?? "" : "Couldn't open your plate.";
            opening = null;
        }
        if (capturing is not { IsCompleted: true } done) return;
        capturing = null;
        if (!done.IsCompletedSuccessfully)
        {
            status = "Couldn't read your plate.";
            Plugin.Log.Error(done.Exception!, "Reading the plate failed");
            return;
        }
        status = done.Result.Message;
        if (done.Result.Preset is not { } preset || preset.Owner != owner) return;

        var name = PlatePreset.CleanName(newName);
        preset.Name = name.Length > 0 ? name : $"Plate {preset.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        plugin.Store.Add(preset);
        plugin.Store.Save();
        newName = "";
        filter = "";
        Select(preset.Id);
    }

    private void DrawList(List<PlatePreset> presets)
    {
        ImGui.SetNextItemWidth(-1);
        ImGui.InputTextWithHint("##filter", "Search", ref filter, 64);
        ImGui.TextDisabled($"{presets.Count} plate{(presets.Count == 1 ? "" : "s")}");
        ImGui.Separator();
        if (presets.Count == 0)
        {
            ImGui.TextWrapped(filter.Length > 0 ? "Nothing matches." : "No plates yet. Open your adventurer plate and press Save current plate.");
            return;
        }
        foreach (var preset in presets)
        {
            using var id = ImRaii.PushId(preset.Id.ToString());
            if (ImGui.Selectable(preset.Name, preset.Id == selected)) Select(preset.Id);
            var parts = (preset.Portrait != null ? "Portrait" : "") + (preset.Portrait != null && preset.Design != null ? " + " : "") +
                        (preset.Design != null ? "Design" : "");
            using (ImRaii.PushIndent())
                ImGui.TextDisabled($"{Names.Race(preset.Race, preset.Sex)} · {preset.UpdatedAt.ToLocalTime():yyyy-MM-dd} · {parts}");
        }
    }

    private void DrawDetail(PlatePreset? preset)
    {
        if (preset == null)
        {
            ImGui.TextDisabled("Select a plate.");
            return;
        }

        DrawHeader(preset);
        ImGui.Spacing();
        using (var table = ImRaii.Table("##summary", 2, ImGuiTableFlags.SizingStretchSame))
        {
            if (table.Success)
            {
                ImGui.TableNextColumn();
                DrawPortrait(preset.Portrait);
                ImGui.TableNextColumn();
                DrawDesign(preset.Design);
            }
        }
        ImGui.Separator();
        images.Draw(plugin.Store.CanWrite);
    }

    private void DrawHeader(PlatePreset preset)
    {
        if (renaming)
        {
            ImGui.SetNextItemWidth(260 * ImGuiHelpers.GlobalScale);
            var enter = ImGui.InputText("##rename", ref rename, PlatePreset.MaxNameLength, ImGuiInputTextFlags.EnterReturnsTrue);
            ImGui.SameLine();
            if ((ImGui.Button("Save name") || enter) && PlatePreset.CleanName(rename) is { Length: > 0 } name)
            {
                preset.Name = name;
                preset.UpdatedAt = DateTimeOffset.UtcNow;
                plugin.Store.Save();
                renaming = false;
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) renaming = false;
            return;
        }

        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) ImGui.TextUnformatted(preset.Name);
        ImGui.SameLine();
        using (ImRaii.Disabled(!plugin.Store.CanWrite))
        {
            if (ImGui.SmallButton("Rename"))
            {
                rename = preset.Name;
                renaming = true;
                confirmDelete = false;
            }
            ImGui.SameLine();
            if (!confirmDelete)
            {
                if (Theme.DangerButton("Delete")) confirmDelete = true;
            }
            else
            {
                ImGui.TextColored(Theme.Warning, "Delete this plate and its picture?");
                ImGui.SameLine();
                if (Theme.DangerButton("Yes, delete"))
                {
                    plugin.Store.Remove(preset.Id);
                    plugin.Store.Save();
                    images.Delete(preset.Id);
                    confirmDelete = false;
                    selected = Guid.Empty;
                    return;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Keep")) confirmDelete = false;
            }
        }
        ImGui.TextDisabled($"{Names.Race(preset.Race, preset.Sex)} · saved {preset.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}");
    }

    private static void DrawPortrait(PortraitSettings? portrait)
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

    private static void DrawDesign(PlateDesign? design)
    {
        Theme.Section("Plate design");
        if (design == null)
        {
            ImGui.TextDisabled("Not saved with this plate.");
            return;
        }
        Line("Base plate", Names.BasePlate(design.BasePlate));
        Line("Top border", Names.Border(design.TopBorder));
        Line("Bottom border", Names.Border(design.BottomBorder));
        foreach (var (kind, name) in Names.Decorations(design)) Line(Names.Kind(kind), name);
        Line("Portrait side", design.InvertPortraitPlacement ? "Flipped" : "Standard");
    }

    private static void Line(string label, string value)
    {
        ImGui.TextDisabled(label);
        ImGui.SameLine(110 * ImGuiHelpers.GlobalScale);
        ImGui.TextUnformatted(value);
    }

    private void Select(Guid id)
    {
        if (id == selected) return;
        selected = id;
        renaming = false;
        confirmDelete = false;
        images.Select(id);
    }
}
