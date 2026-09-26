using System;
using Dalamud.Configuration;
using RePlate.Windows;

namespace RePlate;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public bool WelcomeSeen { get; set; }

    /// <summary>The guided tour's current step, or -1 when it isn't running.</summary>
    public int TourStep { get; set; } = -1;

    /// <summary>The same for the Portraits tab's tour.</summary>
    public int PortraitTourStep { get; set; } = -1;

    /// <summary>The Portraits tour has been offered once, on first opening that tab.</summary>
    public bool PortraitTourOffered { get; set; }

    /// <summary>Restore stops with the editor open, before Save, so you can look it over and save it yourself.</summary>
    public bool PauseBeforeSave { get; set; } = true;

    /// <summary>A button above your plate's edit menu that opens RePlate.</summary>
    public bool ShowPlateToolbar { get; set; } = true;

    public bool UseTheme { get; set; } = true;
    public AccentChoice Accent { get; set; } = AccentChoice.GilGold;

    /// <summary>0xRRGGBB, used when <see cref="Accent"/> is Custom.</summary>
    public uint CustomAccent { get; set; } = 0xE0A63A;

    public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
