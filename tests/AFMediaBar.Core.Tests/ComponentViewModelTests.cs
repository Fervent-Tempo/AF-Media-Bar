using AFMediaBar.Components.BuiltIn.Audio;
using AFMediaBar.Components.Abstractions;
using AFMediaBar.Components.BuiltIn.Media;
using AFMediaBar.Components.BuiltIn.Playback;
using AFMediaBar.Components.BuiltIn.System;
using AFMediaBar.Components.Wpf.BuiltIn.Artwork;
using AFMediaBar.Components.Wpf.BuiltIn.Metrics;
using AFMediaBar.Components.Wpf.BuiltIn.OutputDevice;
using AFMediaBar.Components.Wpf.BuiltIn.PlaybackCommand;
using AFMediaBar.Components.Wpf.BuiltIn.Spectrum;
using AFMediaBar.Components.Wpf.BuiltIn.Volume;
using AFMediaBar.Components.Wpf.Controls;
using AFMediaBar.Components.Wpf;
using AFMediaBar.Components.Wpf.Composition;
using AFMediaBar.Layout.Defaults;
using AFMediaBar.LayoutEditor.Wpf.ViewModels;
using AFMediaBar.LayoutEditor.Wpf.Views;
using AFMediaBar.Models;
using AFMediaBar.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace AFMediaBar.Core.Tests;

[TestClass]
public sealed class ComponentViewModelTests
{
    [TestMethod]
    public void ComponentCommandsReturnTheActualAnchorToTheCoordinatorBoundary()
    {
        var anchor = new object();
        object? artworkAnchor = null;
        object? commandAnchor = null;
        object? deviceAnchor = null;
        object? volumeAnchor = null;

        var artwork = new ArtworkViewModel("art", new ArtworkSettings(), value => artworkAnchor = value);
        var command = new PlaybackCommandViewModel("cmd", new PlaybackCommandSettings(), (_, value) => commandAnchor = value);
        var output = new OutputDeviceViewModel("out", new OutputDeviceSettings(), value => deviceAnchor = value);
        var volume = new VolumeViewModel("vol", new VolumeSettings(), value => volumeAnchor = value);

        artwork.OpenSourceCommand.Execute(anchor);
        command.InvokeCommand.Execute(anchor);
        output.SelectCommand.Execute(anchor);
        volume.OpenPopupCommand.Execute(anchor);

        Assert.AreSame(anchor, artworkAnchor);
        Assert.AreSame(anchor, commandAnchor);
        Assert.AreSame(anchor, deviceAnchor);
        Assert.AreSame(anchor, volumeAnchor);
    }

    [TestMethod]
    public void ConditionalActionsAndSpectrumWarningsFollowSettingsAndAvailability()
    {
        var artworkRequests = 0;
        var metricRequests = 0;
        var artwork = new ArtworkViewModel("art", new ArtworkSettings(OpenSourceOnClick: false), _ => artworkRequests++);
        var metrics = new MetricsViewModel("metrics", new MetricsSettings(OpenTaskManagerOnClick: false), () => metricRequests++);
        var spectrum = new SpectrumViewModel("spectrum", new SpectrumSettings());

        artwork.OpenSourceCommand.Execute(null);
        metrics.OpenTaskManagerCommand.Execute(null);
        spectrum.IsAudioAvailable = false;

        Assert.AreEqual(0, artworkRequests);
        Assert.AreEqual(0, metricRequests);
        Assert.AreEqual("Spectrum.AudioUnavailable", spectrum.WarningCode);
    }

    [TestMethod]
    public void ViewFactoryCreatesAViewModelForEveryMigratedFunctionalSetting()
    {
        var settings = new AFMediaBar.Components.BuiltIn.BuiltInComponentRegistry().Items
            .Where(x => x.Kind == AFMediaBar.Components.Abstractions.ComponentKind.Functional)
            .Select(x => x.CreateDefaultSettings());

        foreach (var item in settings)
        {
            Assert.IsNotNull(ComponentViewFactory.Create("factory-test", item), item.TypeId);
        }
    }

