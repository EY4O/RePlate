using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace RePlate.Core.Plates;

/// <summary>What a share code carries: the plate, its name, and the body it was made on. Nothing about the sharer.</summary>
public sealed record SharedPlate(string Name, uint Race, uint Tribe, byte Sex, PortraitSettings? Portrait, PlateDesign? Design,
    PresetKind Kind = PresetKind.Plate, byte ClassJob = 0);

/// <summary>
/// Turns a plate into a short text code and back. The code is "RePlate1:" followed by compressed JSON in URL-safe
/// base64. HaselTweaks' portrait strings are read too, as portraits. Reading one is strict: anything that doesn't
/// look right is refused rather than guessed at.
/// </summary>
public static class ShareCode
{
    public const string Prefix = "RePlate1:";
    private const int MaxCodeLength = 4000;
    private const int MaxJsonBytes = 16 * 1024;
    private const int MaxDecorations = 8;
    // Camera and head/eye numbers are small; anything far outside this didn't come from the game.
    private const float MaxVector = 100f;

    private static readonly JsonSerializerOptions Options = new();

    public static string Encode(PlatePreset preset)
    {
        var shared = new SharedPlate(preset.Name, preset.Race, preset.Tribe, preset.Sex, preset.Portrait, preset.Design,
            preset.Kind, preset.ClassJob);
        var json = JsonSerializer.SerializeToUtf8Bytes(shared, Options);
        using var packed = new MemoryStream();
        using (var deflate = new DeflateStream(packed, CompressionLevel.SmallestSize, true)) deflate.Write(json);
        return Prefix + Convert.ToBase64String(packed.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>Reads a code, or says in plain words why it can't.</summary>
    public static SharedPlate? Decode(string code, out string problem)
    {
        problem = "";
        code = new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (!code.StartsWith(Prefix, StringComparison.Ordinal))
        {
            if (HaselTweaksCode.Decode(code) is { } portrait)
            {
                var fromHasel = new SharedPlate("", 0, 0, 0, portrait, null, PresetKind.Portrait);
                if (Sensible(fromHasel)) return fromHasel;
                problem = "That code has values no portrait could have.";
                return null;
            }
            problem = "That isn't a RePlate or HaselTweaks code.";
            return null;
        }
        if (code.Length > MaxCodeLength)
        {
            problem = "That code is too long to be a plate.";
            return null;
        }

        try
        {
            var body = code[Prefix.Length..].Replace('-', '+').Replace('_', '/');
            var bytes = Convert.FromBase64String(body.PadRight(body.Length + (4 - body.Length % 4) % 4, '='));
            using var deflate = new DeflateStream(new MemoryStream(bytes), CompressionMode.Decompress);
            var json = new byte[MaxJsonBytes + 1];
            var length = 0;
            int read;
            while ((read = deflate.Read(json, length, json.Length - length)) > 0)
            {
                length += read;
                if (length > MaxJsonBytes) throw new InvalidDataException();
            }
            var shared = JsonSerializer.Deserialize<SharedPlate>(json.AsSpan(0, length), Options);
            if (shared == null || !Sensible(shared))
            {
                problem = "That code has values no plate could have.";
                return null;
            }
            return shared with { Name = PlatePreset.CleanName(shared.Name) };
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or JsonException or IOException)
        {
            problem = "That code is damaged. Copy it again, all of it.";
            return null;
        }
    }

    /// <summary>A new plate for this character from a shared one.</summary>
    public static PlatePreset ToPreset(SharedPlate shared, ulong owner)
    {
        var now = DateTimeOffset.UtcNow;
        return new PlatePreset
        {
            Name = shared.Name.Length > 0 ? shared.Name : shared.Kind == PresetKind.Portrait ? "Shared portrait" : "Shared plate",
            CreatedAt = now,
            UpdatedAt = now,
            Owner = owner,
            Race = shared.Race,
            Tribe = shared.Tribe,
            Sex = shared.Sex,
            Portrait = shared.Portrait,
            Design = shared.Design,
            Kind = shared.Kind,
            ClassJob = shared.ClassJob,
            Imported = true,
        };
    }

    private static bool Sensible(SharedPlate shared)
    {
        if (shared.Portrait == null && shared.Design == null) return false;
        if (!Enum.IsDefined(shared.Kind) || shared.Kind == PresetKind.Portrait && (shared.Portrait == null || shared.Design != null))
            return false;
        if (shared.Sex > 1 || shared.Race > 100 || shared.Tribe > 100) return false;
        if (shared.Portrait is { } p)
        {
            if (!p.IsValid()) return false;
            if (p.CameraPosition.Concat(p.CameraTarget).Concat(p.HeadDirection).Concat(p.EyeDirection).Any(v => Math.Abs(v) > MaxVector))
                return false;
            if (Math.Abs(p.AnimationProgress) > 100_000) return false;
        }
        if (shared.Design is { } d && (d.Decorations == null || d.Decorations.Length > MaxDecorations)) return false;
        return true;
    }
}
