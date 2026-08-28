using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn.Containers;
using AFMediaBar.Layout.Models;
using AFMediaBar.Layout.Widgets;

namespace AFMediaBar.Components.Wpf.Composition;

public sealed class LayoutCompositionService(ComponentInteractionCallbacks callbacks)
{
    public LayoutCompositionViewModel Compose(LayoutProfile profile)
    {
        var components = new Dictionary<string, ComponentViewModelBase>(StringComparer.Ordinal);
        var containers = profile.Containers
            .Where(x => x.Enabled)
            .Select(x => ComposeContainer(x, components))
            .ToArray();
        var collapse = profile.CollapseContainers
            .Where(x => x.Enabled)
            .Select(x => ComposeCollapse(x, components))
            .ToArray();
        return new LayoutCompositionViewModel(profile, containers, collapse, components);
    }

    private ContainerHostViewModel ComposeContainer(
        LayoutContainerElement model,
        IDictionary<string, ComponentViewModelBase> components)
    {
        IComponentSettings settings = model.ContainerKind == LayoutContainerKind.HoverSwitch
            ? new HoverSwitchContainerSettings(
                (ComponentFlowOrientation)model.Orientation,
                (ComponentContentAlignment)model.ContentAlignment,
                (ComponentContentAlignment)model.SecondaryContentAlignment,
                model.ProximityDip,
                ToAnimation(model.Animation))
            : new StaticContainerSettings(
                (ComponentFlowOrientation)model.Orientation,
                (ComponentContentAlignment)model.ContentAlignment);
        return new ContainerHostViewModel(
            model.InstanceId,
            settings,
            model,
            ComposeSlot(model.PrimarySlot, components),
            ComposeSlot(model.SecondarySlot, components));
    }

    private ContainerHostViewModel ComposeCollapse(
        LayoutCollapseContainer model,
        IDictionary<string, ComponentViewModelBase> components) =>
        new(
            model.InstanceId,
            new CollapseContainerSettings(model.TriggerThicknessDip, model.ProximityDip, ToAnimation(model.Animation)),
            model,
            ComposeSlot(model.ExpandedSlot, components),
            []);

    private IReadOnlyList<ComponentViewModelBase> ComposeSlot(
        LayoutSlot slot,
        IDictionary<string, ComponentViewModelBase> components)
    {
        var result = new List<ComponentViewModelBase>();
        foreach (var widget in slot.Children.OfType<LayoutWidgetElement>().Where(x => x.Enabled))
        {
            if (!ComponentDefinitionAdapter.TryMapSettings(widget, out var settings)) continue;
            var viewModel = ComponentViewFactory.Create(
                widget.InstanceId,
                settings,
                callbacks.SourceRequested,
                callbacks.CommandRequested,
                callbacks.OutputDeviceRequested,
                callbacks.VolumeRequested,
                callbacks.OutputDeviceWheelRequested,
                callbacks.VolumeWheelRequested,
                callbacks.MetricsRequested);
            if (viewModel is null) continue;
            components[widget.InstanceId] = viewModel;
            result.Add(viewModel);
        }
        return result;
    }

    private static ComponentAnimationSettings ToAnimation(LayoutAnimationSettings animation) =>
        new(animation.Enabled, animation.DurationMilliseconds, animation.DelayMilliseconds, (ComponentEasingKind)animation.Easing);
}
