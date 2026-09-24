using System;
using System.Collections.Generic;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Component.GUI;
using RePlate.Core.Plates;

namespace RePlate.Game;

/// <summary>
/// Restores a saved portrait in one go, the way you would by hand: open your plate, pick Edit Portrait from its edit
/// menu, put the portrait in, check it, press Save, and check what the plate kept. Anything unexpected stops it, and
/// if it stops before Save the editor is left open for you. Start and Update run on the framework thread.
/// </summary>
public sealed unsafe class PortraitRestore(PortraitEditor editor)
{
    private enum Step { OpenPlate, OpenEditor, WaitEditor, Apply, Check, Save, WaitClose, Confirm }

    private const int SaveButton = 9;
    private const int EditPortraitRow = 0;
    private const int EditMenuRows = 6;
    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StepLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    private PlatePreset? preset;
    private ulong owner;
    private Step step;
    private bool asked;
    private bool reapplied;
    private string mismatch = "";
    private DateTime stepStarted;
    private DateTime nextAction;
    private ApplyResult? finished;

    public bool Running => preset != null;

    public string Progress => step switch
    {
        Step.OpenPlate => "Opening your plate...",
        Step.OpenEditor or Step.WaitEditor => "Opening Edit Portrait...",
        Step.Apply or Step.Check => "Putting the portrait in...",
        Step.Save or Step.WaitClose => "Saving...",
        _ => "Checking your plate...",
    };

    public ApplyResult? TakeResult()
    {
        var result = finished;
        finished = null;
        return result;
    }

    public ApplyResult Start(PlatePreset preset, ulong owner)
    {
        if (Running) return new(false, "Already restoring a portrait.");
        if (preset.Portrait == null || preset.Owner != owner) return new(false, "This plate has no portrait to restore.");
        if (Visible("CharaCardDesignSetting")) return new(false, "Close Edit Plate Design first.");
        if (Visible("SelectYesno") || Visible("SelectOk")) return new(false, "Answer the open dialog first.");
        this.preset = preset;
        this.owner = owner;
        finished = null;
        mismatch = "";
        reapplied = false;
        Go(Step.OpenPlate);
        Plugin.Log.Information($"Restoring the portrait from \"{preset.Name}\".");
        return new(true, Progress);
    }

    public void Stop(string message) => Finish(new ApplyResult(false, message));

