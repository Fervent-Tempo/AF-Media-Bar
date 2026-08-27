using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using AFMediaBar.Layout.Model;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.Layout.Defaults;

/// <summary>
/// Loads the repository-owned default profiles. Defaults are data, not a
/// runtime migration side effect, so the editor and Sandbox can share them.
/// </summary>
public static class LayoutDefaultTemplates
{
    private static readonly JsonSerializerOptions Options = CreateOptions();

    public static LayoutDocument LoadDocument()
    {
        return new LayoutDocument(
            LayoutSchemaContract.Version,
            Load(LayoutSchemaContract.HorizontalDefaultTemplate),
            Load(LayoutSchemaContract.VerticalDefaultTemplate));
    }

    public static LayoutProfile LoadHorizontal() => Load(LayoutSchemaContract.HorizontalDefaultTemplate);

    public static LayoutProfile LoadVertical() => Load(LayoutSchemaContract.VerticalDefaultTemplate);

    private static LayoutProfile Load(string fileName)
    {
        var resourceName = $"AFMediaBar.Layout.Defaults.{fileName}";
        using var stream = typeof(LayoutDefaultTemplates).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException($"Missing embedded layout template: {resourceName}");
        return JsonSerializer.Deserialize<LayoutProfile>(stream, Options)
            ?? throw new InvalidDataException($"Empty layout template: {resourceName}");
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }
}
