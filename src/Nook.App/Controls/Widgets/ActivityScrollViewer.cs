using System.Runtime.CompilerServices;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Nook.App.Controls;

/// <summary>Horizontal history navigation; transient state belongs to the widget, not the view.</summary>
public sealed class ActivityScrollViewer : ScrollViewer
{
    private sealed class ScrollState
    {
        public bool Latest = true;
        public double Offset;
    }
    private static readonly ConditionalWeakTable<WidgetInstance, ScrollState> States = new();
    private readonly ScrollState _state;
    private HwndSource? _source;
    private long _revision;
    private bool _restoring;
    private double? _requestedOffset;
    private Point? _panStart;
    private double _panOffset;
    private bool _panning;
    private static readonly DependencyPropertyKey CanScrollLeftPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanScrollLeft), typeof(bool), typeof(ActivityScrollViewer), new PropertyMetadata(false));
    private static readonly DependencyPropertyKey CanScrollRightPropertyKey =
        DependencyProperty.RegisterReadOnly(nameof(CanScrollRight), typeof(bool), typeof(ActivityScrollViewer), new PropertyMetadata(false));
    public static readonly DependencyProperty CanScrollLeftProperty = CanScrollLeftPropertyKey.DependencyProperty;
    public static readonly DependencyProperty CanScrollRightProperty = CanScrollRightPropertyKey.DependencyProperty;
    public bool CanScrollLeft => (bool)GetValue(CanScrollLeftProperty);
    public bool CanScrollRight => (bool)GetValue(CanScrollRightProperty);

    public ActivityScrollViewer() : this(new WidgetInstance()) { }
    public ActivityScrollViewer(WidgetInstance model)
    {
        _state = States.GetValue(model, _ => new());
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Disabled;
        CanContentScroll = false;
        Focusable = true;
        AutomationProperties.SetName(this, "ActivityScroll");
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public void ScrollBy(double dip)
    {
        if (!double.IsFinite(dip)) return;
        Navigate((_requestedOffset ?? HorizontalOffset) + dip);
    }

    private void Navigate(double offset)
    {
        _revision++; // Any manual input cancels queued automatic restoration.
        _restoring = false;
        offset = Math.Clamp(offset, 0, ScrollableWidth);
        _requestedOffset = offset;
        if (ScrollableWidth > 0)
        {
            _state.Offset = offset;
            _state.Latest = offset >= ScrollableWidth - 2;
        }
        ScrollToHorizontalOffset(offset);
    }

    protected override void OnPreviewMouseWheel(MouseWheelEventArgs e)
    {
        if (!e.Handled && Keyboard.Modifiers == ModifierKeys.None && ScrollableWidth > 0)
        {
            ScrollBy(-e.Delta / 120.0 * 40);
            e.Handled = true;
        }
        base.OnPreviewMouseWheel(e);
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (!e.Handled && Keyboard.Modifiers == ModifierKeys.None && ScrollableWidth > 0)
        {
            switch (e.Key)
            {
                case Key.Left: ScrollBy(-40); break;
                case Key.Right: ScrollBy(40); break;
                case Key.Home: Navigate(0); break;
                case Key.End: Navigate(ScrollableWidth); break;
                default: base.OnPreviewKeyDown(e); return;
            }
            e.Handled = true;
        }
        base.OnPreviewKeyDown(e);
    }

    protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        _revision++;
        _requestedOffset = null;
        _restoring = false;
        if (!e.Handled && Keyboard.Modifiers == ModifierKeys.None && ScrollableWidth > 0 && !IsInteractive(e.OriginalSource as DependencyObject))
        {
            Focus();
            _panStart = e.GetPosition(this);
            _panOffset = HorizontalOffset;
        }
        base.OnPreviewMouseLeftButtonDown(e);
    }

    protected override void OnPreviewMouseMove(MouseEventArgs e)
    {
        if (_panStart is Point origin && e.LeftButton == MouseButtonState.Pressed)
        {
            if (Keyboard.Modifiers != ModifierKeys.None) EndPan();
            else
            {
                double delta = e.GetPosition(this).X - origin.X;
                if (!_panning && Math.Abs(delta) >= SystemParameters.MinimumHorizontalDragDistance)
                    _panning = CaptureMouse();
                if (_panning) { Navigate(_panOffset - delta); e.Handled = true; }
            }
        }
        else EndPan();
        base.OnPreviewMouseMove(e);
    }

    protected override void OnPreviewMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        bool wasPanning = _panning;
        EndPan();
        if (wasPanning) e.Handled = true;
        base.OnPreviewMouseLeftButtonUp(e);
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        _panStart = null;
        _panning = false;
        base.OnLostMouseCapture(e);
    }

    private void EndPan()
    {
        _panStart = null;
        _panning = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    private static bool IsInteractive(DependencyObject? element)
    {
        while (element is not null)
        {
            if (element is ButtonBase or ScrollBar or Thumb or TextBoxBase) return true;
            element = element is Visual ? VisualTreeHelper.GetParent(element) : LogicalTreeHelper.GetParent(element);
        }
        return false;
    }

    protected override void OnScrollChanged(ScrollChangedEventArgs e)
    {
        base.OnScrollChanged(e);
        if (!ReferenceEquals(e.OriginalSource, this)) return;
        UpdateEdges();
        if (e.ExtentWidthChange != 0 || e.ViewportWidthChange != 0)
            QueueRestore();
        else if (!_restoring && ScrollableWidth > 0)
        {
            _state.Offset = HorizontalOffset;
            _state.Latest = HorizontalOffset >= ScrollableWidth - 2;
        }
        if (_requestedOffset is double requested && Math.Abs(requested - HorizontalOffset) < 0.5)
            _requestedOffset = null;
    }

    private void QueueRestore()
    {
        long revision = ++_revision;
        _restoring = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        {
            if (revision != _revision || !IsLoaded) return;
            // Preserve historical position when Small has no overflow, for the next Mid/Big view.
            double target = _state.Latest ? ScrollableWidth : Math.Clamp(_state.Offset, 0, ScrollableWidth);
            ScrollToHorizontalOffset(target);
            Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(() =>
            {
                if (revision == _revision) { _restoring = false; UpdateEdges(); }
            }));
        }));
    }

    private void UpdateEdges()
    {
        SetValue(CanScrollLeftPropertyKey, HorizontalOffset > 1);
        SetValue(CanScrollRightPropertyKey, HorizontalOffset < ScrollableWidth - 1);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _source = PresentationSource.FromVisual(this) as HwndSource;
        _source?.AddHook(WheelHook);
        QueueRestore();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _revision++;
        _restoring = false;
        _source?.RemoveHook(WheelHook);
        _source = null;
        EndPan();
    }

    private IntPtr WheelHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (handled || (message != 0x020A && message != 0x020E) || !IsVisible || ScrollableWidth <= 0 || Keyboard.Modifiers != ModifierKeys.None)
            return IntPtr.Zero;
        long packed = lParam.ToInt64();
        Point point = PointFromScreen(new Point((short)(packed & 0xffff), (short)((packed >> 16) & 0xffff)));
        // A wheel in the title or another widget must never navigate this history.
        if (point.X < 0 || point.Y < 0 || point.X >= ViewportWidth || point.Y >= ViewportHeight) return IntPtr.Zero;
        int delta = (short)((wParam.ToInt64() >> 16) & 0xffff);
        ScrollBy(delta / 120.0 * (message == 0x020E ? 40 : -40));
        handled = true; // suppress WPF's second routing of the same native message
        return IntPtr.Zero;
    }
}
