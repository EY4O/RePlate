using System;
using System.Collections.Generic;
using System.Globalization;
using Dalamud.Game.Addon.Events;
using Dalamud.Game.Addon.Lifecycle;
using Dalamud.Game.Addon.Lifecycle.AddonArgTypes;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Component.GUI;

namespace RePlate.Game;

/// <summary>
/// Watches the plate's own windows while you use them by hand and writes what happened to the log. It never clicks
/// or changes anything. Used to learn how the design editor's lists map to designs, and how Save and the dialogs work.
/// </summary>
public sealed unsafe class UiProbe : IDisposable
{
    private static readonly string[] Addons =
        ["CharaCard", "CharaCardEditMenu", "CharaCardDesignSetting", "BannerEditor", "ContextMenu", "SelectYesno", "SelectOk"];

    // Hover traffic says nothing about what a click does.
    private static readonly HashSet<AddonEventType> Ignored =
    [
        AddonEventType.MouseOver, AddonEventType.MouseOut, AddonEventType.MouseMove,
        AddonEventType.ListItemRollOver, AddonEventType.ListItemRollOut,
        AddonEventType.IconTextRollOver, AddonEventType.IconTextRollOut,
        AddonEventType.DragDropRollOver, AddonEventType.DragDropRollOut,
    ];

    private static readonly AddonEvent[] Events =
        [AddonEvent.PreReceiveEvent, AddonEvent.PostReceiveEvent, AddonEvent.PostSetup, AddonEvent.PostRefresh, AddonEvent.PreFinalize];
    private const int Limit = 600;
    private static readonly TimeSpan Window = TimeSpan.FromMinutes(10);

    private DateTime startedAt;
    private int records;
    private string lastDesign = "";

    public bool Running { get; private set; }

    public void Toggle()
    {
        if (Running)
        {
            Stop("stopped by command");
            return;
        }
        startedAt = DateTime.UtcNow;
        records = 0;
        lastDesign = "";
        Running = true;
        foreach (var type in Events) Plugin.AddonLifecycle.RegisterListener(type, Addons, OnEvent);
        Write("Recording. Nothing is clicked or changed.");
        Plugin.ChatGui.Print("RePlate is recording the plate windows. Use them by hand, then type /replate probe again to stop.");
    }

    public void Update()
    {
        if (!Running) return;
        if (records >= Limit) Stop("event limit reached");
        else if (DateTime.UtcNow - startedAt > Window) Stop("time limit reached");
    }

    public void Dispose()
    {
        if (Running) Stop("plugin unloaded");
    }

    private void Stop(string reason)
    {
        Running = false;
        foreach (var type in Events) Plugin.AddonLifecycle.UnregisterListener(type, Addons, OnEvent);
        Write($"Stopped ({reason}), {records} events.");
        Plugin.ChatGui.Print($"RePlate stopped recording ({reason}). {records} events are in /xllog.");
    }

    private void OnEvent(AddonEvent type, AddonArgs args)
    {
        if (!Running) return;
        try
        {
            var addon = (AtkUnitBase*)args.Addon.Address;
            var design = args.AddonName == "CharaCardDesignSetting";
            // Edit Portrait refreshes every frame, which says nothing.
            if (type == AddonEvent.PostRefresh && args.AddonName == "BannerEditor") return;
            if (type == AddonEvent.PostReceiveEvent)
            {
                // After a design pick: which row each list now shows, and its label.
                if (design && args is AddonReceiveEventArgs after && !Ignored.Contains(after.AtkEventType))
                {
                    Write($"{args.AddonName} after {after.AtkEventType} param={after.EventParam} | {Card()} | {Dropdowns(addon, false)}");
                    // The "Display ... List" buttons open a picker; record what its rows hold.
                    if (after.AtkEventType == AddonEventType.ButtonClick && after.EventParam is >= 19 and <= 26 ||
                        after.AtkEventType == AddonEventType.ListItemClick)
                        Write($"{args.AddonName} picker: {Picker(addon)}");
                }
                return;
            }

            string detail;
            if (args is AddonReceiveEventArgs e)
            {
                if (Ignored.Contains(e.AtkEventType)) return;
                detail = $"{e.AtkEventType} ({(int)e.AtkEventType}) param={e.EventParam}{Row(e)}";
            }
            else detail = type.ToString();

            Write($"{args.AddonName} {detail} | {Card()}");
            if (type == AddonEvent.PostSetup && args.AddonName is "CharaCardDesignSetting" or "CharaCardEditMenu")
            {
                Write($"{args.AddonName} lists: {Lists(addon)}");
                Write($"{args.AddonName} values: {Values(addon)}");
                if (design) Write($"{args.AddonName} dropdowns: {Dropdowns(addon, true)}");
            }
        }
        catch (Exception ex)
        {
            // Never let a recorder disturb the game.
            Running = false;
            foreach (var t in Events) Plugin.AddonLifecycle.UnregisterListener(t, Addons, OnEvent);
            Plugin.Log.Warning(ex, "The probe stopped on an error");
        }
    }

    private void Write(string line)
    {
        records++;
        Plugin.Log.Information($"[Probe +{(DateTime.UtcNow - startedAt).TotalMilliseconds:N0}ms] {line}");
    }

