using System;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using RePlate.Windows;

namespace RePlate;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IChatGui ChatGui { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;
    [PluginService] internal static IFramework Framework { get; private set; } = null!;
    [PluginService] internal static ITextureProvider TextureProvider { get; private set; } = null!;

    private const string Command = "/replate";
    private static readonly TimeSpan SaveDelay = TimeSpan.FromSeconds(1);

    private readonly WindowSystem windows = new("RePlate");
    private DateTime? dirtySince;

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Theme.Use(Configuration);

        MainWindow = new MainWindow(this);
        windows.AddWindow(MainWindow);

        CommandManager.AddHandler(Command, new CommandInfo(OnCommand) { HelpMessage = "Open RePlate." });
        PluginInterface.UiBuilder.Draw += windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainWindow;
        Framework.Update += OnUpdate;
    }

    public Configuration Configuration { get; }
    private MainWindow MainWindow { get; }

    // Saved a second after the last change, not on every keystroke.
    public void MarkDirty() => dirtySince = DateTime.UtcNow;

    public void ToggleMainWindow() => MainWindow.Toggle();

    private void OnUpdate(IFramework framework)
    {
        if (dirtySince is { } since && DateTime.UtcNow - since >= SaveDelay)
        {
            dirtySince = null;
            Configuration.Save();
        }
    }

    private void OnCommand(string command, string args) => ToggleMainWindow();

    public void Dispose()
    {
        Framework.Update -= OnUpdate;
        PluginInterface.UiBuilder.Draw -= windows.Draw;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainWindow;
        CommandManager.RemoveHandler(Command);
        windows.RemoveAllWindows();
        if (dirtySince != null) Configuration.Save();
    }
}