    public void Update()
    {
        if (preset == null || DateTime.UtcNow < nextAction) return;
        // Nothing here opens a dialog; one showing up means something went differently than planned.
        if (Visible("SelectYesno") || Visible("SelectOk"))
        {
            Stop("The game asked something RePlate didn't expect, so it stopped. Answer it yourself.");
            return;
        }
        if (Visible("CharaCardDesignSetting"))
        {
            Stop("Edit Plate Design opened, so RePlate stopped.");
            return;
        }
        if (DateTime.UtcNow - stepStarted > StepLimit)
        {
            Stop(step switch
            {
                Step.OpenPlate => "Your plate didn't open, so RePlate stopped.",
                Step.OpenEditor => "Edit Portrait didn't open from the plate's edit menu, so RePlate stopped.",
                Step.WaitEditor => "Edit Portrait didn't finish loading, so RePlate stopped. Nothing was saved.",
                Step.WaitClose => "Edit Portrait didn't close after Save. Check the plate yourself.",
                Step.Confirm => $"Saved, but your plate's portrait doesn't match{mismatch}. Check the plate yourself.",
                _ => "That step took too long, so RePlate stopped.",
            });
            return;
        }

        switch (step)
        {
            case Step.OpenPlate:
                if (PlateReader.GetOwnCard(owner, out _) == null)
                {
                    Go(Step.OpenEditor);
                    return;
                }
                if (Visible("CharaCard") && asked)
                    return;
                if (Visible("CharaCard"))
                {
                    Stop("Someone else's plate is open. Close it first.");
                    return;
                }
                if (!asked)
                {
                    if (new PlateReader().OpenOwnPlate(owner) is { } problem)
                    {
                        Stop(problem);
                        return;
                    }
                    asked = true;
                    Wait(Pace);
                }
                return;

            case Step.OpenEditor:
                if (Visible("BannerEditor"))
                {
                    Go(Step.WaitEditor);
                    return;
                }
                var menu = Addon("CharaCardEditMenu");
                if (menu == null) return;
                var list = Clicks.FirstList(menu);
                if (list == null || list->GetItemCount() != EditMenuRows)
                {
                    Stop("The plate's edit menu isn't laid out the way RePlate expects, so it stopped.");
                    return;
                }
                list->DispatchItemEvent(EditPortraitRow, AtkEventType.ListItemClick);
                Go(Step.WaitEditor);
                return;

            case Step.WaitEditor:
                if (PortraitEditor.GetEditor(owner, out _, out _) == null) Go(Step.Apply);
                return;

            case Step.Apply:
                var applied = editor.Apply(preset, owner);
                if (!applied.Applied)
                {
                    Stop(applied.Message);
                    return;
                }
                Go(Step.Check);
                Wait(SettleTime);
                return;

            case Step.Check:
                var check = editor.Verify(preset, owner);
                if (!check.Clean && !reapplied)
                {
                    // A freshly opened editor can still be settling its camera; put the portrait in once more.
                    reapplied = true;
                    Go(Step.Apply);
                    return;
                }
                if (!check.Clean)
                {
                    Stop(check.Message + " Edit Portrait is left open; nothing was saved.");
                    return;
                }
                Go(Step.Save);
                return;

            case Step.Save:
                if (PortraitEditor.GetEditor(owner, out _, out var addon) is { } gone)
                {
                    Stop(gone);
                    return;
                }
                var save = addon->SaveButton;
                // The editor can take a moment to notice the change and enable Save.
                if ((save == null || !save->IsEnabled) && DateTime.UtcNow - stepStarted < TimeSpan.FromSeconds(3))
                {
                    Wait(TimeSpan.FromMilliseconds(250));
                    return;
                }
                if (!Clicks.Button(save, SaveButton))
                {
                    Plugin.Log.Information($"Save button: {Clicks.Describe(save)}");
                    Stop("Edit Portrait's Save button wasn't available. Nothing was saved.");
                    return;
                }
                Go(Step.WaitClose);
                return;

            case Step.WaitClose:
                if (!Visible("BannerEditor")) Go(Step.Confirm);
                return;

            default:
                var differences = Kept();
                if (differences is { Count: 0 })
                    Finish(new ApplyResult(true, "Portrait restored and saved.", true));
                else if (differences != null)
                    mismatch = ": " + string.Join(", ", differences);
                return;
        }
    }

    // What your plate now has, compared with the preset; null until it can be read.
    private List<string>? Kept()
    {
        if (PlateReader.GetOwnCard(owner, out var card) != null || card->IsNotCreated) return null;
        var saved = PortraitData.FromGame(card->PortraitData, card->BannerBg, card->BannerFrame, card->BannerDecoration);
        return PortraitCheck.Differences(preset!.Portrait!, saved);
    }

    private void Go(Step next)
    {
        step = next;
        asked = false;
        stepStarted = DateTime.UtcNow;
        Wait(Pace);
    }

    private void Wait(TimeSpan gap) => nextAction = DateTime.UtcNow + gap;

    private void Finish(ApplyResult result)
    {
        if (preset == null) return;
        Plugin.Log.Information($"Portrait restore ended at {step}: {result.Message}");
        preset = null;
        finished = result;
    }

    private static bool Visible(string name)
    {
        var addon = Plugin.GameGui.GetAddonByName(name);
        return !addon.IsNull && addon.IsVisible;
    }

    private static AtkUnitBase* Addon(string name)
    {
        var addon = Plugin.GameGui.GetAddonByName(name);
        return addon.IsNull || !addon.IsVisible || !addon.IsReady ? null : (AtkUnitBase*)addon.Address;
    }
}
