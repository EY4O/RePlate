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
public sealed class PlatesTab
{
    // Long enough for the pose to load before the editor is read back.
    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(1);

    private readonly Plugin plugin;
    private readonly PlateImages images;
    private readonly PresetHeader header;
    private readonly ImportPopup import;

    private Task<CaptureResult>? capturing;
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
    private Guid selected;

    public PlatesTab(Plugin plugin, PlateImages images)
    {
        this.plugin = plugin;
        this.images = images;
        header = new PresetHeader(plugin, images, text => SetStatus(text));
        import = new ImportPopup(plugin);
    }

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
        if (import.Draw(owner) is { } added)
        {
            if (added.Kind == PresetKind.Portrait) SetStatus($"Added \"{added.Name}\" to your portraits.");
            else
            {
                filter = "";
                Select(added.Id);
                SetStatus($"Added \"{added.Name}\".");
            }
        }
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
        if (ImGui.Button("Import")) import.Open();
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

    private bool Busy => plugin.Designs.Running || plugin.Restore.Running || plugin.GearsetRun.Running ||
                         starting != null || applying != null || applied != null;

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
            if (preset.Favorite) Summary.Star();
            if (ImGui.Selectable(preset.Name, preset.Id == selected)) Select(preset.Id);
            header.Menu(preset, () => Select(preset.Id));
            var parts = (preset.Portrait != null ? "Portrait" : "") + (preset.Portrait != null && preset.Design != null ? " + " : "") +
                        (preset.Design != null ? "Design" : "");
            using (ImRaii.PushIndent())
                ImGui.TextDisabled($"{Names.Race(preset.Race, preset.Sex)} · {preset.UpdatedAt.ToLocalTime():MM-dd-yyyy} · {parts}" +
                                   (preset.Imported ? " · Shared" : ""));
        }
    }

    private void DrawDetail(PlatePreset? preset)
    {
        if (preset == null)
        {
            ImGui.TextDisabled("Select a plate.");
            return;
        }

        var subtitle = preset.Imported
            ? $"Shared, made on {Names.Race(preset.Race, preset.Sex)} - Added {preset.CreatedAt.ToLocalTime():MM-dd-yyyy}"
            : $"{Names.Race(preset.Race, preset.Sex)} - Created {preset.CreatedAt.ToLocalTime():MM-dd-yyyy}";
        if (header.Draw(preset, subtitle))
        {
            selected = Guid.Empty;
            return;
        }
        ImGui.Spacing();
        DrawActions(preset);
        ImGui.Spacing();
        ImGui.Separator();
        using (var table = ImRaii.Table("##summary", 2, ImGuiTableFlags.SizingStretchSame))
        {
            if (table.Success)
            {
                ImGui.TableNextColumn();
                Summary.Portrait(preset.Portrait);
                ImGui.TableNextColumn();
                Summary.Design(preset.Design);
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
            Ui.TipAlways(plugin.Configuration.PauseBeforeSave
                ? "Opens your plate's editors and puts this portrait, then this design, in for you to look over and save. " +
                  "Press Restore again after saving for the next part. Parts that already match are skipped."
                : "Puts this portrait and plate design back on your plate and saves them. Parts that already match are skipped.");
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

    private void Select(Guid id)
    {
        if (id == selected) return;
        selected = id;
        header.Reset();
        images.Select(id);
    }
}
