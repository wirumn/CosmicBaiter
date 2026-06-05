using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

namespace CosmicBaiter;

/// <summary>
/// Dalamud-injected services via the service locator pattern.
/// </summary>
public class Services
{
    [PluginService] public static IDalamudPluginInterface PluginInterface { get; set; } = null!;
    [PluginService] public static IPluginLog             Log             { get; set; } = null!;
    [PluginService] public static IFramework             Framework       { get; set; } = null!;
    [PluginService] public static IObjectTable           ObjectTable     { get; set; } = null!;
    [PluginService] public static ICondition             Condition       { get; set; } = null!;
}
