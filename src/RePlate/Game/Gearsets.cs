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

    /// <summary>Opens a gear set's Edit Portrait, as its "Edit Portrait" option in the Gear Set list does.</summary>
    public static string? OpenEditor(GearsetInfo gearset)
    {
        var agent = AgentBannerEditor.Instance();
        if (agent == null) return "Edit Portrait isn't available right now.";
        agent->OpenForGearset(gearset.EnabledIndex);
        return null;
    }
}
