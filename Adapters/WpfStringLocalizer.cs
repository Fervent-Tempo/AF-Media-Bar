using AFMediaBar.Abstractions;
using AFMediaBar.Services;

namespace AFMediaBar.Adapters;

internal sealed class WpfStringLocalizer : IStringLocalizer
{
    internal static WpfStringLocalizer Instance { get; } = new();

    private WpfStringLocalizer()
    {
    }

    public string Get(string key, params object[] args) =>
        Localization.Get(key, args);
}
