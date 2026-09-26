using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace RePlate.Windows;

public sealed class AboutTab(Plugin plugin)
{
    private const string SiteUrl = "https://ey4o.github.io/XIV-Plugins/";
    private const string SourceUrl = "https://github.com/EY4O/RePlate";
    private const string PatreonUrl = "https://www.patreon.com/Looneth";
    private const string KoFiUrl = "https://ko-fi.com/looneth";

    // The donate buttons' width together, measured when drawn, to centre them.
    private float supportWidth;

    public void Draw()
    {
        ImGuiHelpers.ScaledDummy(6);
        Ui.Centered($"RePlate {Plugin.PluginInterface.Manifest.AssemblyVersion}");
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted))
            Ui.Centered("Save and restore your adventurer plate.");
        ImGuiHelpers.ScaledDummy(10);
        Ui.Logo(112);
        ImGuiHelpers.ScaledDummy(14);

        (string Label, Action Click, string Tip)[] buttons =
        [
            ("Welcome guide", plugin.OpenWelcome, "A short tour of what RePlate does."),
            ("Portraits tour", plugin.StartPortraitTour, "A short tour of the Portraits tab."),
            ("Settings", plugin.ToggleSettings, "Pausing before saving, backups, and the look."),
            ("Plugin site", () => Ui.OpenUrl(SiteUrl), SiteUrl),
            ("Source code", () => Ui.OpenUrl(SourceUrl), SourceUrl + "\nMIT licence."),
        ];
        var style = ImGui.GetStyle();
        var width = buttons.Sum(b => ImGui.CalcTextSize(b.Label).X + style.FramePadding.X * 2) + style.ItemSpacing.X * (buttons.Length - 1);
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2) + ImGui.GetCursorPosX());
        for (var i = 0; i < buttons.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (ImGui.Button(buttons[i].Label)) buttons[i].Click();
            Ui.Tip(buttons[i].Tip);
        }

        ImGuiHelpers.ScaledDummy(18);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted))
            Ui.Centered("If you're enjoying RePlate, please consider donating.");
        ImGuiHelpers.ScaledDummy(4);
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - supportWidth) / 2) + ImGui.GetCursorPosX());
        var left = ImGui.GetCursorScreenPos().X;
        // Each in its own brand colour.
        if (Theme.BrandButton(FontAwesomeIcon.Heart, "Patreon", 0xFF424D)) Ui.OpenUrl(PatreonUrl);
        Ui.Tip(PatreonUrl);
        ImGui.SameLine();
        if (Theme.BrandButton(FontAwesomeIcon.MugHot, "Ko-fi", 0x29ABE0)) Ui.OpenUrl(KoFiUrl);
        Ui.Tip(KoFiUrl);
        supportWidth = ImGui.GetItemRectMax().X - left;
    }
}
