using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;

namespace RePlate.Windows;

/// <summary>A short tour for new players: saving a plate, putting it back, pictures, and the one setting worth a look.</summary>
public sealed class WelcomeWindow : ThemedWindow
{
    private static readonly string[] Titles = ["Save your plate", "Put it back", "Pictures", "A few settings"];

    private readonly Plugin plugin;
    private int step;

    public WelcomeWindow(Plugin plugin) : base("Welcome to RePlate###RePlateWelcome", ImGuiWindowFlags.NoCollapse)
    {
        this.plugin = plugin;
        Size = new Vector2(580, 460);
        SizeCondition = ImGuiCond.FirstUseEver;
        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(500, 400), MaximumSize = new Vector2(1000, 1000) };
    }

    public void Open()
    {
        step = 0;
        IsOpen = true;
    }

    public override void OnClose()
    {
        if (plugin.Configuration.WelcomeSeen) return;
        plugin.Configuration.WelcomeSeen = true;
        plugin.MarkDirty();
    }

    public override void Draw()
    {
        if (step == 0)
        {
            DrawWelcome();
            return;
        }

        ImGui.ProgressBar(step / (float)Titles.Length, new Vector2(-1, 6 * ImGuiHelpers.GlobalScale), "");
        ImGui.TextDisabled($"Step {step} of {Titles.Length}");
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) ImGui.TextUnformatted(Titles[step - 1]);
        ImGui.Separator();

        var footer = ImGui.GetFrameHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y;
        using (var body = ImRaii.Child("##body", new Vector2(0, -footer)))
        {
            if (body.Success)
            {
                switch (step)
                {
                    case 1: SavePlate(); break;
                    case 2: PutBack(); break;
                    case 3: Pictures(); break;
                    default: Settings(); break;
                }
            }
        }

        ImGui.Separator();
        if (ImGui.Button("Back")) step--;
        ImGui.SameLine();
        if (ImGui.Button("Close")) IsOpen = false;
        var last = step == Titles.Length;
        var label = last ? "Finish" : "Next";
        ImGui.SameLine(Math.Max(ImGui.GetCursorPosX(), ImGui.GetWindowContentRegionMax().X - ImGui.CalcTextSize(label).X - ImGui.GetStyle().FramePadding.X * 2));
        if (Theme.PrimaryButton(label))
        {
            if (!last) step++;
            else
            {
                IsOpen = false;
                plugin.ShowMainWindow();
            }
        }
    }

    private void DrawWelcome()
    {
        ImGuiHelpers.ScaledDummy(8);
        Ui.Logo(96);
        ImGuiHelpers.ScaledDummy(6);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) Ui.Centered("Welcome to RePlate");
        ImGuiHelpers.ScaledDummy(4);
        ImGui.TextWrapped("RePlate keeps copies of your adventurer plate, so you can try something new, or come back from a " +
                          "Fantasia, and put the old one back in one click.");
        ImGuiHelpers.ScaledDummy(4);
        Bullet("Saves the portrait: pose, expression, camera, lighting, background, frame and accent.");
        Bullet("Saves the plate design: base plate, borders, decorations and layout.");
        Bullet("Puts them back through the plate's own editors, checks the result, and saves it.");
        ImGuiHelpers.ScaledDummy(10);

        const string tour = "Show me around", later = "Not now";
        var style = ImGui.GetStyle();
        var width = ImGui.CalcTextSize(tour).X + ImGui.CalcTextSize(later).X + style.FramePadding.X * 4 + style.ItemSpacing.X;
        ImGui.SetCursorPosX(Math.Max(0, (ImGui.GetContentRegionAvail().X - width) / 2) + ImGui.GetCursorPosX());
        if (Theme.PrimaryButton(tour)) step = 1;
        ImGui.SameLine();
        if (ImGui.Button(later)) IsOpen = false;
        ImGuiHelpers.ScaledDummy(4);
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Muted)) Ui.Centered("You can open this again from the About tab or with /replate welcome.");
    }

    private static void SavePlate()
    {
        ImGui.TextWrapped("RePlate copies the plate you have saved in the game, so set it up the way you like first.");
        ImGuiHelpers.ScaledDummy(2);
        Bullet("Press Open my plate, or open your adventurer plate from the character menu.");
        Bullet("Close Edit Portrait and Edit Plate Design, so what's copied is what you saved.");
        Bullet("Give it a name and press Save current plate.");
        ImGuiHelpers.ScaledDummy(2);
        ImGui.TextWrapped("Plates are kept per character. Each one shows what it holds: the pose, expression, background and " +
                          "the rest of the portrait, and every part of the design.");
    }

    private static void PutBack()
    {
        ImGui.TextWrapped("Select a plate and press Restore. RePlate opens your plate, picks Edit Portrait from its edit menu, " +
                          "puts the portrait in, checks it and saves it, then does the same for the design.");
        ImGuiHelpers.ScaledDummy(2);
        Bullet("Parts that already match are skipped.");
        Bullet("Anything that isn't unlocked on this character is named, and nothing is changed.");
        Bullet("If something looks off it stops before saving, with the editor open for you. Stop or /replate stop halts it any time.");
        ImGuiHelpers.ScaledDummy(2);
        ImGui.TextWrapped("Apply Portrait and Apply Plate Design put a plate into an editor you already have open, without saving, " +
                          "so you can change it further first.");
    }

    private static void Pictures()
    {
        ImGui.TextWrapped("A picture makes plates easy to tell apart.");
        ImGuiHelpers.ScaledDummy(2);
        Bullet("With your plate open, press Capture game view. The plate's area is marked for you.");
        Bullet("Drag to adjust the area, then Crop, then Use this picture. The highlighted button is always the next step.");
        Bullet("Or attach a PNG you already have.");
    }

    private void Settings()
    {
        var c = plugin.Configuration;
        var changed = false;
        ImGui.TextWrapped("These can be changed any time in Settings (the cog on the RePlate window).");
        ImGuiHelpers.ScaledDummy(2);
        changed |= Checkbox("Pause before saving, so I can look it over first", c.PauseBeforeSave, v => c.PauseBeforeSave = v);
        changed |= Checkbox("Use the RePlate theme", c.UseTheme, v => c.UseTheme = v);
        if (changed) plugin.MarkDirty();
        ImGuiHelpers.ScaledDummy(4);
        ImGui.TextWrapped("That's it. Open RePlate any time with /replate.");
    }

    private static bool Checkbox(string label, bool value, Action<bool> set)
    {
        var v = value;
        if (!ImGui.Checkbox(label, ref v)) return false;
        set(v);
        return true;
    }

    private static void Bullet(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }
}
