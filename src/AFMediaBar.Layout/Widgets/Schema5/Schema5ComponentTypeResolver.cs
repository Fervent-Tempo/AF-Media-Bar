using AFMediaBar.Components.Abstractions;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Widgets.Schema5;

/// <summary>
/// Resolves schema-5's legacy command discriminator to the dedicated runtime
/// component TypeId while leaving the persisted discriminator unchanged.
/// </summary>
internal static class Schema5ComponentTypeResolver
{
    public static string Resolve(string typeId, WidgetSettings settings) =>
        typeId == ComponentTypeIds.PlaybackCommand && settings is CommandWidgetSettings command
            ? command.Command switch
            {
                MediaCommandKind.SelectOutputDevice => ComponentTypeIds.OutputDevice,
                MediaCommandKind.AdjustVolume => ComponentTypeIds.Volume,
                _ => ComponentTypeIds.PlaybackCommand
            }
            : typeId;
}
