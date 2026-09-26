using System;
using System.IO;
using System.Linq;
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

    private const string Command = "/replate";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly WindowSystem windows = new("RePlate");
    private readonly PlateToolbar plateToolbar;
    private readonly PlateImages images;
    private readonly PlateImages portraitImages;
    private readonly Thumbnails thumbnails;
    private readonly SettingsWindow settings;
    private readonly WelcomeWindow welcome;
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
        Restore = new PlateRestore(Editor, Designs, () => Configuration.PauseBeforeSave);
        GearsetRun = new GearsetRun(Editor);
        Guide = new Guide(this, Tour.Plates);
        PortraitGuide = new Guide(this, Tour.Portraits);
        Pictures = new ImageFiles(folder);
        images = new PlateImages(Pictures, () =>
        {
            var owner = CharacterId;
            return Framework.RunOnFrameworkThread(() => Reader.PlateWindow(owner));
        }, Guide);
        portraitImages = new PlateImages(Pictures, () => Framework.RunOnFrameworkThread(PortraitEditor.Window), PortraitGuide, "portrait");
        thumbnails = new Thumbnails(Pictures);

        MainWindow = new MainWindow(this, new PlatesTab(this, images), new PortraitsTab(this, portraitImages, thumbnails));
        settings = new SettingsWindow(this);
        welcome = new WelcomeWindow(this);
        windows.AddWindow(MainWindow);
        plateToolbar = new PlateToolbar(this);
        windows.AddWindow(settings);
        windows.AddWindow(welcome);
        if (!Configuration.WelcomeSeen) welcome.Open();

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand)
        {
            HelpMessage = "Open RePlate. /replate settings, /replate welcome, /replate stop (stops a restore).",
        });
        PluginInterface.UiBuilder.Draw += Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleSettings;
        Framework.Update += OnUpdate;
    }

    public Configuration Configuration { get; }
    public PresetStore Store { get; }
    public PlateReader Reader { get; }
    public PortraitEditor Editor { get; }
    public DesignEditor Designs { get; }
    public PlateRestore Restore { get; }
    public GearsetRun GearsetRun { get; }
    public Guide Guide { get; }
    public Guide PortraitGuide { get; }
    public ImageFiles Pictures { get; }

    /// <summary>
    /// Whether HaselTweaks is loaded. Anything that names it (its codes, bringing its portraits over) only shows
    /// while it is.
    /// </summary>
    public static bool HaselTweaksLoaded() => PluginInterface.InstalledPlugins.Any(p => p.InternalName == "HaselTweaks" && p.IsLoaded);
    private MainWindow MainWindow { get; }

    /// <summary>The logged-in character's content id, or 0.</summary>
    public ulong CharacterId => PlayerState.IsLoaded ? PlayerState.ContentId : 0;

    // Saved a second after the last change, not on every keystroke.
    public void MarkDirty() => dirtySince = DateTime.UtcNow;

    public void ToggleMainWindow() => MainWindow.Toggle();
    public void ShowMainWindow() => MainWindow.IsOpen = true;
    public void ShowPlates() => MainWindow.ShowPlates();
    public void ToggleSettings() => settings.Toggle();
    public void OpenWelcome() => welcome.Open();

    /// <summary>Starts the guided tour on the Adventure Plates tab.</summary>
    public void StartTour()
    {
        Guide.Start();
        MainWindow.ShowPlates();
    }

    /// <summary>Starts the Portraits tab's tour.</summary>
    public void StartPortraitTour()
    {
        Configuration.PortraitTourOffered = true;
        PortraitGuide.Start();
        MainWindow.ShowPortraits();
    }

    /// <summary>Stops whatever RePlate is doing in the game's windows.</summary>
    public void StopAll(string message)
    {
        Designs.Stop(message);
        Restore.Stop(message);
        GearsetRun.Stop(message);
    }

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
        plateToolbar.Draw();
        images.DrawDialog();
        portraitImages.DrawDialog();
    }

    private void OnUpdate(IFramework framework)
    {
        images.Update();
        portraitImages.Update();
        Designs.Update();
        Restore.Update();
        GearsetRun.Update();
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
            case "settings" or "config":
                ToggleSettings();
                break;
            case "welcome":
                OpenWelcome();
                break;
            case "stop":
                StopAll("Stopped. Nothing more was changed.");
                break;
            default:
                ToggleMainWindow();
                break;
        }
    }

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleSettings;
        CommandManager.RemoveHandler(Command);
        windows.RemoveAllWindows();
        images.Dispose();
        portraitImages.Dispose();
        thumbnails.Dispose();
        if (dirtySince != null) Configuration.Save();
    }
}
