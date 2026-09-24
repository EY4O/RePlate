using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Component.GUI;
using RePlate.Core.Plates;

namespace RePlate.Game;

public enum PlatePart { Portrait, Design }

/// <summary>
/// Restores a saved portrait or design in one go, the way you would by hand: open your plate, pick the editor from
/// its edit menu, put the saved choices in, check them, press Save, and check what the plate kept. Anything unexpected
/// stops it, and if it stops before Save the editor is left open for you. Start and Update run on the framework thread.
/// </summary>
public sealed unsafe class PlateRestore(PortraitEditor portraits, DesignEditor designs, Func<bool> pauseBeforeSave)
{
    private enum Step { OpenPlate, OpenEditor, WaitEditor, Apply, Check, Save, WaitSaved, Close, WaitClose, Confirm }

    private const string PortraitWindow = "BannerEditor";
    private const string DesignWindow = "CharaCardDesignSetting";
    private const int PortraitSave = 9;
    private const int PortraitClose = 8;
    private const int DesignSave = 30;
    private const int EditMenuRows = 6;
    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StepLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DesignLimit = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    private readonly Queue<PlatePart> parts = new();
    private readonly List<string> done = [];
    private readonly List<string> matched = [];
    // Parts left in an editor for the player to review, with what the plate had saved before; once that changes,
    // the player has saved, and the next Restore moves on to the other part.
    private readonly Dictionary<(Guid, PlatePart), string> reviewed = [];
    private string? savedBefore;
    private PlatePreset? preset;
    private ulong owner;
    private PlatePart part;
    private Step step;
    private bool asked;
    private bool reapplied;
    private string mismatch = "";
    private DateTime stepStarted;
    private DateTime nextAction;
    private ApplyResult? finished;

    public bool Running => preset != null;

    private string Editor => part == PlatePart.Portrait ? "Edit Portrait" : "Edit Plate Design";
    private string Window => part == PlatePart.Portrait ? PortraitWindow : DesignWindow;
    private string OtherWindow => part == PlatePart.Portrait ? DesignWindow : PortraitWindow;

    public string Progress => step switch
    {
        Step.OpenPlate => "Opening your plate...",
        Step.OpenEditor or Step.WaitEditor => $"Opening {Editor}...",
        Step.Apply or Step.Check => part == PlatePart.Design && designs.Running ? designs.Progress : $"Putting the {Name} in...",
        Step.Save or Step.WaitSaved => "Saving...",
        Step.Close or Step.WaitClose => $"Closing {Editor}...",
        _ => "Checking your plate...",
    };

    private string Name => part == PlatePart.Portrait ? "portrait" : "design";

    public ApplyResult? TakeResult()
    {
        var result = finished;
        finished = null;
        return result;
    }

    /// <summary>Restores whatever the preset has: the portrait, then the design.</summary>
    public ApplyResult Start(PlatePreset preset, ulong owner)
    {
        if (Running) return new(false, "Already restoring.");
        if (preset.Owner != owner) return new(false, "That plate belongs to another character.");
        var what = new List<PlatePart>();
        if (preset.Portrait != null) what.Add(PlatePart.Portrait);
        if (preset.Design != null) what.Add(PlatePart.Design);
        if (what.Count == 0) return new(false, "This plate has nothing saved to restore.");
        if (Visible("SelectYesno") || Visible("SelectOk")) return new(false, "Answer the open dialog first.");
        if (Visible(PortraitWindow) && Visible(DesignWindow)) return new(false, "Close Edit Portrait or Edit Plate Design first.");

        this.preset = preset;
        this.owner = owner;
        finished = null;
        done.Clear();
        matched.Clear();
        parts.Clear();
        foreach (var p in what) parts.Enqueue(p);
        NextPart(Step.OpenPlate);
        Plugin.Log.Information($"Restoring the {string.Join(" and ", what.Select(p => p.ToString().ToLowerInvariant()))} from \"{preset.Name}\".");
        return new(true, Progress);
    }

    public void Stop(string message) => Finish(new ApplyResult(false, message));

