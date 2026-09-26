using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using RePlate.Core.Plates;

namespace RePlate.Windows;

public sealed class SettingsWindow : ThemedWindow
{
    private readonly Plugin plugin;
    private readonly FileDialogManager backups = new();
    private Task<string>? working;
    private BackupContents? pending;
    private HaselTweaksPortraits? pendingHasel;
    private string backupStatus = "";

    public SettingsWindow(Plugin plugin) : base("RePlate Settings###RePlateSettings", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(540, 470);
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
            if (grid.Success)
                changed |= Row("Button above your plate",
                    "Shows a RePlate button above the top-right of your adventurer plate, to open RePlate from there.",
                    () => Checkbox("##plateToolbar", c.ShowPlateToolbar, v => c.ShowPlateToolbar = v));
        }

        DrawLibrary();

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

    // Backups: every plate and portrait with their pictures in one zip, for a new PC or another character.
    private void DrawLibrary()
    {
        backups.Draw();
        Theme.Section("Your library");
        FinishBackup();
        var owner = plugin.CharacterId;
        using (ImRaii.Disabled(working != null || owner == 0))
        {
            if (ImGui.Button("Export this character")) Export(owner);
            Ui.TipAlways("Saves this character's plates and portraits, with their pictures, as one file.");
            ImGui.SameLine();
            if (ImGui.Button("Export every character")) Export(null);
            Ui.TipAlways("The same for every character you've used RePlate on.");
            ImGui.SameLine();
            if (ImGui.Button("Import a backup..."))
                backups.OpenFileDialog("Import a RePlate backup", ".zip", (ok, path) =>
                {
                    if (ok) Start("Reading the backup...", Task.Run(() => ReadBackup(path)));
                });
            if (Plugin.HaselTweaksLoaded())
            {
                if (ImGui.Button("Bring over HaselTweaks portraits"))
                {
                    pending = null;
                    Start("Reading HaselTweaks' portraits...", Task.Run(ReadHaselTweaks));
                }
                Ui.TipAlways("Adds the portraits saved in HaselTweaks' Portrait Helper, with their pictures, to this character. " +
                             "HaselTweaks' own presets aren't changed.");
            }
        }

        if (pendingHasel is { } hasel) DrawHaselTweaks(hasel, owner);
        if (pending is { } backup)
        {
            var plates = backup.Presets.Count(p => p.Kind == PresetKind.Plate);
            var portraits = backup.Presets.Count - plates;
            var characters = backup.Presets.Select(p => p.Owner).Distinct().Count();
            ImGui.TextWrapped($"This backup has {plates} plate{(plates == 1 ? "" : "s")} and {portraits} portrait" +
                              $"{(portraits == 1 ? "" : "s")}, with {backup.Pictures.Count} picture{(backup.Pictures.Count == 1 ? "" : "s")}, " +
                              $"from {characters} character{(characters == 1 ? "" : "s")}. They'll all be added to this character.");
            using (ImRaii.Disabled(owner == 0 || !plugin.Store.CanWrite))
            {
                if (Theme.PrimaryButton("Add to this character"))
                {
                    var contents = backup;
                    pending = null;
                    Start("Adding...", Plugin.Framework.RunOnFrameworkThread(() =>
                    {
                        var result = LibraryBackup.AddTo(plugin.Store, plugin.Pictures, contents, owner);
                        plugin.Store.Save();
                        return result.AlreadyHere > 0
                            ? $"Added {result.Added}; {result.AlreadyHere} were already here."
                            : $"Added {result.Added}.";
                    }));
                }
            }
            ImGui.SameLine();
            if (ImGui.Button("Cancel")) pending = null;
        }
        if (backupStatus.Length > 0) ImGui.TextDisabled(backupStatus);
    }

    private void Export(ulong? owner)
    {
        var presets = plugin.Store.All(owner);
        if (presets.Count == 0)
        {
            backupStatus = "There's nothing saved to export yet.";
            return;
        }
        backups.SaveFileDialog("Export a RePlate backup", ".zip", $"RePlate backup {DateTime.Now:yyyy-MM-dd}", ".zip", (ok, path) =>
        {
            if (!ok) return;
            Start("Exporting...", Task.Run(async () =>
            {
                await using (var file = File.Create(path + ".tmp"))
                    await LibraryBackup.ExportAsync(file, presets, plugin.Pictures, CancellationToken.None);
                File.Move(path + ".tmp", path, true);
                return $"Saved {presets.Count} to {Path.GetFileName(path)}.";
            }));
        });
    }

    private string ReadBackup(string path)
    {
        using var file = File.OpenRead(path);
        pendingHasel = null;
        pending = LibraryBackup.Read(file);
        return "";
    }

    // HaselTweaks keeps its settings beside RePlate's, in the folder every plugin's settings share.
    private string ReadHaselTweaks()
    {
        var pluginConfigs = new DirectoryInfo(Plugin.PluginInterface.GetPluginConfigDirectory()).Parent!.FullName;
        var found = HaselTweaksLibrary.Read(pluginConfigs);
        if (found.Contents.Presets.Count == 0)
            return found.Unreadable > 0
                ? $"HaselTweaks has {found.Unreadable} saved portrait{(found.Unreadable == 1 ? "" : "s")}, but none RePlate could read."
                : "HaselTweaks has no saved portraits.";
        pendingHasel = found;
        return "";
    }

    private void DrawHaselTweaks(HaselTweaksPortraits found, ulong owner)
    {
        var count = found.Contents.Presets.Count;
        var pictures = found.Contents.Pictures.Count;
        ImGui.TextWrapped($"HaselTweaks has {count} portrait{(count == 1 ? "" : "s")} saved, with {pictures} picture" +
                          $"{(pictures == 1 ? "" : "s")}. They'll be added to this character's Portraits; any it already has " +
                          "are left out." + (found.Unreadable > 0 ? $" {found.Unreadable} couldn't be read and stay behind." : ""));
        using (ImRaii.Disabled(owner == 0 || !plugin.Store.CanWrite))
        {
            if (Theme.PrimaryButton("Add to this character##hasel"))
            {
                var contents = found.Contents;
                pendingHasel = null;
                Start("Adding...", Plugin.Framework.RunOnFrameworkThread(() =>
                {
                    var result = HaselTweaksLibrary.AddTo(plugin.Store, plugin.Pictures, contents, owner);
                    plugin.Store.Save();
                    return result.AlreadyHere > 0
                        ? $"Added {result.Added} to Portraits; {result.AlreadyHere} were already here."
                        : $"Added {result.Added} to Portraits.";
                }));
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel##hasel")) pendingHasel = null;
    }

    private void Start(string status, Task<string> work)
    {
        backupStatus = status;
        working = work;
    }

    private void FinishBackup()
    {
        if (working is not { IsCompleted: true } done) return;
        working = null;
        if (done.IsCompletedSuccessfully) backupStatus = done.Result;
        else
        {
            var error = done.Exception?.InnerException;
            backupStatus = error is InvalidDataException or IOException or UnauthorizedAccessException
                ? error.Message
                : "That didn't work; see /xllog.";
            if (error is not InvalidDataException) Plugin.Log.Warning(error!, "Backup failed");
        }
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
