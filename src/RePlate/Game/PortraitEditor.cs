using System;
using System.Collections.Generic;
using System.Linq;
using Dalamud.Game.ClientState.Conditions;
using FFXIVClientStructs.FFXIV.Client.UI;
using FFXIVClientStructs.FFXIV.Client.UI.Agent;
using FFXIVClientStructs.FFXIV.Client.UI.Misc;
using FFXIVClientStructs.FFXIV.Component.GUI;
using RePlate.Core.Plates;
using Bounds = FFXIVClientStructs.FFXIV.Common.Math.Bounds;

namespace RePlate.Game;

/// <summary>Applied is false when nothing was changed or the editor didn't take it; Clean when there's nothing to warn about.</summary>
public sealed record ApplyResult(bool Applied, string Message, bool Clean = false);

/// <summary>
/// Puts a saved portrait into the open Edit Portrait window, the way picking each option would. Nothing is saved;
/// the player looks it over and presses Save or Cancel. Call from the framework thread.
/// </summary>
public sealed unsafe class PortraitEditor
{
    private static readonly string[] ListParts = ["background", "frame", "accent", "pose", "expression"];

    private bool refreshPlayCheckbox;
    // What the last Apply actually put in (a shared plate keeps your choice for anything locked), and a note saying so.
    private PortraitSettings? target;
    private string kept = "";
    // The editor the last Apply went into, so Verify reads the same one.
    private int verifying = -1;

    /// <summary>After Verify: " Kept yours for: ..." when a shared plate had something locked, else empty.</summary>
    public string Kept => kept;

    /// <summary>After Verify: what the game warns about the framing, or null.</summary>
    public string? GameWarning { get; private set; }