    private static string Row(AddonReceiveEventArgs e)
    {
        if (e.AtkEventData == nint.Zero || e.AtkEventType is not (AddonEventType.ListItemClick or AddonEventType.ListItemDoubleClick or
            AddonEventType.ListItemHighlight))
            return "";
        var data = &((AtkEventData*)e.AtkEventData)->ListItemData;
        return $" row={data->SelectedIndex} hovered={data->HoveredItemIndex3} button={data->MouseButtonId}";
    }

    // Each dropdown in the window: its selected row and that row's label. With labels, the first rows and the
    // disabled count, which shows how rows line up with the game's sheets and whether locked items are listed.
    // The design window's picker list (node 10): each row's icon, label and whether it's greyed out.
    private static string Picker(AtkUnitBase* addon)
    {
        var node = addon->GetComponentNodeById(10);
        if (node == null || node->Component == null || node->Component->GetComponentType() != ComponentType.List) return "none";
        var list = (AtkComponentList*)node->Component;
        var rows = new List<string>();
        for (var i = 0; i < list->ListLength && i < 300; i++)
        {
            var item = &list->ItemRendererList[i];
            var label = item->Label.HasValue ? System.Text.Encoding.UTF8.GetString(item->Label.AsSpan()) : "";
            rows.Add($"{i}:{item->IconId}{(label.Length > 0 ? ":" + Quote(label) : "")}{(item->IsDisabled ? "x" : "")}");
        }
        return $"visible={node->AtkResNode.IsVisible()} selected={list->SelectedItemIndex} rows={list->ListLength} [{string.Join(" ", rows)}]";
    }

    private static string Dropdowns(AtkUnitBase* addon, bool labels)
    {
        if (addon == null) return "none";
        var parts = new List<string>();
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            var component = node == null ? null : node->GetAsAtkComponentNode();
            if (component == null || component->Component == null || component->Component->GetComponentType() != ComponentType.DropDownList)
                continue;
            var list = ((AtkComponentDropDownList*)component->Component)->List;
            if (list == null) continue;
            var count = list->GetItemCount();
            var selected = ((AtkComponentDropDownList*)component->Component)->GetSelectedItemIndex();
            var text = $"#{node->NodeId} sel={selected}/{count} {Label(list, selected)}";
            if (labels)
            {
                var first = new List<string>();
                var disabled = 0;
                for (var row = 0; row < count; row++)
                {
                    if (list->GetItemDisabledState(row)) disabled++;
                    if (row < 12) first.Add($"{row}:{Label(list, row)}");
                }
                text += $" disabled={disabled} [{string.Join(" ", first)}]";
            }
            parts.Add(text);
        }
        return string.Join(" || ", parts);
    }

    private static string Label(AtkComponentList* list, int row) =>
        row < 0 || row >= list->GetItemCount() ? "-" : Quote(list->GetItemLabel(row).ToString());

    // The working design and which edit window the card thinks is open, so each click can be matched to its effect.
    private string Card()
    {
        var agent = AgentCharaCard.Instance();
        if (agent == null || agent->Data == null) return "card: none";
        var card = agent->Data;
        var d = card->PlateDesign;
        var decorations = new List<string>();
        for (var i = 0; i < d.NumDecorations && i < d.Decorations.Length; i++) decorations.Add(d.Decorations[i].ToString());
        var design = $"base={d.BasePlate} top={d.TopBorder} bottom={d.BottomBorder} decorations=[{string.Join(",", decorations)}] " +
                     $"invert={card->InvertPortraitPlacement}";
        var changed = design == lastDesign ? "" : " (changed)";
        lastDesign = design;
        return $"card: edit={card->EditAddonId} {design}{changed}";
    }

    // Every component in the window with its node id and type, and the length of each list.
    private static string Lists(AtkUnitBase* addon)
    {
        if (addon == null) return "none";
        var parts = new List<string>();
        for (var i = 0; i < addon->UldManager.NodeListCount; i++)
        {
            var node = addon->UldManager.NodeList[i];
            var component = node == null ? null : node->GetAsAtkComponentNode();
            if (component == null || component->Component == null) continue;
            var kind = component->Component->GetComponentType();
            var text = $"#{node->NodeId}:{kind}";
            if (kind == ComponentType.List || kind == ComponentType.TreeList)
                text += $"[{((AtkComponentList*)component->Component)->GetItemCount()}]";
            if (!node->IsVisible()) text += "(hidden)";
            parts.Add(text);
        }
        return parts.Count == 0 ? "none" : string.Join(" ", parts);
    }

    // Numbers and short strings from the window's values, which is where the game hands it the list contents.
    private static string Values(AtkUnitBase* addon)
    {
        if (addon == null || addon->AtkValues == null) return "none";
        var parts = new List<string>();
        for (var i = 0; i < addon->AtkValuesCount && parts.Count < 120; i++)
        {
            var value = &addon->AtkValues[i];
            var text = (value->Type & AtkValueType.TypeMask) switch
            {
                AtkValueType.Bool => value->Bool.ToString(),
                AtkValueType.Int => value->Int.ToString(CultureInfo.InvariantCulture),
                AtkValueType.UInt => value->UInt.ToString(CultureInfo.InvariantCulture),
                AtkValueType.String or AtkValueType.ConstString or AtkValueType.ManagedString => Quote(value->String.ToString()),
                _ => null,
            };
            if (text != null) parts.Add($"[{i}]={text}");
        }
        return $"{addon->AtkValuesCount} values: {string.Join(" ", parts)}";
    }

    private static string Quote(string text) => "\"" + (text.Length > 30 ? text[..30] + "..." : text) + "\"";
}
