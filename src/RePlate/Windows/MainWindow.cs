using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace RePlate.Windows;

public sealed class MainWindow : ThemedWindow
{
    private const string PatreonUrl = "https://www.patreon.com/Looneth";
    private const string KoFiUrl = "https://ko-fi.com/looneth";

    private readonly AboutTab about = new();
    private float supportWidth;

    public MainWindow(Plugin plugin) : base("RePlate###RePlateMain")
    {
        Size = new Vector2(820, 560);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(640, 420), MaximumSize = new Vector2(1600, 1400) };
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
        using (var tab = ImRaii.TabItem("Plates"))
        {
            if (tab.Success) ImGui.TextDisabled("Your saved plates will appear here.");
        }
        using (var tab = ImRaii.TabItem("About"))
        {
            if (tab.Success) about.Draw();
        }
    }

    /// <summary>
    /// The support button at the right end of the tab row: left click opens Patreon, right click Ko-fi. It's drawn after
    /// the tabs, over the empty end of their row, and its width is measured once drawn so it sits flush from then on.
    /// </summary>
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