    /// <summary>
    /// Puts the portrait into your plate's Edit Portrait, or gear set N's (gearset: its place in the game's list). On a
    /// gear set, or with a shared plate, anything this character can't pick keeps what's there; on your own plate it's
    /// refused instead.
    /// </summary>
    public ApplyResult Apply(PlatePreset preset, ulong owner, int gearset = -1)
    {
        if (preset.Portrait is not { } portrait || preset.Owner != owner) return new(false, "This plate has no portrait to apply.");
        var problem = GetEditor(owner, out var state, out var addon, gearset);
        if (problem != null) return new(false, problem);
        verifying = gearset;

        var view = state->CharaView;
        ExportedPortraitData before;
        view->ExportPortraitData(&before);
        var current = PortraitData.FromGame(before, before.BannerBg, state->BannerEntry.BannerFrame, state->BannerEntry.BannerDecoration);

        var missing = Unavailable(state, portrait);
        kept = "";
        if (!preset.Imported && gearset < 0 && Refusal(missing, portrait) is { } refusal) return new(false, refusal);
        if (missing.Count > 0)
        {
            kept = " Kept yours for: " + string.Join(", ", missing.Select(m =>
                $"{ListParts[m.List]} {Name(m.List, ListIds(portrait)[m.List])} ({(m.OtherJob ? "another job's" : "not unlocked")})")) + ".";
            portrait = KeepYours(portrait, current, missing);
        }
        target = portrait;

        // Only refresh the controls that show the current values correctly; the rest are left for the player to see.
        var sliders = Sliders(addon);
        var currentValues = SliderValues(current);
        var sliderOk = new bool[sliders.Length];
        for (var i = 0; i < sliders.Length; i++)
            sliderOk[i] = sliders[i] != null && sliders[i]->Value == currentValues[i];
        var sets = Datasets(state);
        var currentIds = ListIds(current);
        var listOk = new bool[sets.Length];
        for (var i = 0; i < sets.Length; i++)
        {
            var list = Dropdown(addon, i + 1);
            listOk[i] = list != null && list->GetSelectedItemIndex() == IndexOf(sets[i], currentIds[i]);
        }
        refreshPlayCheckbox = addon->PlayAnimationCheckbox != null && addon->PlayAnimationCheckbox->IsChecked == !view->IsAnimationPaused();

        var data = PortraitData.ToGame(portrait);
        view->ImportPortraitData(&data);
        state->SetFrame(portrait.Frame);
        state->SetAccent(portrait.Accent);

        var values = SliderValues(portrait);
        for (var i = 0; i < sliders.Length; i++)
            if (sliderOk[i]) sliders[i]->SetValue(values[i], false);
        var ids = ListIds(portrait);
        for (var i = 0; i < sets.Length; i++)
            if (listOk[i] && IndexOf(sets[i], ids[i]) is >= 0 and var index) Dropdown(addon, i + 1)->SelectItem(index);
        // The design preset list is left alone: its rows don't line up with the game's preset lookup.

        state->SetHasChanged(true);

        var skipped = ListParts.Where((_, i) => !listOk[i]).ToList();
        if (sliderOk.Contains(false)) skipped.Add($"{sliderOk.Count(ok => !ok)} sliders");
        if (skipped.Count > 0) Plugin.Log.Debug($"Edit Portrait controls left as they were: {string.Join(", ", skipped)}");
        return new(true, "Applied. Checking...");
    }

    /// <summary>Reads the editor back once the pose has settled.</summary>
    public ApplyResult Verify(PlatePreset preset, ulong owner)
    {
        if ((target ?? preset.Portrait) is not { } portrait) return new(false, "This plate has no portrait.");
        var problem = GetEditor(owner, out var state, out var addon, verifying);
        if (problem != null) return new(false, "Edit Portrait closed before it could be checked.");

        var view = state->CharaView;
        if (refreshPlayCheckbox && addon->PlayAnimationCheckbox != null)
            addon->PlayAnimationCheckbox->SetChecked(!view->IsAnimationPaused());
        ExportedPortraitData after;
        view->ExportPortraitData(&after);
        var actual = PortraitData.FromGame(after, after.BannerBg, state->BannerEntry.BannerFrame, state->BannerEntry.BannerDecoration);

        var differences = PortraitCheck.Differences(portrait, actual);
        if (differences.Contains("camera"))
            Plugin.Log.Debug($"Camera wanted {Camera(portrait)}, editor has {Camera(actual)}");
        var error = view->GetPortraitError();
        GameWarning = error != CharaViewPortrait.PortraitError.None ? Warning(error) : null;
        var result = differences.Count > 0
            ? new ApplyResult(false, $"Some parts didn't take: {string.Join(", ", differences)}. Nothing was saved; press Cancel in the editor to undo.")
            : error != CharaViewPortrait.PortraitError.None
                ? new ApplyResult(true, $"Applied, but the game warns that {Warning(error)}. Adjust it before saving, or press Cancel.{kept}")
                : new ApplyResult(true, $"Applied. Look it over and press Save in the editor.{kept}", kept.Length == 0);
        Plugin.Log.Information($"Applied \"{preset.Name}\": {result.Message}");
        return result;
    }

    private static string Camera(PortraitSettings p) =>
        $"position [{string.Join(", ", p.CameraPosition)}] target [{string.Join(", ", p.CameraTarget)}] zoom {p.CameraZoom} rotation {p.ImageRotation}";

    /// <summary>
    /// The open Edit Portrait, if it's the one asked for: your plate's (gearset -1), or gear set N's by its place in the
    /// game's list of existing sets.
    /// </summary>
    internal static string? GetEditor(ulong owner, out AgentBannerEditorState* state, out AddonBannerEditor* editor, int gearset = -1)
    {
        state = null;
        editor = null;
        if (gearset < 0 && PlateReader.GetOwnCard(owner, out _) is { } problem) return problem;
        if (gearset >= 0)
        {
            var module = RaptureGearsetModule.Instance();
            if (module == null || module->CharacterContentId != owner) return "Log in first.";
        }
        var addon = Plugin.GameGui.GetAddonByName("BannerEditor");
        if (addon.IsNull || !addon.IsVisible || !Plugin.Condition[ConditionFlag.EditingPortrait])
            return gearset < 0 ? "Open Edit Portrait from your adventurer plate first." : "Edit Portrait isn't open.";
        var pointer = Plugin.GameGui.GetAgentById((int)AgentId.BannerEditor);
        var agent = pointer.IsNull ? null : (AgentBannerEditor*)pointer.Address;
        if (!addon.IsReady || agent == null || agent->AddonId != addon.Id || agent->EditorState == null ||
            agent->EditorState->AgentBannerEditor != agent)
            return "Edit Portrait is still opening.";
        var s = agent->EditorState;
        if (gearset < 0 && s->OpenType != AgentBannerEditorState.EditorOpenType.AdventurerPlate)
            return "That's a gear set's portrait. Open Edit Portrait from your adventurer plate instead.";
        if (gearset >= 0 && (s->OpenType != AgentBannerEditorState.EditorOpenType.Gearset || s->OpenerEnabledGearsetIndex != gearset))
            return "That Edit Portrait is for something else.";
        if (s->CloseDialogAddonId != 0) return "Answer the editor's dialog first.";
        if (!float.IsFinite(s->FrameCountdown) || s->FrameCountdown > 0 || s->CharaView == null ||
            !s->CharaView->CharaViewPortraitCharacterLoaded)
            return "Edit Portrait is still loading.";
        state = s;
        editor = (AddonBannerEditor*)addon.Address;
        return null;
    }

    /// <summary>
    /// Where a portrait is on screen, for suggesting a crop: the game's Portraits window if it's open (its framed
    /// preview), otherwise Edit Portrait's preview. Null when neither is open.
    /// </summary>
    public static (System.Numerics.Vector2 Position, System.Numerics.Vector2 Size)? Window()
    {
        var looked = new List<string>();

        // The preview beside the Portraits list can be a window of its own.
        var preview = Plugin.GameGui.GetAddonByName("BannerPreview");
        if (!preview.IsNull && preview.IsVisible && UprightPicture((AtkUnitBase*)preview.Address, looked) is { } shown) return shown;

        // The Portraits window has no struct of its own, so it's found through its agent.
        var pointer = Plugin.GameGui.GetAgentById((int)AgentId.BannerList);
        var portraits = pointer.IsNull ? null : (AgentInterface*)pointer.Address;
        var stage = AtkStage.Instance();
        if (portraits != null && portraits->IsAgentActive() && stage != null)
        {
            var unit = stage->RaptureAtkUnitManager->GetAddonById((ushort)portraits->AddonId);
            if (unit != null && unit->IsVisible && UprightPicture(unit, looked) is { } listed) return listed;
        }

        var editor = Plugin.GameGui.GetAddonByName("BannerEditor");
        if (!editor.IsNull && editor.IsVisible && UprightPicture((AtkUnitBase*)editor.Address, looked) is { } editing) return editing;

        // Nothing fitted: say what was there, so the right picture can be found.
        Plugin.Log.Debug($"No portrait found for the crop. Portraits agent {(portraits == null ? "missing" : portraits->IsAgentActive() ? "open" : "closed")}; " +
                               (looked.Count == 0 ? "no portrait windows open." : string.Join("; ", looked)));
        return editor.IsNull || !editor.IsVisible ? null : (editor.Position, editor.ScaledSize);
    }

    // The preview is the window's largest upright picture, looking inside its parts too; anything much smaller is an icon or a
    // border. What was seen goes into `looked` for the log.
    private static (System.Numerics.Vector2 Position, System.Numerics.Vector2 Size)? UprightPicture(AtkUnitBase* unit, List<string> looked)
    {
        var pictures = new List<(uint Node, Bounds Bounds)>();
        Pictures(&unit->UldManager, pictures, 0);
        // A portrait stands upright; the window's own backgrounds are wide, so only tall pictures count.
        var tall = pictures.Where(p => p.Bounds.Height > p.Bounds.Width * 1.2f && p.Bounds.Width * p.Bounds.Height >= 150 * 150)
            .OrderByDescending(p => p.Bounds.Width * p.Bounds.Height).ToList();
        var seen = string.Join(", ", tall.Take(4).Select(p => $"#{p.Node} {p.Bounds.Width}x{p.Bounds.Height}"));
        var size = unit->RootNode == null ? "" : $" {unit->RootNode->Width}x{unit->RootNode->Height}";
        looked.Add($"{unit->NameString}{size}: {(seen.Length > 0 ? seen : "no upright pictures")}");
        if (tall.Count == 0) return null;
        var best = tall[0];
        Plugin.Log.Debug($"Portrait for the crop: {unit->NameString} node #{best.Node}, {best.Bounds.Width}x{best.Bounds.Height} " +
                               $"at {best.Bounds.Pos1.X},{best.Bounds.Pos1.Y} (upright pictures: {seen}).");
        return (new System.Numerics.Vector2(best.Bounds.Pos1.X, best.Bounds.Pos1.Y),
                new System.Numerics.Vector2(best.Bounds.Width, best.Bounds.Height));
    }

    private static void Pictures(AtkUldManager* manager, List<(uint, Bounds)> found, int depth)
    {
        if (depth > 4) return;
        for (var i = 0; i < manager->NodeListCount; i++)
        {
            var node = manager->NodeList[i];
            if (node == null || !node->IsVisible()) continue;
            if (node->Type == NodeType.Image)
            {
                Bounds bounds;
                node->GetBounds(&bounds);
                found.Add((node->NodeId, bounds));
                continue;
            }
            var component = node->GetAsAtkComponentNode();
            if (component != null && component->Component != null) Pictures(&component->Component->UldManager, found, depth + 1);
        }
    }

    /// <summary>Whether the Edit Portrait asked for has unsaved changes; null while it isn't open and settled.</summary>
    public static bool? Unsaved(ulong owner, int gearset = -1) =>
        GetEditor(owner, out var state, out _, gearset) == null ? state->HasDataChanged : null;

    /// <summary>Closes Edit Portrait with its own close button. Only for when nothing is left unsaved.</summary>
    public static bool Close()
    {
        var addon = Plugin.GameGui.GetAddonByName("BannerEditor");
        return !addon.IsNull && addon.IsVisible && Clicks.ButtonWithParam((AtkUnitBase*)addon.Address, 8);
    }

    /// <summary>True when the Edit Portrait asked for is open and ready to take a portrait.</summary>
    public static bool Ready(ulong owner, int gearset = -1) => GetEditor(owner, out _, out _, gearset) == null;

    /// <summary>What Edit Portrait looks like right now, for the log when a gear set's doesn't open as expected.</summary>
    public static string Describe()
    {
        var addon = Plugin.GameGui.GetAddonByName("BannerEditor");
        var pointer = Plugin.GameGui.GetAgentById((int)AgentId.BannerEditor);
        var agent = pointer.IsNull ? null : (AgentBannerEditor*)pointer.Address;
        var state = agent == null ? null : agent->EditorState;
        return $"window {(addon.IsNull ? "none" : addon.IsVisible ? "visible" : "hidden")}, agent {(agent == null ? "none" : agent->IsAgentActive() ? "active" : "idle")}" +
               (state == null ? ", no state" : $", opened for {state->OpenType}, gear set place {state->OpenerEnabledGearsetIndex}, " +
                                              $"countdown {state->FrameCountdown}, loaded {state->CharaView != null && state->CharaView->CharaViewPortraitCharacterLoaded}");
    }

    /// <summary>Which gear set's Edit Portrait is open (its place in the game's list), or -1 when it's not a gear set's.</summary>
    public static int OpenGearset()
    {
        var pointer = Plugin.GameGui.GetAgentById((int)AgentId.BannerEditor);
        var agent = pointer.IsNull ? null : (AgentBannerEditor*)pointer.Address;
        if (agent == null || agent->EditorState == null || !Plugin.GameGui.GetAddonByName("BannerEditor").IsVisible) return -1;
        return agent->EditorState->OpenType == AgentBannerEditorState.EditorOpenType.Gearset
            ? agent->EditorState->OpenerEnabledGearsetIndex
            : -1;
    }

    // Same order as the editor's lists after the design preset: background, frame, accent, pose, expression.
    private static AgentBannerEditorState.Dataset*[] Datasets(AgentBannerEditorState* s) =>
        new[] { &s->Backgrounds, &s->Frames, &s->Accents, &s->Poses, &s->Expressions };

    private static uint[] ListIds(PortraitSettings p) => [p.Background, p.Frame, p.Accent, p.Pose, p.Expression];

    // The lists (background, frame, accent, pose, expression) whose saved choice this character can't pick, and why.
    private static List<(int List, bool OtherJob)> Unavailable(AgentBannerEditorState* state, PortraitSettings portrait)
    {
        var sets = Datasets(state);
        var ids = ListIds(portrait);
        var result = new List<(int, bool)>();
        for (var i = 0; i < sets.Length; i++)
        {
            // Zero means none, which the lists don't carry.
            if (ids[i] == 0) continue;
            var entry = Find(sets[i], ids[i]);
            if (entry == null) result.Add((i, false));
            else if (i == 3 && !entry->ClassJobMatches) result.Add((i, true));
        }
        return result;
    }

    // Your own plates are refused when something's missing; a shared one keeps your choice for it instead.
    private static string? Refusal(List<(int List, bool OtherJob)> missing, PortraitSettings portrait)
    {
        if (missing.FirstOrDefault(m => m.OtherJob) is { OtherJob: true })
            return $"The pose {Names.Pose(portrait.Pose)} belongs to another job. Change job and try again.";
        return missing.Count > 0
            ? $"Not unlocked on this character: {string.Join(", ", missing.Select(m => $"{ListParts[m.List]} {Name(m.List, ListIds(portrait)[m.List])}"))}."
            : null;
    }

    private static PortraitSettings KeepYours(PortraitSettings shared, PortraitSettings yours, List<(int List, bool OtherJob)> missing)
    {
        foreach (var (list, _) in missing)
            shared = list switch
            {
                0 => shared with { Background = yours.Background },
                1 => shared with { Frame = yours.Frame },
                2 => shared with { Accent = yours.Accent },
                3 => shared with { Pose = yours.Pose, AnimationProgress = yours.AnimationProgress },
                _ => shared with { Expression = yours.Expression },
            };
        return shared;
    }

    private static string Name(int list, uint id) => list switch
    {
        0 => Names.Background((ushort)id),
        1 => Names.Frame((ushort)id),
        2 => Names.Accent((ushort)id),
        3 => Names.Pose((ushort)id),
        _ => Names.Expression((byte)id),
    };

    private static AgentBannerEditorState.DatasetEntry* Find(AgentBannerEditorState.Dataset* set, uint id)
    {
        var index = IndexOf(set, id);
        return index < 0 ? null : set->UnlockedEntries[index];
    }

    private static int IndexOf(AgentBannerEditorState.Dataset* set, uint id)
    {
        if (set->UnlockedEntries == null) return -1;
        for (var i = 0; i < set->UnlockedEntriesCount; i++)
            if (set->UnlockedEntries[i] != null && set->UnlockedEntries[i]->RowId == id) return i;
        return -1;
    }

    private static AtkComponentDropDownList* Dropdown(AddonBannerEditor* addon, int index) =>
        index < addon->Dropdowns.Length ? addon->Dropdowns[index].Dropdown : null;

    private static AtkComponentSlider*[] Sliders(AddonBannerEditor* a) => new[]
    {
        a->CameraZoomSlider, a->ImageRotation,
        a->DirectionalLightingColorRedSlider, a->DirectionalLightingColorGreenSlider, a->DirectionalLightingColorBlueSlider,
        a->DirectionalLightingBrightnessSlider, a->DirectionalLightingVerticalAngleSlider, a->DirectionalLightingHorizontalAngleSlider,
        a->AmbientLightingColorRedSlider, a->AmbientLightingColorGreenSlider, a->AmbientLightingColorBlueSlider,
        a->AmbientLightingBrightnessSlider,
    };

    private static int[] SliderValues(PortraitSettings p) =>
    [
        p.CameraZoom, p.ImageRotation,
        p.DirectionalRed, p.DirectionalGreen, p.DirectionalBlue,
        p.DirectionalBrightness, p.DirectionalVerticalAngle, p.DirectionalHorizontalAngle,
        p.AmbientRed, p.AmbientGreen, p.AmbientBlue,
        p.AmbientBrightness,
    ];

    private static string Warning(CharaViewPortrait.PortraitError error)
    {
        var parts = new List<string>();
        if (error.HasFlag(CharaViewPortrait.PortraitError.CharacterNotInFrame)) parts.Add("your character isn't in frame");
        if (error.HasFlag(CharaViewPortrait.PortraitError.ExpressionNotInFrame)) parts.Add("the face isn't in frame");
        if (error.HasFlag(CharaViewPortrait.PortraitError.CameraTooClose)) parts.Add("the camera is too close");
        if (error.HasFlag(CharaViewPortrait.PortraitError.CameraTooFar)) parts.Add("the camera is too far");
        if (error.HasFlag(CharaViewPortrait.PortraitError.Obstructed)) parts.Add("something is in the way");
        return parts.Count > 0 ? string.Join(" and ", parts) : "something is off";
    }
}
