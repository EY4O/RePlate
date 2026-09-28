using System.Text.Json;
using System.Text.Json.Serialization;

namespace RePlate.Core.Plates;

/// <summary>
/// Saved plates for every character, kept in plates.json, along with each character's plate from before their last
/// restore so it can be undone.
/// </summary>
public sealed class PresetStore
{
    private readonly string path;
    private readonly List<PlatePreset> presets = [];
    private readonly List<PlatePreset> undo = [];
    private bool canWrite = true;

    // Undo is left out of backups and of files with none, and older versions of RePlate skip over it.
    private sealed record Document(int Version, List<PlatePreset> Presets,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] List<PlatePreset>? Undo = null);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public PresetStore(string path)
    {
        this.path = path;
        if (!File.Exists(path)) return;
        try
        {
            var document = ReadDocument(File.ReadAllText(path));
            presets.AddRange(document.Presets);
            // An undo that doesn't hold together is only dropped; it's not worth refusing the whole library over.
            undo.AddRange((document.Undo ?? []).Where(p => Sound(p) && p.Kind == PresetKind.Plate)
                .GroupBy(p => p.Owner).Select(g => g.Last()));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Leave a file we can't read alone rather than overwrite it.
            canWrite = false;
            Error = "Saved plates could not be read, so changes won't be saved this session: " + ex.Message;
        }
    }

    public string? Error { get; private set; }
    public bool Exists => File.Exists(path);
    public bool CanWrite => canWrite;

    /// <summary>This character's plates or portraits: favourites first, then newest first.</summary>
    public IReadOnlyList<PlatePreset> For(ulong owner, PresetKind kind = PresetKind.Plate) =>
        presets.Where(p => p.Owner == owner && p.Kind == kind)
            .OrderByDescending(p => p.Favorite).ThenByDescending(p => p.UpdatedAt).ToList();

    /// <summary>Everything saved, for a backup. Optionally just one character's.</summary>
    public IReadOnlyList<PlatePreset> All(ulong? owner = null) =>
        presets.Where(p => owner == null || p.Owner == owner).ToList();

    public PlatePreset? Get(Guid id) => presets.FirstOrDefault(p => p.Id == id);

    public void Add(PlatePreset preset)
    {
        presets.RemoveAll(p => p.Id == preset.Id);
        presets.Add(preset);
    }

    public bool Remove(Guid id) => presets.RemoveAll(p => p.Id == id) > 0;

    /// <summary>What this character's plate had before their last restore, or null.</summary>
    public PlatePreset? UndoFor(ulong owner) => undo.FirstOrDefault(p => p.Owner == owner);

    /// <summary>Keeps a plate as its owner's undo, in place of the one before.</summary>
    public void SetUndo(PlatePreset plate)
    {
        undo.RemoveAll(p => p.Owner == plate.Owner);
        undo.Add(plate);
    }

    /// <summary>The library file's contents, as plates.json and backups hold it.</summary>
    public static string Write(IEnumerable<PlatePreset> presets) => JsonSerializer.Serialize(new Document(1, presets.ToList()), Options);

    /// <summary>Reads a library file, refusing it whole if anything in it doesn't hold together.</summary>
    public static List<PlatePreset> Read(string json) => ReadDocument(json).Presets;

    private static Document ReadDocument(string json)
    {
        var document = JsonSerializer.Deserialize<Document>(json, Options);
        if (document is not { Version: 1, Presets: not null } || document.Presets.Any(p => !Sound(p)))
            throw new JsonException("The file is not a RePlate library.");
        return document;
    }

    private static bool Sound(PlatePreset? p) =>
        p != null && p.Id != Guid.Empty && p.Owner != 0 && (p.Portrait != null || p.Design != null) &&
        (p.Portrait == null || p.Portrait.IsValid()) && (p.Design == null || p.Design.Decorations is { Length: <= 16 });

    public void Save()
    {
        if (!canWrite) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Document(1, presets, undo.Count > 0 ? undo : null), Options));
            File.Move(path + ".tmp", path, true);
            Error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Couldn't save your plates: " + ex.Message;
        }
    }
}
