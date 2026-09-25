using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using RePlate.Core.Plates;
using RePlate.Game;

namespace RePlate.Windows;

/// <summary>Your saved portraits as a gallery of cards, the selected one on the right, and putting one on your gear sets.</summary>
public sealed class PortraitsTab
{
    private const string FromGearsetPopup = "##fromGearset";
    private const string ApplyPopup = "Put this portrait on gear sets###replateGearsets";
    private const float CardWidth = 150, PictureHeight = 200, DetailWidth = 340;
    private static readonly TimeSpan CheckDelay = TimeSpan.FromSeconds(1);

    private readonly Plugin plugin;
    private readonly PlateImages images;
    private readonly Thumbnails thumbnails;
    private readonly PresetHeader header;
    private readonly ImportPopup import;

    // Gear sets are read from the game on the framework thread; the popup that asked for them opens once they're in.
    private Task<List<GearsetInfo>>? loadingGearsets;
    private string? popupWhenLoaded;
    private List<GearsetInfo> gearsets = [];
    private readonly HashSet<int> ticked = [];

    private Task<string?>? opening;
    private Task<ApplyResult>? applying;
    private Task<ApplyResult>? checking;
    private Task<ApplyResult>? starting;
    private PlatePreset? applied;
    private DateTime checkAt;
    private string filter = "";
    private string status = "";
    private bool statusWarning;
    private Guid selected;

    public PortraitsTab(Plugin plugin, PlateImages images, Thumbnails thumbnails)
    {
        this.plugin = plugin;
        this.images = images;
        this.thumbnails = thumbnails;
        header = new PresetHeader(plugin, images, text => SetStatus(text));
        import = new ImportPopup(plugin);
    }

