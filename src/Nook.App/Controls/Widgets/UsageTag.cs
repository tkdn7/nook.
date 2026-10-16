using Nook.App.Services;
using Nook.App.ViewModels;

namespace Nook.App.Controls;

public static class UsageTag
{
    public static FrameworkElement Icon(string id, AppIconStore icons, Brush ink, double side)
    {
        var image = icons.Get(id);
        return image is not null ? new Image { Source = image, Width = side, Height = side, Stretch = Stretch.Uniform }
            : new FluentIcon { Glyph = "apps", Width = side, Height = side, Foreground = ink };
    }
    public static FrameworkElement Build(UsageShare share, WidgetViewModel vm, AppIconStore icons)
    {
        var stack = new StackPanel { Width = 230 };
        var heading = new Grid(); heading.ColumnDefinitions.Add(new() { Width = new(42) }); heading.ColumnDefinitions.Add(new());
        var icon = Icon(share.Id, icons, vm.Ink, 30); icon.HorizontalAlignment = HorizontalAlignment.Left; icon.VerticalAlignment = VerticalAlignment.Center; heading.Children.Add(icon);
        var labels = new StackPanel();
        labels.Children.Add(new TextBlock { Text = share.Name, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = vm.Ink, TextTrimming = TextTrimming.CharacterEllipsis });
        labels.Children.Add(new TextBlock { Text = $"{share.Percentage:F1}%  ·  {UsageLedger.Duration(share.Seconds)}", FontSize = 11, Foreground = vm.MutedInk, Margin = new(0, 4, 0, 0) });
        Grid.SetColumn(labels, 1); heading.Children.Add(labels); stack.Children.Add(heading);
        if (share.Members is { Count: > 0 } members)
        {
            var line = vm.MutedInk.CloneCurrentValue(); line.Opacity = .2;
            stack.Children.Add(new Border { Background = line, Height = 1, Margin = new(0, 10, 0, 4) });
            foreach (var app in members.Take(8))
            {
                var row = new Grid { Margin = new(0, 6, 0, 0) }; row.ColumnDefinitions.Add(new() { Width = new(28) }); row.ColumnDefinitions.Add(new()); row.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
                var appIcon = Icon(app.Id, icons, vm.Ink, 18); appIcon.HorizontalAlignment = HorizontalAlignment.Left; row.Children.Add(appIcon);
                var name = new TextBlock { Text = app.Name, FontSize = 11, Foreground = vm.Ink, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(name, 1); row.Children.Add(name);
                var percent = new TextBlock { Text = $"{app.Percentage:F1}%", FontSize = 11, Foreground = vm.MutedInk, VerticalAlignment = VerticalAlignment.Center, Margin = new(8, 0, 0, 0) }; Grid.SetColumn(percent, 2); row.Children.Add(percent); stack.Children.Add(row);
            }
            if (members.Count > 8) stack.Children.Add(new TextBlock { Text = $"외 {members.Count - 8}개 앱", FontSize = 10, Foreground = vm.MutedInk, Margin = new(28, 8, 0, 0) });
        }
        return stack;
    }
}
