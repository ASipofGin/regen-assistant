using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using RegenAssistant.Windows;

namespace RegenAssistant;

public sealed class Plugin : IDalamudPlugin
{
    [PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] internal static ICommandManager CommandManager { get; private set; } = null!;
    [PluginService] internal static IClientState ClientState { get; private set; } = null!;
    [PluginService] internal static IPlayerState PlayerState { get; private set; } = null!;
    [PluginService] internal static IObjectTable ObjectTable { get; private set; } = null!;
    [PluginService] internal static IGameGui GameGui { get; private set; } = null!;
    [PluginService] internal static IDataManager DataManager { get; private set; } = null!;
    [PluginService] internal static IPluginLog Log { get; private set; } = null!;

    private const string CommandName = "/regenassist";

    public Configuration Configuration { get; init; }
    public RegenStatuses Statuses { get; init; }
    public RegenTracker Tracker { get; init; }
    public PartyMembers PartyMembers { get; init; }

    public readonly WindowSystem WindowSystem = new("RegenAssistant");
    private readonly PartyListOverlay overlay;
    private ConfigWindow ConfigWindow { get; init; }
    private MonitorWindow MonitorWindow { get; init; }

    public Plugin()
    {
        Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
        Statuses = new RegenStatuses(DataManager, Log);
        Tracker = new RegenTracker(Statuses, Configuration);
        PartyMembers = new PartyMembers(ObjectTable, PlayerState);
        overlay = new PartyListOverlay(GameGui, ClientState, PartyMembers, Tracker, Configuration);

        ConfigWindow = new ConfigWindow(this);
        MonitorWindow = new MonitorWindow(this);

        WindowSystem.AddWindow(ConfigWindow);
        WindowSystem.AddWindow(MonitorWindow);

        CommandManager.AddHandler(CommandName, new CommandInfo(OnCommand)
        {
            HelpMessage = "Toggle the party regen monitor. \"/regenassist config\" opens settings, \"/regenassist overlay\" toggles the party list overlay.",
        });

        PluginInterface.UiBuilder.Draw += WindowSystem.Draw;
        PluginInterface.UiBuilder.Draw += overlay.Draw;
        PluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    }

    public void Dispose()
    {
        PluginInterface.UiBuilder.Draw -= WindowSystem.Draw;
        PluginInterface.UiBuilder.Draw -= overlay.Draw;
        PluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
        PluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;

        WindowSystem.RemoveAllWindows();

        ConfigWindow.Dispose();
        MonitorWindow.Dispose();

        CommandManager.RemoveHandler(CommandName);
    }

    private void OnCommand(string command, string args)
    {
        switch (args.Trim().ToLowerInvariant())
        {
            case "config":
            case "settings":
                ToggleConfigUi();
                break;
            case "overlay":
                Configuration.OverlayEnabled = !Configuration.OverlayEnabled;
                Configuration.Save();
                break;
            default:
                ToggleMainUi();
                break;
        }
    }

    public void ToggleConfigUi() => ConfigWindow.Toggle();
    public void ToggleMainUi() => MonitorWindow.Toggle();
}