    public void Update()
    {
        if (preset == null || DateTime.UtcNow < nextAction) return;
        // Nothing here opens a dialog; one showing up means something went differently than planned.
        if (Visible("SelectYesno") || Visible("SelectOk"))
        {
            Stop(step is Step.WaitClose or Step.Save
                ? $"{Editor} asked about unsaved changes, so the save may not have gone through. Answer it yourself."
                : "The game asked something RePlate didn't expect, so it stopped. Answer it yourself.");
            return;
        }
        if (Visible(OtherWindow) && step != Step.OpenPlate)
        {
            Stop($"Close {(part == PlatePart.Portrait ? "Edit Plate Design" : "Edit Portrait")} first; RePlate stopped.");
            return;
        }
        var limit = part == PlatePart.Design && step == Step.Check ? DesignLimit : StepLimit;
        if (DateTime.UtcNow - stepStarted > limit)
        {
            Stop(step switch
            {
                Step.OpenPlate => "Your plate didn't open, so RePlate stopped.",
                Step.OpenEditor => $"{Editor} didn't open from the plate's edit menu, so RePlate stopped.",
                Step.WaitEditor => $"{Editor} didn't finish loading, so RePlate stopped. Nothing was saved.",
                Step.Check => $"Putting the {Name} in took too long, so RePlate stopped. Nothing was saved.",
                Step.WaitSaved => $"{Editor} didn't finish saving. Check it yourself.",
                Step.Close or Step.WaitClose => $"Saved, but {Editor} didn't close. Close it yourself.",
                Step.Confirm => $"Saved, but your plate's {Name} doesn't match{mismatch}. Check the plate yourself.",
                _ => "That step took too long, so RePlate stopped.",
            });
            return;
        }

        switch (step)
        {
            case Step.OpenPlate: OpenPlate(); return;
            case Step.OpenEditor: OpenEditor(); return;
            case Step.WaitEditor:
                if (EditorReady()) Go(Step.Apply);
                return;
            case Step.Apply: Apply(); return;
            case Step.Check: Check(); return;
            case Step.Save: Save(); return;
            case Step.WaitSaved:
                // Edit Portrait stays open after Save; it's done once it no longer has unsaved changes.
                if (!Visible(PortraitWindow)) Go(Step.Confirm);
                else if (PortraitEditor.GetEditor(owner, out var saving, out _) == null && !saving->HasDataChanged) Go(Step.Close);
                return;
            case Step.Close: Close(); return;
            case Step.WaitClose:
                if (!Visible(Window)) Go(Step.Confirm);
                return;
            default: Confirm(); return;
        }
    }

