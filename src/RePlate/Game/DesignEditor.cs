using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;
using Lumina.Excel.Sheets;
using RePlate.Core.Plates;

namespace RePlate.Game;

/// <summary>
/// Puts a saved design into the open Edit Plate Design window by stepping each list with its own arrow buttons,
/// one click at a time, like a player would. Nothing is saved; the player presses Save or closes the window.
/// Start and Update run on the framework thread.
/// </summary>
public sealed unsafe class DesignEditor
{
    private const string Window = "CharaCardDesignSetting";
    private const int FlipButton = 27;
    private const int KeepPicker = 28;
    private const int CancelPicker = 29;
    private const uint PickerList = 10;
    private const int MaxClicks = 400;
    private static readonly TimeSpan ClickGap = TimeSpan.FromMilliseconds(60);

    // The window's parts in the order its buttons use: part n's back arrow is button n, its next arrow n + 9, and the
    // button that opens its picker n + 18.
    private static readonly Part[] Parts =
    [
        new("base plate", 30, 0), new("top border", 48, 0), new("bottom border", 54, 0),
        new("backing", 42, 1), new("pattern overlay", 36, 2), new("portrait frame", 60, 3),
        new("plate frame", 66, 4), new("accent", 72, 5),
    ];

    private sealed record Part(string Name, uint Dropdown, byte Kind);

    private PlateDesign? target;
    private ulong owner;
    private int[] tried = new int[Parts.Length];
    private bool[] arrowsOnly = new bool[Parts.Length];
    private int pickerPart = -1;
    private bool picked;
    private DateTime openedAt;
    private bool flipped;
    private int clicks;
    private DateTime nextClick;

    public bool Running => target != null;
    public string Progress => $"Applying the design... {clicks} clicks";

    private ApplyResult? finished;
    private bool quiet;

    /// <summary>The outcome of the last run started quietly, for a restore that drives it.</summary>
    public ApplyResult? Last { get; private set; }

    /// <summary>The outcome of the last run, handed over once.</summary>
    public ApplyResult? TakeResult()
    {
        var result = finished;
        finished = null;
        return result;
    }

    public ApplyResult Start(PlatePreset preset, ulong owner, bool quiet = false)
    {
        if (Running) return new(false, "Already applying a design.");
        if (preset.Design is not { } design || preset.Owner != owner) return new(false, "This plate has no design to apply.");
        var problem = GetWindow(owner, out var addon, out var card);
        if (problem != null) return new(false, problem);

        // Check the window reads the way we expect before touching it.
        var wanted = Ids(design.BasePlate, design.TopBorder, design.BottomBorder, design.Decorations);
        var current = Ids(card);
        var locked = new List<string>();
        for (var i = 0; i < Parts.Length; i++)
        {
            var list = List(addon, Parts[i]);
            if (list == null || Label(list, i, SelectedRow(addon, Parts[i])) != NameOf(i, current[i]))
                return new(false, "The design window isn't laid out the way RePlate expects, so nothing was changed.");
            if (Rows(list, i, wanted[i]).Count == 0) locked.Add($"{Parts[i].Name} {NameOf(i, wanted[i])}");
        }
        if (locked.Count > 0) return new(false, $"Not unlocked on this character: {string.Join(", ", locked)}.");

        target = design;
        this.owner = owner;
        tried = new int[Parts.Length];
        arrowsOnly = new bool[Parts.Length];
        pickerPart = -1;
        flipped = false;
        clicks = 0;
        nextClick = DateTime.UtcNow;
        finished = null;
        Last = null;
        this.quiet = quiet;
        return new(true, Progress);
    }

    public void Stop(string message) => Finish(new ApplyResult(false, message));

