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
    private const string PatreonUrl = "https://www.patreon.com/Looneth";
    private const string KoFiUrl = "https://ko-fi.com/looneth";

    private readonly PlatesTab plates;
    private readonly AboutTab about;
    private float supportWidth;

    public MainWindow(Plugin plugin, PlatesTab plates) : base("RePlate###RePlateMain")
    {
        this.plates = plates;
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

    public override void Draw()
    {
        var tabRow = ImGui.GetCursorPos();
        using (var tabs = ImRaii.TabBar("##tabs"))
        {
            if (tabs.Success) DrawTabs();
        }
        DrawSupport(tabRow);
    }

    private void DrawTabs()
    {
        using (var tab = ImRaii.TabItem("Adventure Plates"))
        {
            if (tab.Success) plates.Draw();
        }
        using (var tab = ImRaii.TabItem("Portraits"))
        {
            if (tab.Success)
            {
                ImGuiHelpers.ScaledDummy(6);
                ImGui.TextDisabled("Saving and restoring your gear sets' portraits is coming here later.");
            }
        }
        using (var tab = ImRaii.TabItem("About"))
        {
            if (tab.Success) about.Draw();
        }
    }

    // Sits at the right end of the tab row. Left click Patreon, right click Ko-fi.
    private void DrawSupport(Vector2 tabRow)
    {
        ImGui.SetCursorPos(new Vector2(Math.Max(tabRow.X, ImGui.GetWindowContentRegionMax().X - supportWidth), tabRow.Y));
        if (Theme.AccentIconButton(FontAwesomeIcon.Heart, "Patreon / Ko-fi")) Ui.OpenUrl(PatreonUrl);
        if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) Ui.OpenUrl(KoFiUrl);
        supportWidth = ImGui.GetItemRectSize().X;
        Ui.Tip("If RePlate has saved you some time, please consider supporting its developer.\n\n" +
               "Left click: Patreon\nRight click: Ko-fi");
    }
}