    [TestMethod]
    public void LayoutCompositionBuildsContainerTreeAndSpecializedAudioViewModels()
    {
        var profile = LayoutDefaultTemplates.LoadDocument().Horizontal;
        var composition = new LayoutCompositionService(new ComponentInteractionCallbacks()).Compose(profile);

        Assert.HasCount(profile.Containers.Count, composition.Containers);
        Assert.HasCount(profile.CollapseContainers.Count, composition.CollapseContainers);
        Assert.IsTrue(composition.Components.Values.Any(x => x is OutputDeviceViewModel));
        Assert.IsTrue(composition.Components.Values.Any(x => x is VolumeViewModel));
        Assert.IsTrue(composition.Containers.All(x => x.ActiveSlotIndex == -1));
    }

    [STATestMethod]
    public void ComponentTemplateDictionaryLoadsAllFunctionalTemplates()
    {
        _ = Application.Current ?? new Application();
        var dictionary = new ResourceDictionary
        {
            Source = new Uri("/AFMediaBar.Components.Wpf;component/ComponentTemplates.xaml", UriKind.RelativeOrAbsolute)
        };
        var viewModelTypes = new[]
        {
            typeof(ArtworkViewModel),
            typeof(AFMediaBar.Components.Wpf.BuiltIn.MediaText.MediaTextViewModel),
            typeof(AFMediaBar.Components.Wpf.BuiltIn.MediaSource.MediaSourceViewModel),
            typeof(PlaybackCommandViewModel),
            typeof(OutputDeviceViewModel),
            typeof(VolumeViewModel),
            typeof(SpectrumViewModel),
            typeof(MetricsViewModel),
            typeof(AFMediaBar.Components.Wpf.BuiltIn.Separator.SeparatorViewModel)
        };

        foreach (var type in viewModelTypes)
        {
            Assert.IsInstanceOfType<DataTemplate>(dictionary[new DataTemplateKey(type)], type.Name);
        }
    }

    [STATestMethod]
    public void InteractiveComponentGlyphsUseTheSameCenteredViewport()
    {
        var views = new UserControl[]
        {
            new PlaybackCommandView(),
            new OutputDeviceView(),
            new VolumeView()
        };

        foreach (var view in views)
        {
            var button = Assert.IsInstanceOfType<Button>(view.Content, view.GetType().Name);
            var viewport = Assert.IsInstanceOfType<CenteredIconGlyph>(button.Content, view.GetType().Name);

            Assert.AreEqual(16d, viewport.Width, view.GetType().Name);
            Assert.AreEqual(16d, viewport.Height, view.GetType().Name);
            Assert.AreEqual(HorizontalAlignment.Center, viewport.HorizontalAlignment, view.GetType().Name);
            Assert.AreEqual(VerticalAlignment.Center, viewport.VerticalAlignment, view.GetType().Name);
        }
    }

    [TestMethod]
    public void LayoutEditorViewModelProjectsContainersSlotsAndFunctionalComponents()
    {
        var document = LayoutDefaultTemplates.LoadDocument();
        using var viewModel = new LayoutEditorViewModel(document, profileKey: LayoutProfileKey.Vertical);

        Assert.AreEqual(LayoutProfileKey.Vertical, viewModel.ProfileKey);
        Assert.HasCount(document.Vertical.Containers.Count + document.Vertical.CollapseContainers.Count, viewModel.Roots);
        Assert.IsTrue(viewModel.Roots.Any(x => x.Kind == LayoutEditorNodeKind.Container));
        Assert.IsTrue(viewModel.Roots.SelectMany(Descendants).Any(x => x.Kind == LayoutEditorNodeKind.Widget));
        Assert.IsNotEmpty(viewModel.Palette);

        var selected = viewModel.Roots
            .SelectMany(Descendants)
            .First(x => x.Kind == LayoutEditorNodeKind.Widget);
        viewModel.SelectNode(selected.InstanceId);

        Assert.AreEqual(selected.InstanceId, viewModel.Inspector.SelectedInstanceId);
        Assert.IsTrue(viewModel.Inspector.HasSelection);
    }

