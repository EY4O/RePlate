using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using RePlate.Core.Plates;
using RePlate.Game;

namespace RePlate.Windows;

/// <summary>Your saved plates on the left, the selected one on the right.</summary>
public sealed class PlatesTab(Plugin plugin, PlateImages images)
{
    // Long enough for the pose to load before the editor is read back.
    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(1);

    private const string ImportPopup = "Import a shared plate###replateImport";
    private const string DeleteQuestion = "Delete this plate and its picture?";

    private Task<CaptureResult>? capturing;
    private string importCode = "";
    private string importProblem = "";
    private SharedPlate? importPreview;
    private Task<string?>? opening;
    private Task<ApplyResult>? applying;
    private Task<ApplyResult>? checking;
    private Task<ApplyResult>? starting;
    private PlatePreset? applied;
    private DateTime checkAt;
    private string newName = "";
    private string filter = "";
    private string status = "";
    private bool statusWarning;
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
        FinishApply(owner);
        FinishRuns();
        plugin.Guide.Update(PlateReader.OwnPlateOpen(owner), store.For(owner).Count, images, plugin.Restore.Running);
        plugin.Guide.DrawBar();
        DrawToolbar();
        DrawImport(owner);
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
            plugin.Guide.Mark(GuideTarget.SavePlate);
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
            plugin.Guide.Mark(GuideTarget.OpenPlate);
        }
        ImGui.SameLine();
        if (ImGui.Button("Import"))
        {
            importCode = "";
            importPreview = null;
            importProblem = "";
            ImGui.OpenPopup(ImportPopup);
        }
        Ui.Tip("Add a plate someone shared with you, from its share code.");
        if (status.Length > 0)
        {
            ImGui.SameLine();
            if (statusWarning) ImGui.TextColored(Theme.Warning, status);
            else ImGui.TextDisabled(status);
        }
    }

    private void SetStatus(string text, bool warning = false)
    {
        status = text;
        statusWarning = warning;
    }

    private void StartCapture()
    {
        var owner = plugin.CharacterId;
        var race = Plugin.PlayerState.Race.RowId;
        var tribe = Plugin.PlayerState.Tribe.RowId;
        var sex = (byte)Plugin.PlayerState.Sex;
        SetStatus("Reading your plate...");
        capturing = Plugin.Framework.RunOnFrameworkThread(() => plugin.Reader.Capture(owner, race, tribe, sex));
    }

    private void FinishCapture(ulong owner)
    {
        if (opening is { IsCompleted: true } open)
        {
            SetStatus(open.IsCompletedSuccessfully ? open.Result ?? "" : "Couldn't open your plate.");
            opening = null;
        }
        if (capturing is not { IsCompleted: true } done) return;
        capturing = null;
        if (!done.IsCompletedSuccessfully)
        {
            SetStatus("Couldn't read your plate.", true);
            Plugin.Log.Error(done.Exception!, "Reading the plate failed");
            return;
        }
        SetStatus(done.Result.Message, done.Result.Preset == null);
        if (done.Result.Preset is not { } preset || preset.Owner != owner) return;

        var name = PlatePreset.CleanName(newName);
        preset.Name = name.Length > 0 ? name : $"Plate {preset.CreatedAt.ToLocalTime():yyyy-MM-dd HH:mm}";
        plugin.Store.Add(preset);
        plugin.Store.Save();
        newName = "";
        filter = "";
        Select(preset.Id);
    }

    private void StartApply(PlatePreset preset)
    {
        var owner = plugin.CharacterId;
        applied = preset;
        SetStatus("Applying...");
        applying = Plugin.Framework.RunOnFrameworkThread(() => plugin.Editor.Apply(preset, owner));
    }

    // Apply, wait for the pose to settle, then read the editor back.
    private void FinishApply(ulong owner)
    {
        if (applying is { IsCompleted: true } apply)
        {
            applying = null;
            if (!apply.IsCompletedSuccessfully)
            {
                SetStatus("Couldn't apply the portrait.", true);
                Plugin.Log.Error(apply.Exception!, "Applying the portrait failed");
                applied = null;
                return;
            }
            SetStatus(apply.Result.Message, !apply.Result.Applied);
            if (apply.Result.Applied) checkAt = DateTime.UtcNow + CheckDelay;
            else applied = null;
        }

        if (applied is { } preset && checking == null && applying == null && DateTime.UtcNow >= checkAt)
            checking = Plugin.Framework.RunOnFrameworkThread(() => plugin.Editor.Verify(preset, owner));

        if (checking is not { IsCompleted: true } check) return;
        checking = null;
        if (!check.IsCompletedSuccessfully)
        {
            SetStatus("Couldn't check the editor.", true);
            Plugin.Log.Error(check.Exception!, "Checking the portrait failed");
        }
        else
        {
            var result = check.Result;
            var otherBody = applied is { } p && (p.Race != Plugin.PlayerState.Race.RowId || p.Sex != (byte)Plugin.PlayerState.Sex);
            var note = result.Applied && otherBody ? " It was saved on a different race, so check the framing." : "";
            SetStatus(result.Message + note, !result.Clean || note.Length > 0);
        }
        applied = null;
    }

    // A design apply and a restore both run over a few seconds; show how far they got, then the outcome.
    private void FinishRuns()
    {
        if (starting is { IsCompleted: true } start)
        {
            starting = null;
            if (!start.IsCompletedSuccessfully)
            {
                SetStatus("Couldn't start.", true);
                Plugin.Log.Error(start.Exception!, "Starting failed");
            }
            else if (!start.Result.Applied) SetStatus(start.Result.Message, true);
        }
        if (plugin.Designs.Running) SetStatus(plugin.Designs.Progress);
        if (plugin.Restore.Running) SetStatus(plugin.Restore.Progress);
        if (plugin.Designs.TakeResult() is { } result) SetStatus(result.Message, !result.Clean);
        if (plugin.Restore.TakeResult() is { } restored) SetStatus(restored.Message, !restored.Clean);
    }

    private bool Busy => plugin.Designs.Running || plugin.Restore.Running || starting != null || applying != null || applied != null;

    private void StartRun(Func<ApplyResult> start, string status)
    {
        SetStatus(status);
        starting = Plugin.Framework.RunOnFrameworkThread(start);
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
            if (preset.Favorite) DrawStar();
            if (ImGui.Selectable(preset.Name, preset.Id == selected)) Select(preset.Id);
            DrawListMenu(preset);
            var parts = (preset.Portrait != null ? "Portrait" : "") + (preset.Portrait != null && preset.Design != null ? " + " : "") +
                        (preset.Design != null ? "Design" : "");
            using (ImRaii.PushIndent())
                ImGui.TextDisabled($"{Names.Race(preset.Race, preset.Sex)} · {preset.UpdatedAt.ToLocalTime():MM-dd-yyyy} · {parts}" +
                                   (preset.Imported ? " · Shared" : ""));
        }
    }

    // A small star before a favourite's name, a little under the text's size and centred on the line.
    private static void DrawStar()
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

    // Right-click a plate in the list.
    private void DrawListMenu(PlatePreset preset)
    {
        using var menu = ImRaii.ContextPopupItem("##plateMenu");
        if (!menu.Success) return;
        using (ImRaii.Disabled(!plugin.Store.CanWrite))
        {
            if (ImGui.MenuItem(preset.Favorite ? "Remove from favorites" : "Add to favorites"))
            {
                // Not an edit to the plate itself, so it keeps its place among the others by date.
                preset.Favorite = !preset.Favorite;
                plugin.Store.Save();
            }
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
        DrawActions(preset);
        ImGui.Spacing();
        ImGui.Separator();
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

    private void DrawActions(PlatePreset preset)
    {
        var owner = plugin.CharacterId;
        if (plugin.Designs.Running || plugin.Restore.Running)
        {
            if (Theme.DangerButton("Stop"))
                Plugin.Framework.RunOnFrameworkThread(() => plugin.StopAll("Stopped. Anything not saved yet can be undone by closing the editor without saving."));
            return;
        }

        using (ImRaii.Disabled(Busy))
        {
            if (Theme.PrimaryButton("Restore"))
                StartRun(() => plugin.Restore.Start(preset, owner), "Restoring...");
            plugin.Guide.Mark(GuideTarget.Restore);
            Ui.TipAlways("Puts this portrait and plate design back on your plate and saves them. Parts that already match are skipped.");
            ImGui.SameLine();
            // The same without saving, for looking it over first.
            using (ImRaii.Disabled(preset.Portrait == null))
            {
                if (ImGui.Button("Apply Portrait")) StartApply(preset);
            }
            Ui.TipAlways("Puts this portrait into Edit Portrait without saving. Open it from your adventurer plate first, then press Save there when it looks right.");
            ImGui.SameLine();
            using (ImRaii.Disabled(preset.Design == null))
            {
                if (ImGui.Button("Apply Plate Design"))
                    StartRun(() => plugin.Designs.Start(preset, owner), "Applying the design...");
            }
            Ui.TipAlways("Picks this design in Edit Plate Design without saving. Open it from your adventurer plate first, then press Save there when it looks right.");
        }
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
        // The buttons sit at the right edge, or straight after a name too long to leave room.
        var style = ImGui.GetStyle();
        string[] buttons = confirmDelete ? ["Rename", "Share", "Yes, delete", "Keep"] : ["Rename", "Share", "Delete"];
        var width = buttons.Sum(b => ImGui.CalcTextSize(b).X + style.FramePadding.X * 2) + style.ItemSpacing.X * (buttons.Length - 1);
        if (confirmDelete) width += ImGui.CalcTextSize(DeleteQuestion).X + style.ItemSpacing.X;
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - width));
        using (ImRaii.Disabled(!plugin.Store.CanWrite))
        {
            if (ImGui.SmallButton("Rename"))
            {
                rename = preset.Name;
                renaming = true;
                confirmDelete = false;
            }
            ImGui.SameLine();
            if (ImGui.SmallButton("Share"))
            {
                ImGui.SetClipboardText(ShareCode.Encode(preset));
                SetStatus("Share code copied. Paste it wherever you like.");
            }
            Ui.Tip("Copies a code anyone with RePlate can import. It holds this plate's portrait and design, " +
                   "not who you are, and not its picture.");
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
                    selected = Guid.Empty;
                    return;
                }
                ImGui.SameLine();
                if (ImGui.SmallButton("Keep")) confirmDelete = false;
            }
        }
        ImGui.TextDisabled(preset.Imported
            ? $"Shared, made on {Names.Race(preset.Race, preset.Sex)} - Added {preset.CreatedAt.ToLocalTime():MM-dd-yyyy}"
            : $"{Names.Race(preset.Race, preset.Sex)} - Created {preset.CreatedAt.ToLocalTime():MM-dd-yyyy}");
    }

    // Paste a code, see what it holds, add it as one of this character's plates.
    private void DrawImport(ulong owner)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(600, 0) * scale, ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(ImportPopup, ImGuiWindowFlags.AlwaysAutoResize)) return;

        ImGui.TextUnformatted("Paste a RePlate share code:");
        if (ImGui.InputTextMultiline("##code", ref importCode, 5000, new Vector2(560 * scale, 70 * scale)))
            ReadImport();
        if (ImGui.Button("Paste from clipboard"))
        {
            importCode = ImGui.GetClipboardText() ?? "";
            ReadImport();
        }
        if (importProblem.Length > 0) ImGui.TextColored(Theme.Warning, importProblem);

        if (importPreview is { } shared)
        {
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) ImGui.TextUnformatted(shared.Name.Length > 0 ? shared.Name : "Shared plate");
            if (Names.Race(shared.Race, shared.Sex) is { Length: > 0 } race) ImGui.TextDisabled($"Made on {race}");
            using (var table = ImRaii.Table("##preview", 2, ImGuiTableFlags.SizingStretchSame))
            {
                if (table.Success)
                {
                    ImGui.TableNextColumn();
                    DrawPortrait(shared.Portrait);
                    ImGui.TableNextColumn();
                    DrawDesign(shared.Design);
                }
            }
            ImGui.Spacing();
            using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted))
                ImGui.TextWrapped("Anything this character hasn't unlocked keeps your own choice. Restore stops before saving " +
                                  "so you can look it over; on a different race the camera may need a nudge.");
        }

        ImGui.Separator();
        using (ImRaii.Disabled(importPreview == null || !plugin.Store.CanWrite))
        {
            if (Theme.PrimaryButton("Add to my plates") && importPreview != null)
            {
                var preset = ShareCode.ToPreset(importPreview, owner);
                plugin.Store.Add(preset);
                plugin.Store.Save();
                filter = "";
                Select(preset.Id);
                SetStatus($"Added \"{preset.Name}\".");
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void ReadImport()
    {
        importPreview = importCode.Trim().Length == 0 ? null : ShareCode.Decode(importCode, out importProblem);
        if (importCode.Trim().Length == 0) importProblem = "";
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

    private void Select(Guid id)
    {
        if (id == selected) return;
        selected = id;
        renaming = false;
        confirmDelete = false;
        images.Select(id);
    }
}
