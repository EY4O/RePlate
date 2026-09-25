using System;
using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace RePlate.Windows;

public enum GuideTarget { OpenPlate, SavePlate, Capture, Crop, UsePicture, Restore, OpenPortraits, SaveFromGearset, ApplyToGearsets }

public enum Tour { Plates, Portraits }

/// <summary>
/// A guided tour of one tab: a pulsing ring and a small bubble on the button to press next, and a bar at the top of
/// the tab. Each step moves on when the real thing happens, and the step is kept in the settings so a reload picks up
/// there. Both tours go the same way: open the game's window, save one, give it a picture, then use it.
/// </summary>
public sealed class Guide(Plugin plugin, Tour tour)
{
    private sealed record Step(GuideTarget? Target, string Bubble, string Bar);

    private Step[] Steps => tour == Tour.Plates ? PlateSteps : PortraitSteps;

    private static readonly Step[] PortraitSteps =
    [
        new(GuideTarget.OpenPortraits, "Start here: open your Portraits.",
            "Open the game's Portraits window. It shows each gear set's portrait, and its preview makes a good picture."),
        new(GuideTarget.SaveFromGearset, "Pick a gear set to keep its portrait.",
            "Press Save from gear set and pick one. RePlate keeps a copy of that gear set's portrait here."),
        new(GuideTarget.Capture, "Take a picture for its tile.",
            "Select the same gear set in the Portraits window so its portrait shows, then press Capture game view. " +
            "RePlate marks the portrait for you."),
        new(GuideTarget.Crop, "Adjust the box if you like, then crop.",
            "Drag on the picture to change the marked area, then press Crop."),
        new(GuideTarget.UsePicture, "Keep it as this portrait's tile.",
            "Happy with it? Press Use this picture."),
        new(GuideTarget.ApplyToGearsets, "Put it on your other gear sets.",
            "Apply to gear sets puts this portrait on the gear sets you tick, one at a time. Each opens for you to look " +
            "over, Save and close. Try it now, or skip this step."),
        new(null, "", "That's everything. Replay this tour any time from the About tab."),
    ];

    private static readonly Step[] PlateSteps =
    [
        new(GuideTarget.OpenPlate, "Start here: open your plate.",
            "Open your adventurer plate, so RePlate can see it. Close Edit Portrait and Edit Plate Design if they're open."),
        new(GuideTarget.SavePlate, "Name it on the left, then save it.",
            "Give this plate a name in the box on the left, then press Save current plate."),
        new(GuideTarget.Capture, "Take a picture for its tile.",
            "With your plate on screen, press Capture game view. RePlate marks the plate's area for you."),
        new(GuideTarget.Crop, "Adjust the box if you like, then crop.",
            "Drag on the picture to change the marked area, then press Crop."),
        new(GuideTarget.UsePicture, "Keep it as this plate's tile.",
            "Happy with it? Press Use this picture."),
        new(GuideTarget.Restore, "Puts this plate back on your character.",
            "Restore puts a saved plate back: the portrait, then the design, each checked and saved. Parts that already " +
            "match are skipped. Try it now, or skip this step."),
        new(null, "", "That's everything. Replay this tour any time from the welcome guide (/replate welcome)."),
    ];

    private const int PictureStart = 2, PictureEnd = 4, UseStep = 5;

    // Where each step started from; set when the step is first seen, so a resumed tour starts afresh.
    private int savedAtStart = -1;
    private int picturesAtStart = -1;
    private bool sawRun;

    public bool Active => Current >= 0;

    private int Current
    {
        get => tour == Tour.Plates ? plugin.Configuration.TourStep : plugin.Configuration.PortraitTourStep;
        set
        {
            if (tour == Tour.Plates) plugin.Configuration.TourStep = value;
            else plugin.Configuration.PortraitTourStep = value;
        }
    }

    public void Start() => Go(0);

    public void End() => Go(-1);

