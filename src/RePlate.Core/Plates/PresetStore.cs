using System.Text.Json;

namespace RePlate.Core.Plates;

/// <summary>Saved plates for every character, kept in plates.json.</summary>
public sealed class PresetStore
{
    private readonly string path;
    private readonly List<PlatePreset> presets = [];
    private bool canWrite = true;

    private sealed record Document(int Version, List<PlatePreset> Presets);

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public PresetStore(string path)
    {
        this.path = path;
        if (!File.Exists(path)) return;
        try
        {
            var document = JsonSerializer.Deserialize<Document>(File.ReadAllText(path), Options);
            if (document is not { Version: 1, Presets: not null } ||
                document.Presets.Any(p => p is null || p.Id == Guid.Empty || p.Owner == 0 || p.Portrait is { } portrait && !portrait.IsValid()))
                throw new JsonException("The file is not a RePlate library.");
            presets.AddRange(document.Presets);
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

    /// <summary>This character's plates: favourites first, then newest first.</summary>
    public IReadOnlyList<PlatePreset> For(ulong owner) =>
        presets.Where(p => p.Owner == owner).OrderByDescending(p => p.Favorite).ThenByDescending(p => p.UpdatedAt).ToList();

    public PlatePreset? Get(Guid id) => presets.FirstOrDefault(p => p.Id == id);

    public void Add(PlatePreset preset)
    {
        presets.RemoveAll(p => p.Id == preset.Id);
        presets.Add(preset);
    }

    public bool Remove(Guid id) => presets.RemoveAll(p => p.Id == id) > 0;

    public void Save()
    {
        if (!canWrite) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
            File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Document(1, presets), Options));
            File.Move(path + ".tmp", path, true);
            Error = null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Error = "Couldn't save your plates: " + ex.Message;
        }
    }
}
