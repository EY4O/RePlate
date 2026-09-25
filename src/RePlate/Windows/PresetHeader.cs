using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using RePlate.Core.Plates;

namespace RePlate.Windows;

/// <summary>
/// The name of the selected plate or portrait with Rename, Share and Delete at the right, and the same three (plus
/// favourite) in the right-click menu. Both tabs use one each.
/// </summary>
public sealed class PresetHeader(Plugin plugin, PlateImages images, Action<string> status)
{
    private const string DeleteQuestion = "Delete this and its picture?";

    private string rename = "";
    private bool renaming;
    private bool confirmDelete;

    /// <summary>Forget a rename or delete in progress, when something else gets selected.</summary>
    public void Reset()
    {
        renaming = false;
        confirmDelete = false;
    }

    /// <summary>Draws the header. Returns true when the preset was deleted this frame.</summary>
    public bool Draw(PlatePreset preset, string subtitle)
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
            return false;
        }

        if (preset.Favorite) Summary.Star();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) ImGui.TextUnformatted(preset.Name);
        ImGui.SameLine();
        // The buttons sit at the right edge, or straight after a name too long to leave room.
        var style = ImGui.GetStyle();
        string[] buttons = confirmDelete ? ["Rename", "Share", "Yes, delete", "Keep"] : ["Rename", "Share", "Delete"];
        var width = buttons.Sum(b => ImGui.CalcTextSize(b).X + style.FramePadding.X * 2) + style.ItemSpacing.X * (buttons.Length - 1);
        if (confirmDelete) width += ImGui.CalcTextSize(DeleteQuestion).X + style.ItemSpacing.X;
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - width));
        var deleted = false;
        using (ImRaii.Disabled(!plugin.Store.CanWrite))
        {
            if (ImGui.SmallButton("Rename")) StartRename(preset);
            ImGui.SameLine();
            if (ImGui.SmallButton("Share")) Share(preset);
            Ui.Tip("Copies a code anyone with RePlate can import. It holds what's saved here, not who you are, and not its picture.");
            ImGui.SameLine();
            if (!confirmDelete)
            {
                if (Theme.DangerButton("Delete")) confirmDelete = true;
            }
            else
            {
                ImGui.TextColored(Theme.Warning, DeleteQuestion);
                ImGui.SameLine();
                if (Theme.DangerButton("Yes, delete"))
                {
                    plugin.Store.Remove(preset.Id);
                    plugin.Store.Save();
                    images.Delete(preset.Id);
                    confirmDelete = false;
                    deleted = true;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Keep")) confirmDelete = false;
            }
        }
        ImGui.TextDisabled(subtitle);
        return deleted;
    }

    /// <summary>The right-click menu on a plate or portrait. Rename and Delete select it and use the header's own.</summary>
    public void Menu(PlatePreset preset, Action select)
    {
        using var menu = ImRaii.ContextPopupItem("##presetMenu");
        if (!menu.Success) return;
        using (ImRaii.Disabled(!plugin.Store.CanWrite))
        {
            if (ImGui.MenuItem(preset.Favorite ? "Remove from favorites" : "Add to favorites"))
            {
                // Not an edit to the preset itself, so it keeps its place among the others by date.
                preset.Favorite = !preset.Favorite;
                plugin.Store.Save();
            }
            if (ImGui.MenuItem("Rename"))
            {
                select();
                StartRename(preset);
            }
        }
        if (ImGui.MenuItem("Share")) Share(preset);
        using (ImRaii.Disabled(!plugin.Store.CanWrite))
        {
            if (ImGui.MenuItem("Delete"))
            {
                select();
                renaming = false;
                confirmDelete = true;
            }
        }
    }

    private void StartRename(PlatePreset preset)
    {
        rename = preset.Name;
        renaming = true;
        confirmDelete = false;
    }

    private void Share(PlatePreset preset)
    {
        ImGui.SetClipboardText(ShareCode.Encode(preset));
        status("Share code copied. Paste it wherever you like.");
    }
}
