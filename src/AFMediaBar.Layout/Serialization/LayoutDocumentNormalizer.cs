using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Model;

namespace AFMediaBar.Layout.Serialization;

public static class LayoutDocumentNormalizer
{
    public static LayoutDocument Normalize(LayoutDocument document)
    {
        if (document.SchemaVersion > LayoutSchemaVersion())
        {
            throw new InvalidDataException($"Unsupported layout schema version: {document.SchemaVersion}.");
        }

        return document with
        {
            SchemaVersion = LayoutSchemaVersion(),
            Horizontal = NormalizeProfile(document.Horizontal with
            {
                Key = LayoutProfileKey.Horizontal,
                LayoutMode = PlayerLayoutMode.Horizontal
            }),
            Vertical = NormalizeProfile(document.Vertical with
            {
                Key = LayoutProfileKey.Vertical,
                LayoutMode = PlayerLayoutMode.Vertical
            })
        };
    }

    public static void ValidateOrThrow(LayoutDocument document)
    {
        var failures = document.Horizontal
            .GetValidationErrors("horizontal")
            .Concat(document.Vertical.GetValidationErrors("vertical"))
            .ToArray();
        if (failures.Length > 0)
        {
            throw new InvalidDataException($"Layout validation failed: {string.Join("; ", failures)}");
        }
    }

    private static LayoutProfile NormalizeProfile(LayoutProfile profile)
    {
        var grid = LayoutGridSettings.Normalize(profile.Grid);
        return profile with
        {
            Grid = grid,
            Containers = profile.Containers
                .Select(container => container with
                {
                    GridBounds = NormalizeBounds(container.GridBounds, grid),
                    PrimarySlot = NormalizeSlot(container.PrimarySlot),
                    SecondarySlot = NormalizeSlot(container.SecondarySlot)
                })
                .ToArray(),
            CollapseContainers = profile.CollapseContainers
                .Select(collapse => collapse with
                {
                    GridBounds = NormalizeBounds(collapse.GridBounds, grid)!,
                    ExpandedSlot = NormalizeSlot(collapse.ExpandedSlot)
                })
                .ToArray()
        };
    }

    private static LayoutSlot NormalizeSlot(LayoutSlot slot) =>
        slot with { Children = slot.Children.Select(NormalizeElement).ToArray() };

    private static LayoutElement NormalizeElement(LayoutElement element) => element switch
    {
        LayoutWidgetElement widget => widget with { GridBounds = widget.GridBounds?.Normalized },
        LayoutContainerElement container => container with
        {
            GridBounds = container.GridBounds?.Normalized,
            PrimarySlot = NormalizeSlot(container.PrimarySlot),
            SecondarySlot = NormalizeSlot(container.SecondarySlot)
        },
        _ => element
    };

    private static LayoutGridRect? NormalizeBounds(LayoutGridRect? bounds, LayoutGridSettings grid)
    {
        if (bounds is null) return null;
        var normalized = bounds.Normalized;
        var x = Math.Clamp(normalized.X, 0, Math.Max(0, grid.Columns - 1));
        var y = Math.Clamp(normalized.Y, 0, Math.Max(0, grid.Rows - 1));
        var width = Math.Clamp(normalized.Width, 1, grid.Columns - x);
        var height = Math.Clamp(normalized.Height, 1, grid.Rows - y);
        return new LayoutGridRect(x, y, width, height);
    }

    private static int LayoutSchemaVersion() => LayoutSchemaContract.Version;

    private static IEnumerable<string> GetValidationErrors(this LayoutProfile profile, string label)
    {
        foreach (var error in Services.LayoutGridConstraintService.ValidateProfile(profile))
        {
            yield return $"{label}:{error.InstanceId}:{error.Failure}";
        }
    }
}