    [TestMethod]
    public void LayoutEditorPaletteContainsEveryBuiltInVariantAndLocalizedCategory()
    {
        var document = LayoutDefaultTemplates.LoadDocument();
        using var viewModel = new LayoutEditorViewModel(document, localize: key => key);

        Assert.HasCount(17, viewModel.Palette);
        Assert.HasCount(6, viewModel.PaletteGroups);
        Assert.IsTrue(viewModel.Palette.Select(x => x.Token).ToHashSet(StringComparer.Ordinal).IsSupersetOf(
        [
            "container:static",
            "container:hover",
            "container:edge",
            $"{AFMediaBar.Layout.Models.BuiltInWidgetTypeIds.Command}|{(int)AFMediaBar.Layout.Models.MediaCommandKind.Previous}",
            $"{AFMediaBar.Layout.Models.BuiltInWidgetTypeIds.Command}|{(int)AFMediaBar.Layout.Models.MediaCommandKind.PlayPause}",
            $"{AFMediaBar.Layout.Models.BuiltInWidgetTypeIds.Command}|{(int)AFMediaBar.Layout.Models.MediaCommandKind.Next}",
            $"{AFMediaBar.Layout.Models.BuiltInWidgetTypeIds.Command}|{(int)AFMediaBar.Layout.Models.MediaCommandKind.SelectSource}",
            $"{AFMediaBar.Layout.Models.BuiltInWidgetTypeIds.Command}|{(int)AFMediaBar.Layout.Models.MediaCommandKind.SelectOutputDevice}",
            $"{AFMediaBar.Layout.Models.BuiltInWidgetTypeIds.Command}|{(int)AFMediaBar.Layout.Models.MediaCommandKind.AdjustVolume}",
            $"{AFMediaBar.Layout.Models.BuiltInWidgetTypeIds.MediaText}|{(int)AFMediaBar.Layout.Models.MediaTextKind.Title}",
            $"{AFMediaBar.Layout.Models.BuiltInWidgetTypeIds.MediaText}|{(int)AFMediaBar.Layout.Models.MediaTextKind.Artist}",
            $"{AFMediaBar.Layout.Models.BuiltInWidgetTypeIds.MediaText}|{(int)AFMediaBar.Layout.Models.MediaTextKind.TitleAndArtist}"
        ]));

        var groupNames = viewModel.PaletteGroups.ToDictionary(x => x.Category, x => x.DisplayName);
        Assert.AreEqual("Settings.Layout.CategoryLayout", groupNames[ComponentCategory.Container]);
        Assert.AreEqual("Settings.Layout.CategoryMedia", groupNames[ComponentCategory.Media]);
        Assert.AreEqual("Settings.Layout.CategoryControls", groupNames[ComponentCategory.Playback]);
        Assert.AreEqual("Settings.Layout.CategoryAudio", groupNames[ComponentCategory.Audio]);
        Assert.AreEqual("Settings.Layout.CategorySystem", groupNames[ComponentCategory.System]);
        Assert.AreEqual("Settings.Layout.CategoryLayout", groupNames[ComponentCategory.Layout]);
    }

    [TestMethod]
    public void LayoutEditorDocumentChangedFiresOncePerApplyUndoAndRedo()
    {
        var document = LayoutDefaultTemplates.LoadDocument();
        using var viewModel = new LayoutEditorViewModel(document);
        var changes = 0;
        viewModel.DocumentChanged += (_, _) => changes++;
        var changed = document with
        {
            Horizontal = document.Horizontal with
            {
                Surface = document.Horizontal.Surface with { GapDip = document.Horizontal.Surface.GapDip + 1 }
            }
        };

        Assert.IsTrue(viewModel.TryApply(_ => changed));
        viewModel.UndoCommand.Execute(null);
        viewModel.RedoCommand.Execute(null);

        Assert.AreEqual(3, changes);
    }

    [TestMethod]
    public void SelectingATreeNodeDoesNotRebuildTheBoundTree()
    {
        var document = LayoutDefaultTemplates.LoadDocument();
        using var viewModel = new LayoutEditorViewModel(document);
        var root = viewModel.Roots[0];
        var selected = root.Children
            .SelectMany(Descendants)
            .First(x => x.Kind == LayoutEditorNodeKind.Widget);

        viewModel.SelectNode(selected.InstanceId);

        Assert.AreSame(root, viewModel.Roots[0]);
        Assert.AreEqual(selected.InstanceId, viewModel.SelectedInstanceId);
        Assert.AreEqual(selected.InstanceId, viewModel.Inspector.SelectedInstanceId);
    }

