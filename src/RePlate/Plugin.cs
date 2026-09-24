using System;
using System.IO;
using System.Text.Json;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using RePlate.Core.Images;
using RePlate.Core.Plates;
using RePlate.Game;
using RePlate.Windows;

namespace RePlate;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static ICondition Condition { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;
    [PluginService] internal static ITextureReadbackProvider TextureReadback { get; private set; } = null!;
    [PluginService] internal static IAddonLifecycle AddonLifecycle { get; private set; } = null!;

    private const string Command = "/replate";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly WindowSystem windows = new("RePlate");
    private readonly PlateImages images;
    private readonly UiProbe probe = new();
    private DateTime? dirtySince;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Theme.Use(Configuration);

        var folder = PluginInterface.GetPluginConfigDirectory();
        Store = new PresetStore(Path.Combine(folder, "plates.json"));
        ImportOldLibrary(Path.Combine(folder, "library.json"));
        Reader = new PlateReader();
        Editor = new PortraitEditor();
        Designs = new DesignEditor();
        Restore = new PlateRestore(Editor, Designs);
        images = new PlateImages(new ImageFiles(folder), () =>
        {
            var owner = CharacterId;
            return Framework.RunOnFrameworkThread(() => Reader.PlateWindow(owner));
        });

        MainWindow = new MainWindow(new PlatesTab(this, images));
        windows.AddWindow(MainWindow);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open RePlate. /replate stop stops a restore." });
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        Framework.Update += OnUpdate;
    }

    public Configuration Configuration { get; }
    public PresetStore Store { get; }
    public PlateReader Reader { get; }
    public PortraitEditor Editor { get; }
    public DesignEditor Designs { get; }
    public PlateRestore Restore { get; }
    private MainWindow MainWindow { get; }

    /// <summary>The logged-in character's content id, or 0.</summary>
    public ulong CharacterId => PlayerState.IsLoaded ? PlayerState.ContentId : 0;

    // Saved a second after the last change, not on every keystroke.
    public void MarkDirty() => dirtySince = DateTime.UtcNow;

    public void ToggleMainWindow() => MainWindow.Toggle();

    // Earlier builds kept portraits in library.json. Bring them over once; the old file is left as it was.
    private void ImportOldLibrary(string path)
    {
        if (Store.Exists || !File.Exists(path)) return;
        try
        {
            var presets = LegacyLibrary.Read(File.ReadAllText(path), out var skipped);
            foreach (var preset in presets) Store.Add(preset);
            Store.Save();
            Log.Information($"Imported {presets.Count} plates from the old library" + (skipped > 0 ? $"; {skipped} couldn't be read." : "."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Log.Warning(ex, "Couldn't import the old RePlate library");
        }
    }

    private void Draw()
    {
        windows.Draw();
        images.DrawDialog();
    }

    private void OnUpdate(IFramework framework)
    {
        images.Update();
        probe.Update();
        Designs.Update();
        Restore.Update();
        if (dirtySince is { } since && DateTime.UtcNow - since >= SaveDelay)
        {
            dirtySince = null;
            Configuration.Save();
        }
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "probe":
                probe.Toggle();
                break;
            case "stop":
                StopAll("Stopped. Nothing more was changed.");
                break;
            default:
                ToggleMainWindow();
                break;
        }
    }

    /// <summary>Stops whatever RePlate is doing in the game's windows.</summary>
    public void StopAll(string message)
    {
        Designs.Stop(message);
        Restore.Stop(message);
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        CommandManager.RemoveHandler(Command);
        windows.RemoveAllWindows();
        images.Dispose();
        probe.Dispose();
        if (dirtySince != null) Configuration.Save();
    }
}
