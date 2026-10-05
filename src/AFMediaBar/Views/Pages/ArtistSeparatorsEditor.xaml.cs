// 管理艺术家分隔符弹窗的呈现和临时输入；不读取或写入设置，也不负责歌词匹配。
using System.Collections.ObjectModel;
using System.Windows.Controls;
using AFMediaBar.Resources;
using Wpf.Ui;
using Wpf.Ui.Controls;
using TextBox = System.Windows.Controls.TextBox;

namespace AFMediaBar.Views.Pages;

/// <summary>可添加和删除条目的分隔符编辑弹窗；取消时丢弃草稿。</summary>
public partial class ArtistSeparatorsEditor : UserControl
{
    private static readonly ContentDialogService DialogService = new();
    private readonly ObservableCollection<SeparatorEntry> _entries = [];
    private readonly string _defaults;

    internal ArtistSeparatorsEditor(string current, string defaults)
    {
        InitializeComponent();
        _defaults = defaults;
        SetEntries(current);
        Rows.ItemsSource = _entries;
    }

    private void SetEntries(string current)
    {
        _entries.Clear();
        foreach (var text in current.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
            if (!string.IsNullOrWhiteSpace(text)) _entries.Add(new SeparatorEntry { Text = text });
    }

    internal static void SetHost(ContentDialogHost host) => DialogService.SetDialogHost(host);

    internal static async Task<string?> EditAsync(string current, string defaults)
    {
        var editor = new ArtistSeparatorsEditor(current, defaults);
        var dialog = new ContentDialog
        {
            Title = Translations.Get("Lyrics.Row.ArtistSeparators.Manage"),
            Content = editor,
            PrimaryButtonText = Translations.Get("Lyrics.ArtistSeparators.Dialog.Confirm"),
            CloseButtonText = Translations.Get("Common.Cancel"),
            DefaultButton = ContentDialogButton.Close
        };
        return await DialogService.ShowAsync(dialog, CancellationToken.None) == ContentDialogResult.Primary
            ? editor.GetSeparators() : null;
    }

    internal string GetSeparators() => string.Join("\n", _entries.Select(entry => entry.Text)
        .Where(text => !string.IsNullOrWhiteSpace(text)).Distinct(StringComparer.OrdinalIgnoreCase));

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var entry = new SeparatorEntry();
        _entries.Add(entry);
        // 容器生成后聚焦新条目，让“新增”后可以直接输入。
        Dispatcher.BeginInvoke(new Action(() =>
        {
            if (Rows.ItemContainerGenerator.ContainerFromItem(entry) is ContentPresenter presenter)
            {
                presenter.ApplyTemplate();
                FindTextBox(presenter)?.Focus();
            }
        }));
    }

    private void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SeparatorEntry entry }) _entries.Remove(entry);
    }

    private void RestoreDefaults_Click(object sender, RoutedEventArgs e) => SetEntries(_defaults);

    private static TextBox? FindTextBox(DependencyObject root)
    {
        for (var index = 0; index < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); index++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, index);
            if (child is TextBox textBox) return textBox;
            if (FindTextBox(child) is { } found) return found;
        }
        return null;
    }

    private sealed class SeparatorEntry
    {
        public string Text { get; set; } = string.Empty;
    }
}