    public void Update()
    {
        if (target == null || DateTime.UtcNow < nextClick) return;
        var problem = GetWindow(owner, out var addon, out var card);
        if (problem != null)
        {
            Stop("Edit Plate Design closed before the design was finished. Nothing was saved.");
            return;
        }
        if (clicks >= MaxClicks)
        {
            Stop("That took more clicks than expected, so RePlate stopped. Check the design or close without saving.");
            return;
        }

        var wanted = Ids(target.BasePlate, target.TopBorder, target.BottomBorder, target.Decorations);
        var current = Ids(card);

        // A picker that got its part right is closed with its keep button before moving on.
        if (pickerPart >= 0 && current[pickerPart] == wanted[pickerPart])
        {
            pickerPart = -1;
            Click(addon, KeepPicker);
            return;
        }

        for (var i = 0; i < Parts.Length; i++)
        {
            if (current[i] == wanted[i]) continue;
            var list = List(addon, Parts[i]);
            var rows = list == null ? [] : Rows(list, i, wanted[i]);
            if (tried[i] >= rows.Count)
            {
                Stop($"Couldn't pick the {Parts[i].Name}. Nothing was saved; close the window without saving to undo.");
                return;
            }
            if (!arrowsOnly[i])
            {
                UsePicker(addon, i, rows[tried[i]], list->GetItemCount());
                return;
            }
            // Two items can share a name: if we're on the row and it's the wrong one, go on to the next match.
            var row = SelectedRow(addon, Parts[i]);
            if (row == rows[tried[i]]) tried[i]++;
            if (tried[i] >= rows.Count) continue;
            Click(addon, rows[tried[i]] > row ? i + 10 : i + 1);
            return;
        }

        if (card->InvertPortraitPlacement != target.InvertPortraitPlacement)
        {
            if (flipped)
            {
                Stop("The portrait side didn't change. Nothing was saved.");
                return;
            }
            flipped = true;
            Click(addon, FlipButton);
            return;
        }

        var name = string.Join(", ", Parts.Where((_, i) => current[i] != wanted[i]).Select(p => p.Name));
        Finish(name.Length > 0
            ? new ApplyResult(false, $"Some parts didn't take: {name}. Nothing was saved; close without saving to undo.")
            : new ApplyResult(true, "Design applied. Look it over and press Save in Edit Plate Design.", true));
    }

    // The part's picker lists every item, greyed out when you don't have it, in the same order as its dropdown. So the
    // dropdown's row n is the picker's nth row that isn't greyed out. One click there sets the part.
    private void UsePicker(AtkUnitBase* addon, int part, int dropdownRow, int dropdownCount)
    {
        if (pickerPart != part)
        {
            pickerPart = part;
            picked = false;
            openedAt = DateTime.UtcNow;
            Click(addon, part + 19);
            // Let it fill in before reading it, so an earlier picker's rows aren't mistaken for this one's.
            nextClick = DateTime.UtcNow + TimeSpan.FromMilliseconds(150);
            return;
        }

        var picker = Picker(addon);
        if (picked)
        {
            // Still the wrong item after the click: undo it and step there with the arrows instead.
            UseArrows(addon, part, "the pick didn't take");
            return;
        }
        if (picker == null)
        {
            if (DateTime.UtcNow - openedAt < TimeSpan.FromMilliseconds(500))
            {
                nextClick = DateTime.UtcNow + TimeSpan.FromMilliseconds(50);
                return;
            }
            UseArrows(addon, part, "the picker didn't open");
            return;
        }

        var enabled = new List<int>();
        for (var row = 0; row < picker->ListLength; row++)
            if (!picker->ItemRendererList[row].IsDisabled) enabled.Add(row);
        if (enabled.Count != dropdownCount)
        {
            UseArrows(addon, part, $"the picker has {enabled.Count} items and the list {dropdownCount}");
            return;
        }
        picked = true;
        picker->DispatchItemEvent(enabled[dropdownRow], AtkEventType.ListItemClick);
        clicks++;
        nextClick = DateTime.UtcNow + ClickGap;
    }

    private void UseArrows(AtkUnitBase* addon, int part, string why)
    {
        Plugin.Log.Information($"The {Parts[part].Name} picker didn't work ({why}); using the arrows.");
        arrowsOnly[part] = true;
        pickerPart = -1;
        // Cancel undoes anything picked in it.
        if (Picker(addon) != null) Click(addon, CancelPicker);
    }

    private static AtkComponentList* Picker(AtkUnitBase* addon)
    {
        var node = addon->GetComponentNodeById(PickerList);
        if (node == null || node->Component == null || node->Component->GetComponentType() != ComponentType.List ||
            !node->AtkResNode.IsVisible())
            return null;
        var list = (AtkComponentList*)node->Component;
        return list->ListLength > 0 ? list : null;
    }

    private void Finish(ApplyResult result)
    {
        if (target != null) Plugin.Log.Information($"Design apply ended after {clicks} clicks: {result.Message}");
        target = null;
        if (quiet) Last = result;
        else finished = result;
    }

    /// <summary>The parts of your plate's current design that differ from this one, by name.</summary>
    public static List<string> Differences(AgentCharaCard.Storage* card, PlateDesign design)
    {
        var wanted = Ids(design.BasePlate, design.TopBorder, design.BottomBorder, design.Decorations);
        var current = Ids(card);
        var parts = Parts.Where((_, i) => current[i] != wanted[i]).Select(p => p.Name).ToList();
        if (card->InvertPortraitPlacement != design.InvertPortraitPlacement) parts.Add("layout");
        return parts;
    }

