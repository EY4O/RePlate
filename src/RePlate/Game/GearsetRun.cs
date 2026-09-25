using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using RePlate.Core.Plates;

namespace RePlate.Game;

/// <summary>
/// Puts one portrait on several gear sets, one at a time. For each it opens that gear set's Edit Portrait, puts the
/// portrait in and checks it, then waits for the player to look it over, press Save and close the editor; closing
/// without saving skips it. RePlate never presses Save here. Start and Update run on the framework thread.
/// </summary>
public sealed class GearsetRun(PortraitEditor portraits)
{
    private enum Step { Open, WaitEditor, Apply, Check, Review, Next }

    private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StepLimit = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(1);

    private readonly List<GearsetInfo> queue = [];
    private readonly List<string> saved = [];
    private readonly List<string> notSaved = [];
    private PlatePreset? preset;
    private ulong owner;
    private int index;
    private Step step;
    private bool reapplied;
    private bool triedList;
    private string before = "";
    private DateTime stepStarted;
    private DateTime nextAction;
    private ApplyResult? finished;

    public bool Running => preset != null;

    /// <summary>What's happening now, for the bar above the gallery.</summary>
    public string Progress { get; private set; } = "";

    /// <summary>True while RePlate is waiting for the player to save or close.</summary>
    public bool WaitingForPlayer => Running && step == Step.Review;

    public ApplyResult? TakeResult()
    {
        var result = finished;
        finished = null;
        return result;
    }

    public ApplyResult Start(PlatePreset preset, ulong owner, IReadOnlyList<GearsetInfo> gearsets)
    {
        if (Running) return new(false, "Already going through gear sets.");
        if (preset.Portrait == null || preset.Owner != owner) return new(false, "This has no portrait to use.");
        if (gearsets.Count == 0) return new(false, "Tick at least one gear set.");
        if (Visible("BannerEditor") || Visible("CharaCardDesignSetting")) return new(false, "Close the portrait and plate editors first.");
        if (Visible("SelectYesno") || Visible("SelectOk")) return new(false, "Answer the open dialog first.");

        this.preset = preset;
        this.owner = owner;
        queue.Clear();
        queue.AddRange(gearsets);
        saved.Clear();
        notSaved.Clear();
        finished = null;
        index = 0;
        Go(Step.Open);
        Plugin.Log.Information($"Putting \"{preset.Name}\" on {gearsets.Count} gear sets.");
        return new(true, Progress);
    }

    public void Stop(string message) => Finish(new ApplyResult(false, message));

