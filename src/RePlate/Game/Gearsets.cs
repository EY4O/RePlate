using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using RePlate.Core.Plates;

namespace RePlate.Game;

/// <summary>A gear set as the portrait tools see it. EnabledIndex is its place in the game's list of existing sets.</summary>
public sealed record GearsetInfo(int Id, int EnabledIndex, string Name, byte ClassJob, uint Icon, PortraitSettings? Portrait);

/// <summary>Your gear sets and their saved portraits. Call from the framework thread.</summary>
public static unsafe class Gearsets
{
    public static List<GearsetInfo> List(ulong owner)
    {
        var result = new List<GearsetInfo>();
        var module = RaptureGearsetModule.Instance();
        if (owner == 0 || module == null || module->CharacterContentId != owner) return result;
        for (var enabled = 0; enabled < module->NumGearsets; enabled++)
        {
            var id = module->ResolveIdFromEnabledIndex((byte)enabled);
            if (id < 0 || !module->IsValidGearset(id)) continue;
            var entry = module->GetGearset(id);
            if (entry == null || !entry->Flags.HasFlag(RaptureGearsetModule.GearsetFlag.Exists)) continue;
            var banner = entry->GetBanner();
            var portrait = banner == null ? null : PortraitData.FromBanner(banner);
            result.Add(new GearsetInfo(id, enabled, entry->NameString, entry->ClassJob,
                (uint)module->GetClassJobIconForGearset(id), portrait is { } p && p.IsValid() ? p : null));
        }
        return result;
    }

    /// <summary>Opens a gear set's Edit Portrait through the portrait editor's own opener.</summary>
    public static string? OpenEditor(GearsetInfo gearset)
    {
        var agent = AgentBannerEditor.Instance();
        if (agent == null) return "Edit Portrait isn't available right now.";
        agent->OpenForGearset(gearset.EnabledIndex);
        return null;
    }

    /// <summary>Opens the game's Portraits window, as the character menu does.</summary>
    public static string? OpenPortraitsWindow()
    {
        var pointer = Plugin.GameGui.GetAgentById((int)AgentId.BannerList);
        if (pointer.IsNull) return "The Portraits window isn't available right now.";
        var agent = (AgentInterface*)pointer.Address;
        if (!agent->IsAgentActive()) agent->Show();
        return null;
    }

    /// <summary>The same through the Gear Set list's own "Edit Portrait", for when the first way doesn't open it.</summary>
    public static string? OpenEditorFromList(GearsetInfo gearset)
    {
        var agent = AgentGearSet.Instance();
        if (agent == null) return "The Gear Set list isn't available right now.";
        agent->EditPortrait(gearset.Id);
        return null;
    }
}
