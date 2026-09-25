using System.Text.Json;
using RePlate.Core.Images;

namespace RePlate.Core.Plates;

/// <summary>What was found in HaselTweaks' saved portraits, and how many of them couldn't be read.</summary>
public sealed record HaselTweaksPortraits(BackupContents Contents, int Unreadable);

/// <summary>
/// Brings over the portraits saved in HaselTweaks' Portrait Helper. They sit in its settings file (HaselTweaks.json,
/// under Tweaks.PortraitHelper.Presets, each with an id, a name and a preset string) and its pictures in
/// HaselTweaks/Portraits/&lt;id&gt;.png, both beside RePlate's own settings folder. Nothing of HaselTweaks' is changed.
/// </summary>
public static class HaselTweaksLibrary
{
    private const string SettingsFile = "HaselTweaks.json";
    private const int MaxJsonBytes = 16 * 1024 * 1024;
    private const int MaxPresets = 2000;

    /// <summary>Reads HaselTweaks' portraits. <paramref name="pluginConfigs"/> is the folder that holds every plugin's settings.</summary>
    public static HaselTweaksPortraits Read(string pluginConfigs)
    {
        var settings = new FileInfo(Path.Combine(pluginConfigs, SettingsFile));
        if (!settings.Exists) throw new InvalidDataException("HaselTweaks hasn't saved any settings on this PC.");
        if (settings.Length > MaxJsonBytes) throw new InvalidDataException("HaselTweaks' settings are larger than RePlate expects.");

        var found = Parse(File.ReadAllText(settings.FullName));
        var pictures = new Dictionary<Guid, byte[]>();
        foreach (var preset in found.Contents.Presets)
        {
            var path = Path.Combine(pluginConfigs, "HaselTweaks", "Portraits", preset.Id.ToString("D") + ".png");
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > ImageFiles.MaxBytes) continue;
            var png = File.ReadAllBytes(path);
            try
            {
                ImageFiles.ReadHeader(png);
                pictures[preset.Id] = png;
            }
            catch (InvalidDataException)
            {
                // A picture that isn't a PNG RePlate can use is left behind; the portrait still comes over.
            }
        }
        return found with { Contents = found.Contents with { Pictures = pictures } };
    }

    /// <summary>
    /// The portraits in HaselTweaks' settings, each kept under its HaselTweaks id until it's added. Each preset string
    /// goes through the same checks as a pasted one; any that fail are counted, not guessed at.
    /// </summary>
    public static HaselTweaksPortraits Parse(string json)
    {
        var presets = new List<PlatePreset>();
        var unreadable = 0;
        try
        {
            using var document = JsonDocument.Parse(json);
            if (!Child(document.RootElement, "Tweaks", out var tweaks) || !Child(tweaks, "PortraitHelper", out var helper) ||
                !Child(helper, "Presets", out var list) || list.ValueKind != JsonValueKind.Array)
                return new HaselTweaksPortraits(new BackupContents(presets, []), 0);
            if (list.GetArrayLength() > MaxPresets) throw new InvalidDataException("HaselTweaks has more portraits saved than RePlate expects.");

            var now = DateTimeOffset.UtcNow;
            var ids = new HashSet<Guid>();
            foreach (var entry in list.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object || !Child(entry, "Id", out var idValue) ||
                    idValue.ValueKind != JsonValueKind.String || !idValue.TryGetGuid(out var id) ||
                    !Child(entry, "Preset", out var code) || code.ValueKind != JsonValueKind.String ||
                    ShareCode.Decode(code.GetString()!, out _) is not { Kind: PresetKind.Portrait } shared ||
                    !ids.Add(id))
                {
                    unreadable++;
                    continue;
                }
                var name = Child(entry, "Name", out var nameValue) && nameValue.ValueKind == JsonValueKind.String
                    ? PlatePreset.CleanName(nameValue.GetString()!)
                    : "";
                presets.Add(new PlatePreset
                {
                    Id = id,
                    Name = name.Length > 0 ? name : "HaselTweaks portrait",
                    CreatedAt = now,
                    UpdatedAt = now,
                    Kind = PresetKind.Portrait,
                    Portrait = shared.Portrait,
                    // HaselTweaks doesn't say which character or job a portrait was made on, so it's treated like a
                    // shared one: anything locked keeps your own choice, and it stops for you to look it over.
                    Imported = true,
                });
            }
        }
        catch (JsonException)
        {
            throw new InvalidDataException("HaselTweaks' settings couldn't be read.");
        }
        return new HaselTweaksPortraits(new BackupContents(presets, []), unreadable);
    }

    /// <summary>
    /// Adds the portraits to this character. One this character already has, the same down to the last setting, is
    /// left out, so bringing them over twice adds nothing new. Changes the store, so run it where the store is used.
    /// </summary>
    public static BackupImport AddTo(PresetStore store, ImageFiles files, BackupContents found, ulong owner)
    {
        var here = store.For(owner, PresetKind.Portrait)
            .Where(p => p.Portrait != null)
            .Select(p => HaselTweaksCode.Encode(p.Portrait!))
            .ToHashSet();
        int added = 0, alreadyHere = 0;
        foreach (var preset in found.Presets)
        {
            if (!here.Add(HaselTweaksCode.Encode(preset.Portrait!)))
            {
                alreadyHere++;
                continue;
            }
            var haselId = preset.Id;
            preset.Id = Guid.NewGuid();
            preset.Owner = owner;
            if (found.Pictures.TryGetValue(haselId, out var png)) files.Save(preset.Id, png);
            store.Add(preset);
            added++;
        }
        return new BackupImport(added, alreadyHere);
    }

    private static bool Child(JsonElement element, string name, out JsonElement child)
    {
        child = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out child);
    }
}
