namespace AFMediaBar.Layout.Model;

/// <summary>
/// Central contract for the next breaking layout format. The runtime remains on
/// schema 5 is the only persisted runtime format.
/// </summary>
public static class LayoutSchemaContract
{
    public const int Version = 5;

    public const string HorizontalDefaultTemplate = "default-horizontal.json";
    public const string VerticalDefaultTemplate = "default-vertical.json";
}
