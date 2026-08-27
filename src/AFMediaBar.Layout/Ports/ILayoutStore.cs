using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Ports;

public interface ILayoutStore
{
    Task<LayoutDocument> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(LayoutDocument document, CancellationToken cancellationToken = default);
}
