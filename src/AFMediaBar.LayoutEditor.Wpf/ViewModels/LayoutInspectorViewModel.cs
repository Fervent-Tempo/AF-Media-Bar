using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using AFMediaBar.Components.Abstractions;
using AFMediaBar.Layout.Models;

namespace AFMediaBar.LayoutEditor.Wpf.ViewModels;

/// <summary>
/// Selection-aware inspector projection. Property editors can bind to the
/// selected immutable model while mutations are committed through the host's
/// layout command boundary.
/// </summary>
public sealed partial class LayoutInspectorViewModel : ObservableObject
{
    private readonly Func<string, string> _localize;

    public LayoutInspectorViewModel(Func<string, string>? localize = null) =>
        _localize = localize ?? (key => key);

    [ObservableProperty]
    private object? selectedModel;

    [ObservableProperty]
    private string? selectedInstanceId;

    [ObservableProperty]
    private ComponentMeasureResult? measure;

    [ObservableProperty]
    private IReadOnlyList<ComponentValidationIssue> validationIssues = [];

    [ObservableProperty]
    private string? minimumSizeText;

    [ObservableProperty]
    private IReadOnlyList<string> validationMessages = [];

    public bool HasSelection => SelectedModel is not null;
    public bool HasWarning => Measure?.HasWarning == true || ValidationIssues.Any(x => x.IsWarning);

    public void SetSelection(
        string? instanceId,
        object? model,
        ComponentMeasureResult? measured = null,
        IReadOnlyList<ComponentValidationIssue>? issues = null)
    {
        SelectedInstanceId = instanceId;
        SelectedModel = model;
        Measure = measured;
        ValidationIssues = issues ?? [];
        MinimumSizeText = measured is null
            ? null
            : string.Format(
                CultureInfo.CurrentCulture,
                _localize("Settings.Layout.EditorMinimumSizeFormat"),
                measured.MinimumWidth,
                measured.MinimumHeight);
        ValidationMessages = ValidationIssues
            .Select(issue => _localize(issue.MessageResourceKey))
            .ToArray();
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(HasWarning));
    }
}