    public void Draw()
    {
        var owner = plugin.CharacterId;
        if (owner == 0)
        {
            ImGui.TextDisabled("Log in to see your portraits.");
            return;
        }

        FinishTasks(owner);
        var guide = plugin.PortraitGuide;
        guide.Update(Gearsets.PortraitsWindowOpen(), plugin.Store.For(owner, PresetKind.Portrait).Count, images, plugin.GearsetRun.Running);
        DrawTourOffer();
        guide.DrawBar();
        DrawRunBar();
        DrawToolbar(owner);
        if (import.Draw(owner) is { } added)
        {
            if (added.Kind == PresetKind.Plate) SetStatus($"Added \"{added.Name}\" to your adventure plates.");
            else
            {
                filter = "";
                Select(added.Id);
                SetStatus($"Added \"{added.Name}\".");
            }
        }
        DrawFromGearset(owner);
        DrawApplyPopup(owner);
        ImGui.Separator();

        var portraits = plugin.Store.For(owner, PresetKind.Portrait)
            .Where(p => filter.Length == 0 || p.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                        Names.Job(p.ClassJob).Contains(filter, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (portraits.All(p => p.Id != selected)) Select(portraits.FirstOrDefault()?.Id ?? Guid.Empty);

        var detailWidth = DetailWidth * ImGuiHelpers.GlobalScale;
        using (var gallery = ImRaii.Child("##gallery", new Vector2(Math.Max(1, ImGui.GetContentRegionAvail().X - detailWidth), 0), true))
        {
            if (gallery.Success) DrawGallery(portraits);
        }
        ImGui.SameLine();
        using (var detail = ImRaii.Child("##portrait", Vector2.Zero, false))
        {
            if (detail.Success) DrawDetail(plugin.Store.Get(selected));
        }
    }

    // The first time the tab is opened, offer the tour once.
    private void DrawTourOffer()
    {
        var config = plugin.Configuration;
        if (config.PortraitTourOffered || plugin.PortraitGuide.Active) return;
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) ImGui.TextUnformatted("New to Portraits?");
        ImGui.SameLine();
        ImGui.TextUnformatted("A short tour shows you how to save a gear set's portrait and put it on others.");
        if (Theme.PrimaryButton("Show me around")) plugin.StartPortraitTour();
        ImGui.SameLine();
        if (ImGui.Button("No thanks"))
        {
            config.PortraitTourOffered = true;
            plugin.MarkDirty();
        }
        ImGui.Separator();
    }

    // While gear sets are being done: which one, what to do, and Stop.
    private void DrawRunBar()
    {
        var run = plugin.GearsetRun;
        if (!run.Running) return;
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.AccentText)) ImGui.TextWrapped(run.Progress);
        if (Theme.DangerButton("Stop"))
            Plugin.Framework.RunOnFrameworkThread(() => plugin.StopAll("Stopped. Anything not saved yet can be undone by closing the editor without saving."));
        ImGui.Separator();
    }

    private void DrawToolbar(ulong owner)
    {
        using (ImRaii.Disabled(loadingGearsets != null || !plugin.Store.CanWrite))
        {
            if (Theme.PrimaryButton("Save from gear set")) LoadGearsets(owner, FromGearsetPopup);
            plugin.PortraitGuide.Mark(GuideTarget.SaveFromGearset);
        }
        Ui.TipAlways("Saves a copy of one of your gear sets' portraits.");
        ImGui.SameLine();
        using (ImRaii.Disabled(opening != null))
        {
            if (ImGui.Button("Open my Portraits"))
                opening = Plugin.Framework.RunOnFrameworkThread(Gearsets.OpenPortraitsWindow);
            plugin.PortraitGuide.Mark(GuideTarget.OpenPortraits);
        }
        Ui.TipAlways("Opens the game's Portraits window, as from the character menu. Its preview is also what a captured picture is fitted to.");
        ImGui.SameLine();
        if (ImGui.Button("Import")) import.Open();
        Ui.Tip("Add a portrait someone shared with you, from its share code.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(180 * ImGuiHelpers.GlobalScale);
        ImGui.InputTextWithHint("##search", "Search", ref filter, 64);
        if (status.Length > 0)
        {
            ImGui.SameLine();
            if (statusWarning) ImGui.TextColored(Theme.Warning, status);
            else ImGui.TextDisabled(status);
        }
    }

    private void SetStatus(string text, bool warning = false)
    {
        status = text;
        statusWarning = warning;
    }

    private void LoadGearsets(ulong owner, string popup)
    {
        popupWhenLoaded = popup;
        loadingGearsets = Plugin.Framework.RunOnFrameworkThread(() => Gearsets.List(owner));
    }

    // The gear sets that have a portrait; picking one saves a copy of it.
    private void DrawFromGearset(ulong owner)
    {
        using var popup = ImRaii.Popup(FromGearsetPopup);
        if (!popup.Success) return;
        var withPortraits = gearsets.Where(g => g.Portrait != null).ToList();
        if (withPortraits.Count == 0) ImGui.TextDisabled("None of your gear sets has a portrait yet.");
        foreach (var gearset in withPortraits)
        {
            using var id = ImRaii.PushId(gearset.Id);
            Icon(gearset.Icon, ImGui.GetTextLineHeight());
            if (ImGui.Selectable($"{gearset.Id + 1}. {GearsetName(gearset)}"))
                SaveFromGearset(owner, gearset);
            ImGui.SameLine();
            ImGui.TextDisabled(Names.Pose(gearset.Portrait!.Pose));
        }
    }

    private void SaveFromGearset(ulong owner, GearsetInfo gearset)
    {
        var now = DateTimeOffset.UtcNow;
        var preset = new PlatePreset
        {
            Kind = PresetKind.Portrait,
            Name = PlatePreset.CleanName(GearsetName(gearset)),
            CreatedAt = now,
            UpdatedAt = now,
            Owner = owner,
            Race = Plugin.PlayerState.Race.RowId,
            Tribe = Plugin.PlayerState.Tribe.RowId,
            Sex = (byte)Plugin.PlayerState.Sex,
            ClassJob = gearset.ClassJob,
            Portrait = gearset.Portrait,
        };
        plugin.Store.Add(preset);
        plugin.Store.Save();
        filter = "";
        Select(preset.Id);
        SetStatus($"Saved \"{preset.Name}\".");
    }

    private static string GearsetName(GearsetInfo gearset) => gearset.Name.Length > 0 ? gearset.Name : Names.Job(gearset.ClassJob);

    private void DrawGallery(List<PlatePreset> portraits)
    {
        if (portraits.Count == 0)
        {
            ImGui.TextWrapped(filter.Length > 0
                ? "Nothing matches."
                : "No portraits yet. Press Save from gear set to keep a copy of one of your gear sets' portraits.");
            return;
        }
        var scale = ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var columns = Math.Max(1, (int)((ImGui.GetContentRegionAvail().X + spacing) / (CardWidth * scale + spacing)));
        for (var i = 0; i < portraits.Count; i++)
        {
            if (i % columns != 0) ImGui.SameLine();
            DrawCard(portraits[i]);
        }
    }

    // A card: the picture (or the job and pose when there isn't one), then the name and job under it.
    private void DrawCard(PlatePreset preset)
    {
        using var id = ImRaii.PushId(preset.Id.ToString());
        var scale = ImGuiHelpers.GlobalScale;
        var width = CardWidth * scale;
        var pictureSize = new Vector2(width, PictureHeight * scale);
        var line = ImGui.GetTextLineHeightWithSpacing();
        var min = ImGui.GetCursorScreenPos();
        if (ImGui.InvisibleButton("##card", new Vector2(width, pictureSize.Y + line * 2 + 4 * scale))) Select(preset.Id);
        header.Menu(preset, () => Select(preset.Id));
        var hovered = ImGui.IsItemHovered();

        var draw = ImGui.GetWindowDrawList();
        var rounding = 6 * scale;
        var pictureMax = min + pictureSize;
        draw.AddRectFilled(min, pictureMax, ImGui.GetColorU32(ImGuiCol.FrameBg), rounding);
        if (thumbnails.Get(preset.Id) is { } picture)
        {
            // Fill the card, cropping the picture's longer side evenly.
            var (uv0, uv1) = Cover(picture.Size, pictureSize);
            draw.AddImageRounded(picture.Handle, min, pictureMax, uv0, uv1, 0xFFFFFFFF, rounding);
        }
        else
        {
            var iconSize = 48 * scale;
            if (Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(Names.JobIcon(preset.ClassJob))).GetWrapOrDefault() is { } icon &&
                preset.ClassJob != 0)
            {
                var iconMin = new Vector2(min.X + (width - iconSize) / 2, min.Y + pictureSize.Y * 0.3f);
                draw.AddImage(icon.Handle, iconMin, iconMin + new Vector2(iconSize));
            }
            if (preset.Portrait is { } portrait)
            {
                var pose = Names.Pose(portrait.Pose);
                var wrap = width - 12 * scale;
                var size = ImGui.CalcTextSize(pose, false, wrap);
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize(), new Vector2(min.X + (width - size.X) / 2, min.Y + pictureSize.Y * 0.3f + iconSize + 8 * scale),
                    ImGui.GetColorU32(Theme.Muted), pose, wrap);
            }
        }
        if (preset.Favorite)
        {
            using (Plugin.PluginInterface.UiBuilder.IconFontHandle.Push())
                draw.AddText(ImGui.GetFont(), ImGui.GetFontSize() * 0.8f, min + new Vector2(6 * scale), ImGui.GetColorU32(Theme.AccentText),
                    FontAwesomeIcon.Star.ToIconString());
        }
        var selectedCard = preset.Id == selected;
        if (selectedCard || hovered)
            draw.AddRect(min, pictureMax, ImGui.GetColorU32(selectedCard ? Theme.Accent : Theme.Muted), rounding, selectedCard ? 2.5f * scale : 1f * scale);

        var textTop = pictureMax.Y + 3 * scale;
        draw.AddText(new Vector2(min.X, textTop), ImGui.GetColorU32(selectedCard ? Theme.AccentText : ImGui.GetStyle().Colors[(int)ImGuiCol.Text]),
            Fit(preset.Name, width));
        var job = string.Join(" · ", new[] { Names.Job(preset.ClassJob), preset.Imported ? "Shared" : "" }.Where(s => s.Length > 0));
        draw.AddText(new Vector2(min.X, textTop + line), ImGui.GetColorU32(Theme.Muted), Fit(job, width));
    }

    private static (Vector2 Uv0, Vector2 Uv1) Cover(Vector2 picture, Vector2 card)
    {
        var pictureAspect = picture.X / picture.Y;
        var cardAspect = card.X / card.Y;
        if (pictureAspect > cardAspect)
        {
            var part = cardAspect / pictureAspect;
            return (new Vector2((1 - part) / 2, 0), new Vector2((1 + part) / 2, 1));
        }
        var height = pictureAspect / cardAspect;
        return (new Vector2(0, (1 - height) / 2), new Vector2(1, (1 + height) / 2));
    }

    // Shortens text with "..." until it fits the card.
    private static string Fit(string text, float width)
    {
        if (ImGui.CalcTextSize(text).X <= width) return text;
        while (text.Length > 1 && ImGui.CalcTextSize(text + "...").X > width) text = text[..^1];
        return text + "...";
    }

    private static void Icon(uint iconId, float size)
    {
        if (iconId != 0 && Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault() is { } icon)
            ImGui.Image(icon.Handle, new Vector2(size));
        else ImGui.Dummy(new Vector2(size));
        ImGui.SameLine();
    }

    private void DrawDetail(PlatePreset? preset)
    {
        if (preset == null)
        {
            ImGui.TextDisabled("Select a portrait.");
            return;
        }
        var job = Names.Job(preset.ClassJob);
        var made = string.Join(" · ", new[] { job, Names.Race(preset.Race, preset.Sex) }.Where(s => s.Length > 0));
        var subtitle = preset.Imported
            ? $"Shared{(made.Length > 0 ? $", made on {made}" : "")} - Added {preset.CreatedAt.ToLocalTime():MM-dd-yyyy}"
            : $"{made} - Created {preset.CreatedAt.ToLocalTime():MM-dd-yyyy}";
        if (header.Draw(preset, subtitle))
        {
            thumbnails.Forget(preset.Id);
            selected = Guid.Empty;
            return;
        }
        ImGui.Spacing();
        DrawActions(preset);
        ImGui.Spacing();
        ImGui.Separator();
        Summary.Portrait(preset.Portrait);
        ImGui.Separator();
        images.Draw(plugin.Store.CanWrite);
    }

    private bool Busy => plugin.GearsetRun.Running || plugin.Restore.Running || plugin.Designs.Running ||
                         starting != null || applying != null || applied != null || loadingGearsets != null;

    private void DrawActions(PlatePreset preset)
    {
        using (ImRaii.Disabled(Busy))
        {
            if (Theme.PrimaryButton("Apply to gear sets..."))
            {
                ticked.Clear();
                LoadGearsets(plugin.CharacterId, ApplyPopup);
            }
            plugin.PortraitGuide.Mark(GuideTarget.ApplyToGearsets);
            Ui.TipAlways("Puts this portrait on the gear sets you pick, one at a time. Each opens for you to look over and save.");
            ImGui.SameLine();
            if (ImGui.Button("Apply Portrait")) StartApply(preset);
            Ui.TipAlways("Puts this portrait into the Edit Portrait you have open, a gear set's or your plate's, without saving.");
        }
    }

    // Tick the gear sets, then Start: they're done one at a time, each left for you to save.
    private void DrawApplyPopup(ulong owner)
    {
        var scale = ImGuiHelpers.GlobalScale;
        ImGui.SetNextWindowSize(new Vector2(520, 0) * scale, ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(ApplyPopup, ImGuiWindowFlags.AlwaysAutoResize)) return;
        var preset = plugin.Store.Get(selected);
        ImGui.TextWrapped($"Put \"{preset?.Name}\" on these gear sets. They open one at a time for you to look over and save; " +
                          "anything a gear set's job can't use keeps what that gear set has.");
        ImGui.Spacing();
        using (var list = ImRaii.Child("##gearsets", new Vector2(480 * scale, Math.Min(360, 26 * Math.Max(1, gearsets.Count)) * scale), true))
        {
            if (list.Success)
            {
                if (gearsets.Count == 0) ImGui.TextDisabled("You have no gear sets.");
                foreach (var gearset in gearsets)
                {
                    using var id = ImRaii.PushId(gearset.Id);
                    var on = ticked.Contains(gearset.Id);
                    if (ImGui.Checkbox("##tick", ref on))
                    {
                        if (on) ticked.Add(gearset.Id);
                        else ticked.Remove(gearset.Id);
                    }
                    ImGui.SameLine();
                    Icon(gearset.Icon, ImGui.GetFrameHeight());
                    ImGui.AlignTextToFramePadding();
                    ImGui.TextUnformatted($"{gearset.Id + 1}. {GearsetName(gearset)}");
                    ImGui.SameLine();
                    ImGui.TextDisabled(gearset.Portrait is { } p ? Names.Pose(p.Pose) : "No portrait yet");
                }
            }
        }
        if (ImGui.SmallButton("Select all")) ticked.UnionWith(gearsets.Select(g => g.Id));
        ImGui.SameLine();
        if (ImGui.SmallButton("Select none")) ticked.Clear();
        ImGui.Separator();
        using (ImRaii.Disabled(ticked.Count == 0 || preset == null))
        {
            if (Theme.PrimaryButton($"Start ({ticked.Count})") && preset != null)
            {
                var picked = gearsets.Where(g => ticked.Contains(g.Id)).ToList();
                SetStatus("Starting...");
                starting = Plugin.Framework.RunOnFrameworkThread(() => plugin.GearsetRun.Start(preset, owner, picked));
                ImGui.CloseCurrentPopup();
            }
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel")) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    private void StartApply(PlatePreset preset)
    {
        var owner = plugin.CharacterId;
        applied = preset;
        SetStatus("Applying...");
        // Into whichever Edit Portrait is open: a gear set's, or your plate's.
        applying = Plugin.Framework.RunOnFrameworkThread(() => plugin.Editor.Apply(preset, owner, PortraitEditor.OpenGearset()));
    }

    private void FinishTasks(ulong owner)
    {
        if (opening is { IsCompleted: true } open)
        {
            opening = null;
            if (!open.IsCompletedSuccessfully) SetStatus("Couldn't open the Portraits window.", true);
            else if (open.Result is { } problem) SetStatus(problem, true);
        }

        if (loadingGearsets is { IsCompleted: true } loaded)
        {
            loadingGearsets = null;
            gearsets = loaded.IsCompletedSuccessfully ? loaded.Result : [];
            if (!loaded.IsCompletedSuccessfully) Plugin.Log.Error(loaded.Exception!, "Reading gear sets failed");
            if (popupWhenLoaded != null) ImGui.OpenPopup(popupWhenLoaded);
            popupWhenLoaded = null;
        }

        if (starting is { IsCompleted: true } start)
        {
            starting = null;
            if (!start.IsCompletedSuccessfully)
            {
                SetStatus("Couldn't start.", true);
                Plugin.Log.Error(start.Exception!, "Starting the gear set run failed");
            }
            else SetStatus(start.Result.Message, !start.Result.Applied);
        }
        if (plugin.GearsetRun.TakeResult() is { } result) SetStatus(result.Message, !result.Clean);

        // Apply Portrait: apply, wait for the pose to settle, then read the editor back.
        if (applying is { IsCompleted: true } apply)
        {
            applying = null;
            if (!apply.IsCompletedSuccessfully)
            {
                SetStatus("Couldn't apply the portrait.", true);
                Plugin.Log.Error(apply.Exception!, "Applying the portrait failed");
                applied = null;
                return;
            }
            SetStatus(apply.Result.Message, !apply.Result.Applied);
            if (apply.Result.Applied) checkAt = DateTime.UtcNow + CheckDelay;
            else applied = null;
        }
        if (applied is { } preset && checking == null && applying == null && DateTime.UtcNow >= checkAt)
            checking = Plugin.Framework.RunOnFrameworkThread(() => plugin.Editor.Verify(preset, owner));
        if (checking is not { IsCompleted: true } check) return;
        checking = null;
        applied = null;
        if (!check.IsCompletedSuccessfully)
        {
            SetStatus("Couldn't check the editor.", true);
            Plugin.Log.Error(check.Exception!, "Checking the portrait failed");
            return;
        }
        SetStatus(check.Result.Message, !check.Result.Clean);
    }

    private void Select(Guid id)
    {
        if (id == selected) return;
        selected = id;
        header.Reset();
        images.Select(id);
    }
}