    public void Update()
    {
        if (preset == null || DateTime.UtcNow < nextAction) return;
        var gearset = queue[index];
        var label = $"Gear set {index + 1} of {queue.Count} · {Title(gearset)}";

        // While the player has the editor, their own dialogs (like discarding changes) are theirs to answer.
        if (step != Step.Review && (Visible("SelectYesno") || Visible("SelectOk")))
        {
            Stop("The game asked something RePlate didn't expect, so it stopped. Answer it yourself.");
            return;
        }
        if (Visible("CharaCardDesignSetting"))
        {
            Stop("Edit Plate Design opened, so RePlate stopped.");
            return;
        }
        if (step is Step.Open or Step.WaitEditor or Step.Apply or Step.Check && DateTime.UtcNow - stepStarted > StepLimit)
        {
            if (step == Step.WaitEditor)
                Plugin.Log.Information($"{Title(gearset)} (place {gearset.EnabledIndex}, id {gearset.Id}): {PortraitEditor.Describe()}");
            Stop(step == Step.Open && Visible("BannerEditor")
                ? $"Another Edit Portrait is still open, so {Title(gearset)}'s couldn't open. Close it and start again."
                : step is Step.Open or Step.WaitEditor
                    ? $"{Title(gearset)}'s Edit Portrait didn't open, so RePlate stopped."
                    : "That took too long, so RePlate stopped. Edit Portrait is left open; nothing was saved.");
            return;
        }

        switch (step)
        {
            case Step.Open:
                if (Visible("BannerEditor")) return;
                before = Fingerprint(gearset.Id);
                if (Gearsets.OpenEditor(gearset) is { } problem)
                {
                    Stop(problem);
                    return;
                }
                Progress = $"{label}: opening Edit Portrait...";
                triedList = false;
                Go(Step.WaitEditor);
                return;

            case Step.WaitEditor:
                if (PortraitEditor.Ready(owner, gearset.EnabledIndex))
                {
                    Go(Step.Apply);
                    return;
                }
                // Nothing showed up: try the Gear Set list's own Edit Portrait once.
                if (!triedList && !Visible("BannerEditor") && DateTime.UtcNow - stepStarted > TimeSpan.FromSeconds(3))
                {
                    triedList = true;
                    Plugin.Log.Information($"{Title(gearset)}: Edit Portrait didn't open; trying the Gear Set list's own.");
                    if (Gearsets.OpenEditorFromList(gearset) is { } listProblem) Stop(listProblem);
                }
                return;

            case Step.Apply:
                var applied = portraits.Apply(preset, owner, gearset.EnabledIndex);
                if (!applied.Applied)
                {
                    Stop(applied.Message);
                    return;
                }
                Progress = $"{label}: putting the portrait in...";
                Go(Step.Check);
                Wait(SettleTime);
                return;

            case Step.Check:
                var check = portraits.Verify(preset, owner);
                if (!check.Applied && !reapplied)
                {
                    // A freshly opened editor can still be settling its camera; put the portrait in once more.
                    reapplied = true;
                    Go(Step.Apply);
                    return;
                }
                if (!check.Applied)
                {
                    Stop($"{check.Message} Edit Portrait is left open; nothing was saved.");
                    return;
                }
                var warning = portraits.GameWarning is { } w ? $" The game warns that {w}." : "";
                Progress = $"{label}: Portrait applied! Look it over, press Save if it's looking good, then close Edit Portrait. " +
                           $"Closing without saving skips it.{warning}{portraits.Kept}";
                Go(Step.Review);
                return;

            case Step.Review:
                if (Visible("BannerEditor")) return;
                // The gear set's saved portrait changed: the player saved it.
                var changed = Fingerprint(gearset.Id) != before;
                (changed ? saved : notSaved).Add(Title(gearset));
                Plugin.Log.Information($"{Title(gearset)}: {(changed ? "saved" : "not saved")}.");
                Go(Step.Next);
                Wait(TimeSpan.FromSeconds(1));
                return;

            default:
                if (++index < queue.Count)
                {
                    reapplied = false;
                    Go(Step.Open);
                    return;
                }
                Finish(new ApplyResult(true, Summary(), notSaved.Count == 0));
                return;
        }
    }

    private string Summary()
    {
        var text = $"Saved on {saved.Count} of {queue.Count} gear set{(queue.Count == 1 ? "" : "s")}.";
        if (notSaved.Count > 0) text += $" Not saved: {string.Join(", ", notSaved)}.";
        return text;
    }

    private static string Title(GearsetInfo gearset) =>
        gearset.Name.Length > 0 ? $"{gearset.Name} ({Names.Job(gearset.ClassJob)})" : Names.Job(gearset.ClassJob);

    // The gear set's saved portrait as text, to tell whether the player saved.
    private string Fingerprint(int id) =>
        Gearsets.List(owner).FirstOrDefault(g => g.Id == id)?.Portrait is { } portrait ? JsonSerializer.Serialize(portrait) : "none";

    private void Go(Step next)
    {
        step = next;
        stepStarted = DateTime.UtcNow;
        Wait(Pace);
    }

    private void Wait(TimeSpan gap) => nextAction = DateTime.UtcNow + gap;

    private void Finish(ApplyResult result)
    {
        if (preset == null) return;
        // Stopped part way: say what was done so far, too.
        if (!result.Applied && (saved.Count > 0 || notSaved.Count > 0)) result = result with { Message = $"{result.Message} {Summary()}" };
        Plugin.Log.Information($"Gear set run ended: {result.Message}");
        preset = null;
        Progress = "";
        finished = result;
    }

    private static bool Visible(string name)
    {
        var addon = Plugin.GameGui.GetAddonByName(name);
        return !addon.IsNull && addon.IsVisible;
    }
}
