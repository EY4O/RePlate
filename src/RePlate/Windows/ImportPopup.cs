using System.Linq;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using RePlate.Core.Plates;
using RePlate.Game;

namespace RePlate.Windows;

/// <summary>Paste a share code, see what it holds, add it to this character. Plates and portraits both come in here.</summary>
public sealed class ImportPopup(Plugin plugin)
{
    private const string Id = "Import from a share code###replateImport";

    private string code = "";
    private string problem = "";
    private SharedPlate? preview;

    public void Open()
    {
        code = "";
        problem = "";
        preview = null;
        ImGui.OpenPopup(Id);
    }

    /// <summary>Draws the popup when it's open. Returns what was added, the frame it's added.</summary>
    public PlatePreset? Draw(ulong owner)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(600, 0) * scale, ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(Id, ImGuiWindowFlags.AlwaysAutoResize)) return null;

        ImGui.TextUnformatted("Paste a RePlate share code:");
        if (ImGui.InputTextMultiline("##code", ref code, 5000, new Vector2(560 * scale, 70 * scale))) Read();
        if (ImGui.Button("Paste from clipboard"))
        {
            code = ImGui.GetClipboardText() ?? "";
            Read();
        }
        if (problem.Length > 0) ImGui.TextColored(Theme.Warning, problem);

        if (preview is { } shared)
        {
            var portrait = shared.Kind == PresetKind.Portrait;
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText))
                ImGui.TextUnformatted(shared.Name.Length > 0 ? shared.Name : portrait ? "Shared portrait" : "Shared plate");
            var made = string.Join(", ", new[] { Names.Job(shared.ClassJob), Names.Race(shared.Race, shared.Sex) }.Where(s => s.Length > 0));
            if (made.Length > 0) ImGui.TextDisabled($"{(portrait ? "A portrait" : "A plate")}, made on {made}");
            using (var table = ImRaii.Table("##preview", portrait ? 1 : 2, ImGuiTableFlags.SizingStretchSame))
            {
                if (table.Success)
                {
                    ImGui.TableNextColumn();
                    Summary.Portrait(shared.Portrait);
                    if (!portrait)
                    {
                        ImGui.TableNextColumn();
                        Summary.Design(shared.Design);
                    }
                }
            }
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted))
                ImGui.TextWrapped("Anything this character hasn't unlocked keeps your own choice, and you look it over before " +
                                  "anything is saved. On a different race the camera may need a nudge.");
        }

        PlatePreset? added = null;
        ImGui.Separator();
        using (ImRaii.Disabled(preview == null || !plugin.Store.CanWrite))
        {
            var label = preview?.Kind == PresetKind.Portrait ? "Add to my portraits" : "Add to my plates";
            if (Theme.PrimaryButton(label) && preview != null)
            {
                added = ShareCode.ToPreset(preview, owner);
                plugin.Store.Add(added);
                plugin.Store.Save();
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
        return added;
    }

    private void Read()
    {
        var empty = code.Trim().Length == 0;
        preview = empty ? null : ShareCode.Decode(code, out problem);
        if (empty) problem = "";
    }
}
