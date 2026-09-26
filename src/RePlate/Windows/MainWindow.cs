using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace RePlate.Windows;

public sealed class MainWindow : ThemedWindow
{
    private readonly PlatesTab plates;
    private readonly PortraitsTab portraits;
    private readonly AboutTab about;
    private bool showPlates;
    private bool showPortraits;

    public MainWindow(Plugin plugin, PlatesTab plates, PortraitsTab portraits) : base("RePlate###RePlateMain")
    {
        this.plates = plates;
        this.portraits = portraits;
        about = new AboutTab(plugin);
        Size = new Vector2(820, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(640, 420), MaximumSize = new Vector2(1600, 1400) };
        TitleBarButtons.Add(new TitleBarButton
        {
            Icon = FontAwesomeIcon.Cog,
            IconOffset = new Vector2(2, 1),
            Click = _ => plugin.ToggleSettings(),
            ShowTooltip = () => ImGui.SetTooltip("Settings"),
        });
    }

    /// <summary>Opens the window on the Adventure Plates tab.</summary>
    public void ShowPlates()
    {
        showPlates = true;
        IsOpen = true;
    }

    /// <summary>Opens the window on the Portraits tab.</summary>
    public void ShowPortraits()
    {
        showPortraits = true;
        IsOpen = true;
    }

    public override void Draw()
    {
        using var tabs = ImRaii.TabBar("##tabs");
        if (tabs.Success) DrawTabs();
    }

    private void DrawTabs()
    {
        var platesFlags = showPlates ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        showPlates = false;
        using (var tab = ImRaii.TabItem("Adventure Plates", platesFlags))
        {
            if (tab.Success) plates.Draw();
        }
        var portraitsFlags = showPortraits ? ImGuiTabItemFlags.SetSelected : ImGuiTabItemFlags.None;
        showPortraits = false;
        using (var tab = ImRaii.TabItem("Portraits", portraitsFlags))
        {
            if (tab.Success) portraits.Draw();
        }
        using (var tab = ImRaii.TabItem("About"))
        {
            if (tab.Success) about.Draw();
        }
    }
}
