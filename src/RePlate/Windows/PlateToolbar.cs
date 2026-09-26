using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using RePlate.Game;

namespace RePlate.Windows;

/// <summary>
/// A small bar above the top-right corner of your plate's edit menu (the window beside the plate with Edit Portrait and
/// Edit Plate Design), with a button that opens RePlate. It only shows beside your own plate.
/// </summary>
public sealed class PlateToolbar(Plugin plugin)
{
    private const ImGuiWindowFlags Flags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
        ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoFocusOnAppearing |
        ImGuiWindowFlags.NoNav | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoCollapse;

    public void Draw()
    {
        if (!plugin.Configuration.ShowPlateToolbar || !PlateReader.OwnPlateOpen(plugin.CharacterId)) return;
        var menu = Plugin.GameGui.GetAddonByName("CharaCardEditMenu");
        if (menu.IsNull || !menu.IsVisible) return;
        // The bar's bottom-right corner touches the edit menu's top-right.
        var corner = ImGuiHelpers.MainViewport.Pos + menu.Position + new Vector2(menu.ScaledSize.X, 0);
        ImGui.SetNextWindowPos(corner, ImGuiCond.Always, new Vector2(1, 1));
        var theme = Theme.Push();
        try
        {
            if (ImGui.Begin("###RePlatePlateToolbar", Flags))
            {
                if (Theme.AccentIconButton(FontAwesomeIcon.IdCard, "RePlate")) plugin.ShowPlates();
                Ui.Tip("Open RePlate on your saved plates.");
            }
            ImGui.End();
        }
        finally
        {
            Theme.Pop(theme);
        }
    }
}