    /// <summary>
    /// Moves the tour on when what the step asks for has happened. Called once a frame from the tab, with whether the
    /// game's window is open, how many are saved, the picture tools, and whether a restore or gear set run is going.
    /// </summary>
    public void Update(bool windowOpen, int saved, PlateImages images, bool running)
    {
        if (!Active) return;
        switch (Current)
        {
            case 0:
                if (windowOpen) Go(1);
                break;
            case 1:
                if (savedAtStart < 0) savedAtStart = saved;
                else if (saved > savedAtStart) Go(2);
                break;
            case >= PictureStart and <= PictureEnd:
                if (picturesAtStart < 0) picturesAtStart = images.SavedCount;
                if (images.SavedCount > picturesAtStart) Go(UseStep);
                else if (!images.HasCapture) { if (Current != PictureStart) Go(PictureStart); }
                else if (images.IsCropped) { if (Current != PictureEnd) Go(PictureEnd); }
                else if (Current != 3) Go(3);
                break;
            case UseStep:
                if (running) sawRun = true;
                else if (sawRun) Go(UseStep + 1);
                break;
        }
    }

    /// <summary>The tour bar, drawn at the top of its tab.</summary>
    public void DrawBar()
    {
        if (!Active) return;
        // Taken now: the buttons below can end the tour while this frame is still being drawn.
        var step = Math.Min(Current, Steps.Length - 1);
        var last = step == Steps.Length - 1;
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText))
            ImGui.TextUnformatted(last ? "Guided tour: done" : $"Guided tour: step {step + 1} of {Steps.Length - 1}");
        ImGui.SameLine();
        if (last)
        {
            if (ImGui.SmallButton("Finish")) End();
        }
        else
        {
            if (ImGui.SmallButton("Skip step")) Go(step is >= PictureStart and <= PictureEnd ? UseStep : step + 1);
            ImGui.SameLine();
            if (ImGui.SmallButton("End tour")) End();
        }
        ImGui.TextWrapped(Steps[step].Bar);
        ImGui.Separator();
    }

    /// <summary>Call straight after drawing a button: if it's the one to press next, it gets the ring and the bubble.</summary>
    public void Mark(GuideTarget target)
    {
        if (!Active || Current >= Steps.Length || Steps[Current].Target != target) return;
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        var scale = ImGuiHelpers.GlobalScale;
        // Drawn with RePlate's own window, not over everything, so another window in front of RePlate covers it too.
        // The clip is widened so the bubble can still hang past the window's edge.
        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRectFullScreen();
        var accent = Theme.Accent;

        // A ring that breathes, with a fainter halo outside it.
        var pulse = 0.55f + 0.45f * MathF.Sin((float)ImGui.GetTime() * 4f);
        var ring = new Vector2(3 * scale);
        draw.AddRect(min - ring, max + ring, ImGui.GetColorU32(accent with { W = pulse }), 6 * scale, 2.5f * scale);
        draw.AddRect(min - ring * 2, max + ring * 2, ImGui.GetColorU32(accent with { W = pulse * 0.35f }), 8 * scale, 2f * scale);

        // The bubble sits under the button with an arrow pointing up at it, or above when there's no room below.
        var text = Steps[Current].Bubble;
        var wrap = 220 * scale;
        var padding = new Vector2(8 * scale, 6 * scale);
        var size = ImGui.CalcTextSize(text, false, wrap) + padding * 2;
        var gap = 12 * scale;
        var screen = ImGui.GetMainViewport();
        var below = max.Y + gap + size.Y < screen.Pos.Y + screen.Size.Y;
        var x = Math.Clamp(min.X, screen.Pos.X + 4, screen.Pos.X + screen.Size.X - size.X - 4);
        var top = new Vector2(x, below ? max.Y + gap : min.Y - gap - size.Y);
        var tip = new Vector2(Math.Clamp((min.X + max.X) / 2, x + 10 * scale, x + size.X - 10 * scale), below ? max.Y + 4 * scale : min.Y - 4 * scale);
        var edge = below ? top.Y : top.Y + size.Y;
        var arrow = 6 * scale;

        draw.AddRectFilled(top, top + size, ImGui.GetColorU32(new Vector4(0.10f, 0.11f, 0.14f, 0.97f)), 6 * scale);
        draw.AddRect(top, top + size, ImGui.GetColorU32(accent), 6 * scale, 1.5f * scale);
        draw.AddTriangleFilled(new Vector2(tip.X - arrow, edge), new Vector2(tip.X + arrow, edge), tip, ImGui.GetColorU32(accent));
        draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), top + padding, ImGui.GetColorU32(new Vector4(1, 1, 1, 1)), text, wrap);
        draw.PopClipRect();
    }

    private void Go(int step)
    {
        Current = step;
        plugin.MarkDirty();
        savedAtStart = -1;
        if (step is < PictureStart or > PictureEnd) picturesAtStart = -1;
        sawRun = false;
    }
}
