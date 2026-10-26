using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using DS4Windows;

namespace DS4WinWPF.DS4Forms;

internal enum ProfileViewChoice { Tiles, List, Details }

// Keep one list, selection and collection view for every presentation.
internal sealed class ProfileBrowserViews
{
    private readonly ListBox list;
    private readonly FrameworkElement header;
    private readonly ItemsPanelTemplate tilesPanel;
    private readonly DataTemplate tilesTemplate;
    private readonly Style tilesStyle, rowsStyle;
    private readonly ItemsPanelTemplate rowsPanel;
    private readonly DataTemplate listTemplate, detailsTemplate;
    internal ProfileViewChoice Choice { get; private set; }

    internal ProfileBrowserViews(ListBox list, FrameworkElement header)
    {
        this.list = list; this.header = header;
        tilesPanel = list.ItemsPanel; tilesTemplate = list.ItemTemplate; tilesStyle = list.ItemContainerStyle;
        rowsStyle = new Style(typeof(ListBoxItem), tilesStyle);
        rowsStyle.Setters.Add(new Setter(FrameworkElement.WidthProperty, double.NaN));
        rowsStyle.Setters.Add(new Setter(FrameworkElement.HeightProperty, 60.0));
        rowsStyle.Setters.Add(new Setter(FrameworkElement.MarginProperty, new Thickness(6)));
        rowsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(StackPanel)));
        list.SizeChanged += (_, _) => list.Dispatcher.BeginInvoke((Action)AlignHeader, DispatcherPriority.Loaded);
        list.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler((_, _) => AlignHeader()));
        listTemplate = Template("""
            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="42"/><ColumnDefinition Width="*"/><ColumnDefinition Width="180"/></Grid.ColumnDefinitions>
            <local:ProfileIconBadge Width="30" Height="30"/>
            <TextBlock Grid.Column="1" Text="{Binding Name}" ToolTip="{Binding Name}" FontWeight="SemiBold" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" Margin="4,0,14,0"/>
            <TextBlock Grid.Column="2" Text="{Binding Summary}" ToolTip="{Binding Summary}" Foreground="{DynamicResource MutedForegroundColor}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
            </Grid>
            """);
        detailsTemplate = Template("""
            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="*"/><ColumnDefinition Width="170"/><ColumnDefinition Width="160"/></Grid.ColumnDefinitions>
            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="40"/><ColumnDefinition Width="*"/></Grid.ColumnDefinitions>
            <local:ProfileIconBadge Width="28" Height="28"/>
            <TextBlock Grid.Column="1" Text="{Binding Name}" ToolTip="{Binding Name}" FontWeight="SemiBold" VerticalAlignment="Center" TextTrimming="CharacterEllipsis" Margin="4,0,12,0"/></Grid>
            <TextBlock Grid.Column="1" Text="{Binding OutputDescription}" ToolTip="{Binding OutputDescription}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
            <TextBlock Grid.Column="2" Text="{Binding ModifiedDate}" Foreground="{DynamicResource MutedForegroundColor}" VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
            </Grid>
            """);
    }

    internal static void CreatePicker(WrapPanel host, ProfileViewChoice choice, Action<ProfileViewChoice> changed)
    {
        foreach (var item in new[] { (ProfileViewChoice.Tiles, UiText.Get("Tiles")), (ProfileViewChoice.List, UiText.Get("List")), (ProfileViewChoice.Details, UiText.Get("Details")) })
        {
            var button = new RadioButton { Content = item.Item2, GroupName = "ProfileView", IsChecked = choice == item.Item1,
                Padding = new Thickness(10, 7, 10, 7), Margin = new Thickness(0, 0, 4, 6) };
            button.SetResourceReference(FrameworkElement.StyleProperty, "BridgeSettingsCategoryStyle");
            var value = item.Item1;
            button.Checked += (_, _) => changed(value);
            host.Children.Add(button);
        }
    }

    internal void AlignHeader()
    {
        if (Choice != ProfileViewChoice.Details || list.Items.Count == 0) return;
        if (list.ItemContainerGenerator.ContainerFromIndex(0) is not ListBoxItem item) return;
        var presenter = Find<ContentPresenter>(item);
        var grid = presenter == null ? null : Find<Grid>(presenter);
        if (grid == null || grid.ActualWidth <= 0) return;
        double x = grid.TranslatePoint(new Point(), (UIElement)header.Parent).X;
        header.HorizontalAlignment = HorizontalAlignment.Left;
        // Stack-based rows use physical scrolling. Account for the actual
        // themed list inset (including its border), rather than guessing it.
        var scroll = Find<ScrollViewer>(list);
        double inset = item.TranslatePoint(new Point(), list).Y + (scroll?.VerticalOffset ?? 0);
        double gap = Math.Max(10, inset);
        header.Margin = new Thickness(x, gap, 0, gap - inset);
        header.Width = grid.ActualWidth;
    }

    private static T Find<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) return match;
            if (Find<T>(child) is T descendant) return descendant;
        }
        return null;
    }

    private static DataTemplate Template(string content) => (DataTemplate)XamlReader.Parse(
        "<DataTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\" xmlns:globalization=\"clr-namespace:System.Globalization;assembly=System.Runtime\" xmlns:local=\"clr-namespace:DS4WinWPF.DS4Forms;assembly=DS4Windows\">" + content + "</DataTemplate>");

    internal void Apply(ProfileViewChoice choice)
    {
        if (!Enum.IsDefined(choice)) choice = ProfileViewChoice.Tiles;
        Choice = choice;
        list.ItemsPanel = choice == ProfileViewChoice.Tiles ? tilesPanel : rowsPanel;
        list.ItemContainerStyle = choice == ProfileViewChoice.Tiles ? tilesStyle : rowsStyle;
        list.ItemTemplate = choice switch { ProfileViewChoice.List => listTemplate, ProfileViewChoice.Details => detailsTemplate, _ => tilesTemplate };
        header.Visibility = choice == ProfileViewChoice.Details ? Visibility.Visible : Visibility.Collapsed;
        if (choice == ProfileViewChoice.Details) list.Dispatcher.BeginInvoke((Action)AlignHeader, DispatcherPriority.Loaded);
    }
}