    private void Click(AtkUnitBase* addon, int param)
    {
        if (!Clicks.ButtonWithParam(addon, param))
        {
            Stop("A button in the design window wasn't available, so RePlate stopped. Nothing was saved.");
            return;
        }
        clicks++;
        nextClick = DateTime.UtcNow + ClickGap;
    }

    private static string? GetWindow(ulong owner, out AtkUnitBase* addon, out AgentCharaCard.Storage* card)
    {
        addon = null;
        if (PlateReader.GetOwnCard(owner, out card) is { } problem) return problem;
        var window = Plugin.GameGui.GetAddonByName(Window);
        if (window.IsNull || !window.IsVisible) return "Open Edit Plate Design from your adventurer plate first.";
        if (!window.IsReady) return "Edit Plate Design is still opening.";
        var dialog = Plugin.GameGui.GetAddonByName("SelectYesno");
        if (!dialog.IsNull && dialog.IsVisible) return "Answer the dialog first.";
        addon = (AtkUnitBase*)window.Address;
        return null;
    }

    // The part ids in Parts order; decorations are found by their kind, 0 when the plate has none of that kind.
    private static uint[] Ids(AgentCharaCard.Storage* card)
    {
        var d = card->PlateDesign;
        var decorations = new List<ushort>();
        for (var i = 0; i < d.NumDecorations && i < d.Decorations.Length; i++) decorations.Add(d.Decorations[i]);
        return Ids(d.BasePlate, d.TopBorder, d.BottomBorder, decorations);
    }

    private static uint[] Ids(ushort basePlate, byte top, byte bottom, IEnumerable<ushort> decorations)
    {
        var ids = new uint[Parts.Length];
        ids[0] = basePlate;
        ids[1] = top;
        ids[2] = bottom;
        var sheet = Plugin.DataManager.GetExcelSheet<CharaCardDecoration>();
        foreach (var id in decorations)
        {
            if (id == 0 || sheet.GetRowOrDefault(id) is not { } row) continue;
            var part = Array.FindIndex(Parts, p => p.Kind == row.Component && p.Kind != 0);
            if (part >= 0) ids[part] = id;
        }
        return ids;
    }

    // None is the first row of every list but the base plates, and its label is translated, so it goes by position.
    private const string None = "(none)";

    // The item's name as the window lists it.
    private static string? NameOf(int part, uint id)
    {
        if (id == 0) return part == 0 ? null : None;
        var name = part switch
        {
            0 => Plugin.DataManager.GetExcelSheet<CharaCardBase>().GetRowOrDefault(id)?.Name.ExtractText(),
            1 or 2 => Plugin.DataManager.GetExcelSheet<CharaCardHeader>().GetRowOrDefault(id)?.Name.ExtractText(),
            _ => Plugin.DataManager.GetExcelSheet<CharaCardDecoration>().GetRowOrDefault(id)?.Name.ExtractText(),
        };
        return name == null ? null : Clean(name);
    }

    private static List<int> Rows(AtkComponentList* list, int part, uint id)
    {
        var rows = new List<int>();
        var name = NameOf(part, id);
        if (name == null) return rows;
        for (var row = 0; row < list->GetItemCount(); row++)
            if (!list->GetItemDisabledState(row) && Label(list, part, row) == name) rows.Add(row);
        return rows;
    }

    private static AtkComponentList* List(AtkUnitBase* addon, Part part)
    {
        var dropdown = Dropdown(addon, part);
        return dropdown == null ? null : dropdown->List;
    }

    private static int SelectedRow(AtkUnitBase* addon, Part part)
    {
        var dropdown = Dropdown(addon, part);
        return dropdown == null ? -1 : dropdown->GetSelectedItemIndex();
    }

    private static AtkComponentDropDownList* Dropdown(AtkUnitBase* addon, Part part)
    {
        var node = addon->GetComponentNodeById(part.Dropdown);
        if (node == null || node->Component == null || node->Component->GetComponentType() != ComponentType.DropDownList) return null;
        return (AtkComponentDropDownList*)node->Component;
    }

    private static string Label(AtkComponentList* list, int part, int row)
    {
        if (row < 0 || row >= list->GetItemCount()) return "";
        if (row == 0 && part != 0) return None;
        return Clean(Encoding.UTF8.GetString(list->GetItemLabel(row).AsSpan()));
    }

    private static string Clean(string text) => new string(text.Where(c => !char.IsControl(c)).ToArray()).Trim();
}