    [TestMethod]
    public void LayoutInspectorLocalizesMinimumSizeAndValidationMessages()
    {
        var inspector = new LayoutInspectorViewModel(key => $"localized:{key}");
        inspector.SetSelection(
            "component",
            new object(),
            new ComponentMeasureResult(4, 3, 2, 1, true),
            [new ComponentValidationIssue("warning", "Component.Validation.Warning", true)]);

        Assert.AreEqual("localized:Settings.Layout.EditorMinimumSizeFormat", inspector.MinimumSizeText);
        Assert.HasCount(1, inspector.ValidationMessages);
        Assert.AreEqual("localized:Component.Validation.Warning", inspector.ValidationMessages[0]);
        Assert.IsTrue(inspector.HasWarning);
    }

    [TestMethod]
    public void WindowStateProjectionsRemainNativeHandleFreeAndTrackRecovery()
    {
        var viewModel = new MainWindowViewModel(WindowSettings.Default);

        viewModel.ApplyWindowSettings(WindowSettings.Default with
        {
            HostMode = WindowHostMode.Floating,
            LayoutMode = AFMediaBar.Layout.Models.PlayerLayoutMode.Vertical
        });
        viewModel.Placement.ApplyBounds(20, 30, 640, 180, 144);
        viewModel.ApplyPresentation(visible: true, expanded: false);
        viewModel.TaskbarHost.ApplySnapshot(nint.Zero, embedded: false, floating: true);
        viewModel.ApplyRecovery("dpi-changed");

        Assert.AreEqual(WindowHostMode.Floating, viewModel.Placement.Settings.HostMode);
        Assert.AreEqual(AFMediaBar.Layout.Models.PlayerLayoutMode.Vertical, viewModel.Placement.LayoutMode);
        Assert.AreEqual(1.5, viewModel.Placement.DpiScale, 0.001);
        Assert.IsTrue(viewModel.Placement.IsVisible);
        Assert.IsFalse(viewModel.Placement.IsExpanded);
        Assert.IsTrue(viewModel.TaskbarHost.IsFloating);
        Assert.IsTrue(viewModel.Placement.IsRecoveryPending);
        Assert.IsTrue(viewModel.TaskbarHost.IsRecoveryPending);

        viewModel.ApplyRecovery(null);
        Assert.IsFalse(viewModel.Placement.IsRecoveryPending);
        Assert.IsFalse(viewModel.TaskbarHost.IsRecoveryPending);
    }

    [STATestMethod]
    public void LayoutEditorViewsLoadWithTheirBindingContracts()
    {
        var editor = new LayoutEditorView();
        var tree = new LayoutTreeView();
        var palette = new ComponentPaletteView();
        var inspector = new LayoutInspectorView();

        Assert.IsNotNull(editor);
        Assert.IsNotNull(tree);
        Assert.IsNotNull(palette);
        Assert.IsNotNull(inspector);
    }

    [TestMethod]
    public void LayoutEditorViewModelUndoCommandRestoresThePreviousDocument()
    {
        var document = LayoutDefaultTemplates.LoadDocument();
        using var viewModel = new LayoutEditorViewModel(document);
        var changed = document with
        {
            Horizontal = document.Horizontal with
            {
                Surface = document.Horizontal.Surface with { GapDip = document.Horizontal.Surface.GapDip + 1 }
            }
        };

        Assert.IsTrue(viewModel.TryApply(_ => changed));
        Assert.IsTrue(viewModel.CanUndo);
        viewModel.UndoCommand.Execute(null);

        Assert.AreEqual(document, viewModel.Session.Document);
        Assert.IsTrue(viewModel.CanRedo);
    }

    private static IEnumerable<LayoutTreeItemViewModel> Descendants(LayoutTreeItemViewModel item) =>
        new[] { item }.Concat(item.Children.SelectMany(Descendants));
}
