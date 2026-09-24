using System;
using System.Linq;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace RePlate.Windows;

public sealed class AboutTab
{
    private const string SiteUrl = "https://ey4o.github.io/XIV-Plugins/";
    private const string SourceUrl = "https://github.com/EY4O/RePlate";

    public void Draw()
    {
        ImGuiHelpers.ScaledDummy(6);
        Ui.Centered($"RePlate {Plugin.PluginInterface.Manifest.AssemblyVersion}");
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted))
            Ui.Centered("Save and restore your adventurer plate.");
        ImGuiHelpers.ScaledDummy(10);
        Ui.Logo(112);
        ImGuiHelpers.ScaledDummy(14);

        (string Label, string Url)[] links = [("Plugin site", SiteUrl), ("Source code", SourceUrl)];
        var style = ImGui.GetStyle();
        var width = links.Sum(l => ImGui.CalcTextSize(l.Label).X + style.FramePadding.X * 2) + style.ItemSpacing.X * (links.Length - 1);
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2) + ImGui.GetCursorPosX());
        for (var i = 0; i < links.Length; i++)
        {
            if (i > 0) ImGui.SameLine();
            if (ImGui.Button(links[i].Label)) Ui.OpenUrl(links[i].Url);
            Ui.Tip(links[i].Url);
        }
    }
}
