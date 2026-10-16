using Nook.App.Services;
using Nook.App.ViewModels;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Threading.Tasks;

namespace Nook.App.Controls;

public sealed partial class WidgetFeaturesView
{
    private void BuildMedia()
    {
        var panel = new MediaLayoutPanel(); Children.Add(panel);
        _coverFallback = new FluentIcon { Glyph = "music_note_2", Width = 22, Height = 22, Foreground = _vm.MutedInk, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _cover = new Border { Tag = "cover", CornerRadius = new(8), Child = _coverFallback, Background = _vm.MutedInk.CloneCurrentValue() }; _cover.Background.Opacity = .10; panel.Children.Add(_cover);
        _primary = Label("재생 중인 미디어 없음", _vm.IsExpanded ? 18 : 13); _primary.Tag = "title"; _primary.FontWeight = FontWeights.SemiBold; System.Windows.Automation.AutomationProperties.SetName(_primary, "현재 곡"); panel.Children.Add(_primary);
        _secondary = Label("", 11, true); _secondary.Tag = "artist"; System.Windows.Automation.AutomationProperties.SetName(_secondary, "아티스트 및 앱"); panel.Children.Add(_secondary);
        var controls = new StackPanel { Tag = "controls", Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        _previous = IconButton("previous", "이전", () => _ = Control("previous"));
        _play = IconButton("play", "재생 / 일시정지", () => _ = Control(_vm.SelectedMedia?.Playing == true ? "pause" : "play"));
        _playIcon = (FluentIcon)_play.Content; _playIcon.Width = _playIcon.Height = 22;
        _next = IconButton("next", "다음", () => _ = Control("next"));
        foreach (var button in new[] { _previous, _play, _next }) { button.SetResourceReference(StyleProperty, "MediaTransportButton"); button.Margin = new(6, 0, 6, 0); button.Width = 32; button.Height = 34; }
        controls.Children.Add(_previous); controls.Children.Add(_play); controls.Children.Add(_next); panel.Children.Add(controls);
        _progress = new SmoothProgressBar { Tag = "progress", Maximum = 100, Height = 3, Foreground = _vm.Ink, Background = _vm.MutedInk }; System.Windows.Automation.AutomationProperties.SetName(_progress, "재생 진행률"); panel.Children.Add(_progress);
        _time = Label("", 10, true); _time.Tag = "time"; _time.HorizontalAlignment = HorizontalAlignment.Center; System.Windows.Automation.AutomationProperties.SetName(_time, "재생 시간"); panel.Children.Add(_time);
        _status = Label("", 10, true);
    }
    private async Task Control(string operation)
    {
        if (_controlBusy) return;
        _controlBusy = true; UpdateMedia();
        try { await _app.MediaService.ControlAsync(_vm.SelectedMediaId, operation); }
        finally { _controlBusy = false; if (!_disposed) UpdateMedia(); }
    }
    private void UpdateMedia()
    {
        if (_primary is null) return;
        var s = _vm.SelectedMedia;
        _primary.Text = s is null ? _vm.Model.Media.Mode == MediaMode.Pinned ? "고정한 앱을 기다리는 중" : "재생 중인 미디어 없음" : string.IsNullOrEmpty(s.Title) ? s.AppName : s.Title;
        _secondary!.Text = s is null ? "" : string.IsNullOrEmpty(s.Artist) ? s.AppName : $"{s.Artist} · {s.AppName}";
        if (_cover is not null) { _cover.Background = s?.Artwork is { } art ? new ImageBrush(art) { Stretch = Stretch.UniformToFill } : new SolidColorBrush(_vm.IsDark ? Color.FromRgb(45,45,45) : Color.FromRgb(229,223,213)); _coverFallback!.Visibility = s?.Artwork is null ? Visibility.Visible : Visibility.Collapsed; }
        _playIcon!.Glyph = s?.Playing == true ? "pause" : "play";
        _play!.IsEnabled = !_controlBusy && s is not null && (s.Playing ? s.CanPause : s.CanPlay);
        _previous!.IsEnabled = !_controlBusy && s?.CanPrevious == true; _next!.IsEnabled = !_controlBusy && s?.CanNext == true;
        _progress!.TargetValue = s is not null && s.Duration > TimeSpan.Zero ? s.Position.TotalSeconds / s.Duration.TotalSeconds * 100 : 0;
        _time!.Text = s is null ? "—" : s.Duration > TimeSpan.Zero ? _vm.IsStandard
            ? $"{Time(s.Position)} / {Time(s.Duration)}  ·  −{Time(s.Duration - s.Position)}" : $"−{Time(s.Duration - s.Position)}" : "남은 시간 없음";
        ToolTip = _app.MediaService.Error;
        _status!.Text = !string.IsNullOrEmpty(_app.MediaService.Error) ? _app.MediaService.Error : s is null ? "" : $"{s.AppName} · {(_vm.Model.Media.Mode == MediaMode.Pinned ? "고정" : "자동")}";
    }
    private static string Time(TimeSpan value) => value.TotalHours >= 1 ? $"{(int)value.TotalHours}:{value.Minutes:00}:{value.Seconds:00}" : $"{(int)value.TotalMinutes}:{value.Seconds:00}";
}
