using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace RePlate.Windows;

/// <summary>The first thing a new player sees. Show me around starts the guided tour in the main window.</summary>
public sealed class WelcomeWindow : ThemedWindow
{
    private readonly Plugin plugin;

    public WelcomeWindow(Plugin plugin) : base("Welcome to RePlate###RePlateWelcome", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(520, 430);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(460, 380), MaximumSize = new Vector2(900, 900) };
    }

    public void Open() => IsOpen = true;

    public override void OnClose()
    {
        if (plugin.Configuration.WelcomeSeen) return;
        plugin.Configuration.WelcomeSeen = true;
        plugin.MarkDirty();
    }

    public override void Draw()
    {
        ImGuiHelpers.ScaledDummy(8);
        Ui.Logo(96);
        ImGuiHelpers.ScaledDummy(6);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) Ui.Centered("Welcome to RePlate");
        ImGuiHelpers.ScaledDummy(4);
        ImGui.TextWrapped("RePlate lets you save copies of your Adventure Plate so you can try something new, come back from a " +
                          "Fantasia and put the old one back in one click!");
        ImGuiHelpers.ScaledDummy(4);
        ImGui.TextUnformatted("We'll be guiding you through:");
        ImGuiHelpers.ScaledDummy(2);
        using (ImRaii.PushIndent())
        {
            ImGui.TextUnformatted("1. Saving your current Plate,");
            ImGui.TextUnformatted("2. Capturing a tile for it,");
            ImGui.TextUnformatted("3. and restoring.");
        }
        ImGuiHelpers.ScaledDummy(10);

        const string tour = "Show me around", later = "Not now";
        var style = ImGui.GetStyle();
        var width = ImGui.CalcTextSize(tour).X + ImGui.CalcTextSize(later).X + style.FramePadding.X * 4 + style.ItemSpacing.X;
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2) + ImGui.GetCursorPosX());
        if (Theme.PrimaryButton(tour))
        {
            IsOpen = false;
            plugin.StartTour();
        }
        ImGui.SameLine();
        if (ImGui.Button(later)) IsOpen = false;
        ImGuiHelpers.ScaledDummy(4);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted)) Ui.Centered("You can open this again from the About tab or with /replate welcome");
    }
}
