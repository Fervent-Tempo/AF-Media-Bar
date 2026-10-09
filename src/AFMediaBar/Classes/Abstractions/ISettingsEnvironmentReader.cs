// Supplies immutable environment facts; the implementation owns platform queries, never a settings page or view model.
using AFMediaBar.Classes.Models.Settings;

namespace AFMediaBar.Classes.Abstractions;

/// <summary>Reads monitor and taskbar facts off the UI thread.</summary>
public interface ISettingsEnvironmentReader
{
    /// <summary>Returns enabled runtime targets without changing monitor preferences or the running host.</summary>
    Task<SettingsEnvironmentSnapshot> ReadAsync(CancellationToken cancellationToken);
}
