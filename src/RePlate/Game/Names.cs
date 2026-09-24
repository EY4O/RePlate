using Lumina.Excel.Sheets;
using RePlate.Core.Plates;

namespace RePlate.Game;

public enum DecorationKind : byte { Backing = 1, Pattern = 2, PortraitFrame = 3, PlateFrame = 4, Accent = 5 }

/// <summary>Display names for portrait and plate choices, from the game's sheets.</summary>
public static class Names
{
    public static string Pose(ushort id) => Plugin.DataManager.GetExcelSheet<BannerTimeline>().GetRowOrDefault(id) is { } row
        ? Text(row.Name.ExtractText(), id) : Unknown(id);

    public static string Expression(byte id)
    {
        if (id == 0) return "None";
        return Plugin.DataManager.GetExcelSheet<BannerFacial>().GetRowOrDefault(id) is { } row &&
               row.Emote.ValueNullable is { } emote
            ? Text(emote.Name.ExtractText(), id) : Unknown(id);
    }

    public static string Background(ushort id) => Plugin.DataManager.GetExcelSheet<BannerBg>().GetRowOrDefault(id) is { } row
        ? Text(row.Name.ExtractText(), id) : Unknown(id);

    public static string Frame(ushort id) => Plugin.DataManager.GetExcelSheet<BannerFrame>().GetRowOrDefault(id) is { } row
        ? Text(row.Name.ExtractText(), id) : Unknown(id);

    public static string Accent(ushort id) => Plugin.DataManager.GetExcelSheet<BannerDecoration>().GetRowOrDefault(id) is { } row
        ? Text(row.Name.ExtractText(), id) : Unknown(id);

    public static string BasePlate(ushort id) => Plugin.DataManager.GetExcelSheet<CharaCardBase>().GetRowOrDefault(id) is { } row
        ? Text(row.Name.ExtractText(), id) : Unknown(id);

    public static string Border(byte id)
    {
        if (id == 0) return "None";
        return Plugin.DataManager.GetExcelSheet<CharaCardHeader>().GetRowOrDefault(id) is { } row
            ? Text(row.Name.ExtractText(), id) : Unknown(id);
    }

    /// <summary>The design's decoration of this kind, or None when it has none.</summary>
    public static string Decoration(PlateDesign design, DecorationKind kind)
    {
        var sheet = Plugin.DataManager.GetExcelSheet<CharaCardDecoration>();
        foreach (var id in design.Decorations)
            if (id != 0 && sheet.GetRowOrDefault(id) is { } row && row.Component == (byte)kind)
                return Text(row.Name.ExtractText(), id);
        return "None";
    }

    public static string Race(uint id, byte sex) => Plugin.DataManager.GetExcelSheet<Race>().GetRowOrDefault(id) is { } row
        ? (sex == 1 ? row.Feminine : row.Masculine).ExtractText() : "";

    private static string Text(string name, uint id) => name.Length > 0 ? name : Unknown(id);
    private static string Unknown(uint id) => $"#{id}";
}