    private void OpenPlate()
    {
        if (PlateReader.GetOwnCard(owner, out _) == null)
        {
            Go(Step.OpenEditor);
            return;
        }
        if (Visible("CharaCard") && asked) return;
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
        }
    }

    // The plate's edit menu lists Edit Portrait first and Edit Plate Design second.
    private void OpenEditor()
    {
        savedBefore = null;
        if (Visible(Window))
        {
            Go(Step.WaitEditor);
            return;
        }
        // Nothing to do when the plate already has it, or when it was put in for review and has been saved since.
        var key = (preset!.Id, part);
        if (Kept() is { Count: 0 } || reviewed.TryGetValue(key, out var before) && Fingerprint() is { } now && now != before)
        {
            Plugin.Log.Information($"The {Name} is already done for \"{preset.Name}\".");
            reviewed.Remove(key);
            matched.Add(Name);
            NextOrFinish();
            return;
        }
        // What the plate has saved now, before the editor starts changing it.
        savedBefore = Fingerprint();
        var menu = Addon("CharaCardEditMenu");
        if (menu == null) return;
        var list = Clicks.FirstList(menu);
        if (list == null || list->GetItemCount() != EditMenuRows)
        {
            Stop("The plate's edit menu isn't laid out the way RePlate expects, so it stopped.");
            return;
        }
        list->DispatchItemEvent(part == PlatePart.Portrait ? 0 : 1, AtkEventType.ListItemClick);
        Go(Step.WaitEditor);
    }

    private bool EditorReady() => part == PlatePart.Portrait
        ? PortraitEditor.GetEditor(owner, out _, out _) == null
        : Addon(DesignWindow) != null && PlateReader.GetOwnCard(owner, out _) == null;

    private void Apply()
    {
        var applied = part == PlatePart.Portrait ? portraits.Apply(preset!, owner) : designs.Start(preset!, owner, quiet: true);
        if (!applied.Applied)
        {
            Stop(applied.Message);
            return;
        }
        Go(Step.Check);
        if (part == PlatePart.Portrait) Wait(SettleTime);
    }

    private void Check()
    {
        ApplyResult check;
        if (part == PlatePart.Portrait)
        {
            check = portraits.Verify(preset!, owner);
            if (!check.Applied && !reapplied)
            {
                // A freshly opened editor can still be settling its camera; put the portrait in once more.
                reapplied = true;
                Go(Step.Apply);
                return;
            }
        }
        else
        {
            if (designs.Running) return;
            check = designs.Last ?? new ApplyResult(false, "The design didn't finish.");
        }
        if (!check.Applied)
        {
            Stop($"{check.Message} {Editor} is left open; nothing was saved.");
            return;
        }
        // Shared plates always stop here so the player has the last look; so do your own with the setting on.
        if (pauseBeforeSave() || preset!.Imported)
        {
            if (savedBefore != null) reviewed[(preset!.Id, part)] = savedBefore;
            var rest = parts.Count > 0 ? " Once it's saved, press Restore again for the design." : "";
            var message = preset!.Imported ? SharedMessage() : check.Message;
            Finish(new ApplyResult(true, $"{message}{rest}", false));
            return;
        }
        if (!check.Clean)
        {
            Stop($"{check.Message} {Editor} is left open; nothing was saved.");
            return;
        }
        Go(Step.Save);
    }

    private void Save()
    {
        var window = Addon(Window);
        if (window == null)
        {
            Stop($"{Editor} closed before it was saved.");
            return;
        }
        // Found by click number: Edit Portrait's struct field named SaveButton is really its close button.
        var number = part == PlatePart.Portrait ? PortraitSave : DesignSave;
        var save = Clicks.FindButton(window, number);
        // The editor can take a moment to notice the change and enable Save.
        if ((save == null || !save->IsEnabled) && DateTime.UtcNow - stepStarted < TimeSpan.FromSeconds(3))
        {
            Wait(TimeSpan.FromMilliseconds(250));
            return;
        }
        if (!Clicks.Button(save, number))
        {
            Plugin.Log.Information($"{Editor} save button: {Clicks.Describe(save)}");
            Stop($"{Editor}'s Save button wasn't available. Nothing was saved.");
            return;
        }
        // Edit Plate Design closes itself after saving.
        Go(part == PlatePart.Portrait ? Step.WaitSaved : Step.WaitClose);
    }

    private void Close()
    {
        var open = Addon(PortraitWindow);
        if (open == null)
        {
            Go(Step.Confirm);
            return;
        }
        if (!Clicks.ButtonWithParam(open, PortraitClose))
        {
            Plugin.Log.Information($"Close button: {Clicks.Describe(Clicks.FindButton(open, PortraitClose))}");
            Stop("Saved, but Edit Portrait's close button wasn't available. Close it yourself.");
            return;
        }
        Go(Step.WaitClose);
    }

    private void Confirm()
    {
        var differences = Kept();
        if (differences == null) return;
        if (differences.Count > 0)
        {
            mismatch = ": " + string.Join(", ", differences);
            return;
        }
        Plugin.Log.Information($"The {Name} from \"{preset!.Name}\" is saved.");
        done.Add(Name);
        NextOrFinish();
    }

    // What your plate keeps, compared with the preset; null while it can't be read. With the design editor open the
    // plate shows its unsaved picks, so this is only asked when the part's editor is closed.
    // A shared plate always ends with the player's look, so its message is an invitation rather than a report.
    private string SharedMessage()
    {
        var warning = part == PlatePart.Portrait && portraits.GameWarning is { } w ? $" The game warns that {w}." : "";
        var kept = part == PlatePart.Portrait ? portraits.Kept : designs.Kept;
        return $"{(part == PlatePart.Portrait ? "Portrait" : "Design")} Applied! Look it over and press Save if it's looking good.{warning}{kept}";
    }

    // What the plate has saved for this part, as text, to notice when the player saves after a review.
    private string? Fingerprint()
    {
        if (PlateReader.GetOwnCard(owner, out var card) != null) return null;
        if (part == PlatePart.Portrait)
            return System.Text.Json.JsonSerializer.Serialize(
                PortraitData.FromGame(card->PortraitData, card->BannerBg, card->BannerFrame, card->BannerDecoration));
        var d = card->PlateDesign;
        var decorations = new List<ushort>();
        for (var i = 0; i < d.NumDecorations && i < d.Decorations.Length; i++) decorations.Add(d.Decorations[i]);
        return $"{d.BasePlate}/{d.TopBorder}/{d.BottomBorder}/{string.Join(",", decorations)}/{card->InvertPortraitPlacement}";
    }

    private List<string>? Kept()
    {
        if (PlateReader.GetOwnCard(owner, out var card) != null) return null;
        if (part == PlatePart.Design) return DesignEditor.Differences(card, preset!.Design!);
        if (card->IsNotCreated || card->WasResetDueToFantasia) return ["portrait"];
        var saved = PortraitData.FromGame(card->PortraitData, card->BannerBg, card->BannerFrame, card->BannerDecoration);
        return PortraitCheck.Differences(preset!.Portrait!, saved);
    }

    private void NextOrFinish()
    {
        if (parts.Count > 0)
        {
            NextPart(Step.OpenEditor);
            return;
        }
        var message = done.Count switch
        {
            0 => "Your plate already matches this one.",
            2 => "Portrait and design restored and saved.",
            _ => $"{char.ToUpperInvariant(done[0][0])}{done[0][1..]} restored and saved" +
                 (matched.Count > 0 ? $"; the {matched[0]} already matched." : "."),
        };
        Finish(new ApplyResult(true, message, true));
    }

    private void NextPart(Step first)
    {
        part = parts.Dequeue();
        reapplied = false;
        mismatch = "";
        Go(first);
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
        if (designs.Running) designs.Stop(result.Message);
        Plugin.Log.Information($"Restore ended at {part} {step}: {result.Message}");
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
