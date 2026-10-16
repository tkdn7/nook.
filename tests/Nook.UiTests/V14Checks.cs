using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Nook.Core;
using Nook.App.Controls;
using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program
{
    private static void RunV14Checks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)
    {
        int good=0,bad=0;
        void Check(string name,Action test) { try { test();good++;Console.WriteLine("PASS V14 "+name); } catch(Exception ex) {bad++;Console.WriteLine("FAIL V14 "+name+": "+ex);} }
        void Capture(FrameworkElement root,string path) {root.UpdateLayout();SaveV10Image(root,path);}
        foreach(var kind in new[]{WidgetKind.Dice,WidgetKind.KeyStats})
        Check("native limits "+kind,()=>
        {
            var widget=new WidgetViewModel(vm,new(){Kind=kind,Width=999,Height=999});var window=new WidgetWindow(vm,widget);window.Show();Pump(20);
            try
            {
                window.ResizeAnimated(999,999);Pump(20);
                void Bounds() { var bounds=V10Bounds(window);double scale=VisualTreeHelper.GetDpi(window).DpiScaleX;if(bounds.Width>WidgetLimits.MaxWidth(kind)*scale+1 || bounds.Height>WidgetLimits.MaxHeight(kind)*scale+1)throw new Exception("Native limit escaped");if(kind==WidgetKind.Dice && Math.Min(bounds.Width,bounds.Height)>280*scale+1)throw new Exception("Dice short side escaped"); }
                Bounds();window.Width=999;window.Height=999;Pump(20);Bounds();
                typeof(WidgetWindow).GetProperty("GetPointerPosition",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.SetValue(window,(Func<(double X,double Y)?>)(()=>null));
                foreach(bool magnetic in new[]{false,true}) {settings.MagneticMode=magnetic;var grip=AllChildren(window).OfType<Thumb>().Single(t=>Equals(t.Tag,"RB") && t.IsVisible);grip.RaiseEvent(new DragStartedEventArgs(0,0));grip.RaiseEvent(new DragDeltaEventArgs(900,900));grip.RaiseEvent(new DragCompletedEventArgs(900,900,false));Pump(20);Bounds();}
                if(kind==WidgetKind.Dice){window.ResizeAnimated(200,300);Pump(20);if(window.ActualHeight<=window.ActualWidth)throw new Exception("Portrait rejected");}
                else if(AllText(window).Any(t=>t.Text.Contains("누적 타자 수")))throw new Exception("Old caption remains");
            }
            finally {window.Close();settings.MagneticMode=false;}
        });
        Check("presets are independent and all main options use switches",()=>
        {
            using var own=new AppViewModel(new(),new SettingsStore(Path.Combine(output,"v14-main")),vm.Metrics,theme);
            own.Add(WidgetKind.SystemShelf);own.SavePreset("집중");var main=new MainWindow(own);main.Show();Pump(30);
            try
            {
                foreach(string nav in new[]{"LibraryNav","ManageNav","SettingsNav","PresetsNav"}) {((Button)main.FindName(nav)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(20);if(AllChildren(main).OfType<CheckBox>().Any())throw new Exception("Checkbox remains");}
                var page=(FrameworkElement)main.FindName("PresetsPage");var host=(FrameworkElement)main.FindName("PresetHost");if(page.Visibility!=Visibility.Visible || !page.IsAncestorOf(host) || ((FrameworkElement)main.FindName("SettingsPage")).IsAncestorOf(host))throw new Exception("Presets page not independent");
                Capture((FrameworkElement)main.Content,Path.Combine(output,"V14-Presets.png"));
                ((Button)main.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(20);
                foreach(var toggle in AllChildren(main).OfType<ToggleButton>().Where(x=>x.IsVisible && x.Content is string)){toggle.ApplyTemplate();if(!AllChildren(toggle).OfType<Border>().Any(x=>x.Name=="SwitchTrack" && Math.Abs(x.ActualWidth-46)<1))throw new Exception("Wrong switch template "+toggle.Content);}
                Capture((FrameworkElement)main.Content,Path.Combine(output,"V14-Settings.png"));
                foreach(var pair in new[]{(WidgetKind.Activity,"오늘 사용 시간 표시"),(WidgetKind.MediaController,"볼륨 슬라이더 표시")})
                {
                    var w=own.Add(pair.Item1);main.OpenWidgetEditor(w);Pump(20);var toggle=AllChildren(main).OfType<ToggleButton>().Single(x=>Equals(x.Content,pair.Item2));toggle.IsChecked=pair.Item1!=WidgetKind.Activity;toggle.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    if(w.Model.ActivityShowScreenTime && pair.Item1==WidgetKind.Activity || !w.Model.MediaControllerShowVolume && pair.Item1==WidgetKind.MediaController)throw new Exception("Editor toggle failed");
                }
            }
            finally {main.Exiting=true;main.Close();}
        });
        Check("startup opt-in, command quoting, one-time prompt and failure rollback",()=>
        {
            foreach(bool enable in new[]{false,true})
            {
                string? command=null;int writes=0;var startup=new StartupService(()=>command,value=>{command=value;writes++;},@"C:\Nook Folder\Nook.exe");
                var config=new AppSettings();using var own=new AppViewModel(config,new SettingsStore(Path.Combine(output,"v14-startup-"+enable)),vm.Metrics,theme,startup);
                var main=new MainWindow(own);main.Show();Pump(20);
                try
                {
                    if(writes!=0 || own.StartupEnabled)throw new Exception("Startup was enabled without choice");
                    main.Dispatcher.BeginInvoke(()=>{var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Owner==main && w.Title=="Nook 자동 실행");Capture((FrameworkElement)dialog.Content,Path.Combine(output,"V14-Startup.png"));AllChildren(dialog).OfType<Button>().Single(b=>Equals(b.Content,enable?"자동 실행 켜기":"나중에")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));});
                    main.ShowStartupRecommendation();if(!config.StartupPromptShown || own.StartupEnabled!=enable || writes!=(enable?1:0))throw new Exception("Startup choice failed");
                    main.ShowStartupRecommendation();if(writes!=(enable?1:0))throw new Exception("Prompt repeated");
                    if(enable){if(command!= "\"C:\\Nook Folder\\Nook.exe\" --background")throw new Exception("Unquoted executable");own.StartupEnabled=false;if(command is not null)throw new Exception("Disable did not remove entry");}
                }
                finally{main.Exiting=true;main.Close();}
            }
            using var failure=new AppViewModel(new(),new SettingsStore(Path.Combine(output,"v14-startup-failed")),vm.Metrics,theme,new StartupService(()=>null,_=>throw new IOException("blocked"),@"C:\Nook.exe"));failure.StartupEnabled=true;if(failure.StartupEnabled || string.IsNullOrEmpty(failure.Status))throw new Exception("Write failure was not reported/rolled back");
            var failedMain=new MainWindow(failure);failedMain.Show();Pump(20);
            try {((Button)failedMain.FindName("SettingsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(20);var toggle=AllChildren(failedMain).OfType<ToggleButton>().Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="시작 프로그램");toggle.IsChecked=true;Pump(20);if(toggle.IsChecked!=false)throw new Exception("Failed startup toggle did not roll back visually");}
            finally{failedMain.Exiting=true;failedMain.Close();}
            if(vm.CanConfigureStartup)throw new Exception("Isolated settings can write registry");
        });
        foreach(var mode in new[]{ThemeMode.Light,ThemeMode.Dark})
        foreach(var requested in new[]{new Size(180,140),new Size(260,170),new Size(420,280),new Size(180,240),new Size(200,300),new Size(280,420)})
        Check("dice portrait/landscape "+mode+requested,()=>
        {
            var widget=new WidgetViewModel(vm,new(){Kind=WidgetKind.Dice,Theme=mode,Width=requested.Width,Height=requested.Height,DiceCount=3});var window=new WidgetWindow(vm,widget);
            try{var root=(FrameworkElement)window.Content;root.Measure(requested);root.Arrange(new Rect(requested));root.UpdateLayout();if(AllChildren(root).OfType<DiceVisual>().Single().Portrait!=(requested.Height>requested.Width*1.1))throw new Exception("Wrong dice direction");ValidateText(root,requested);Capture(root,Path.Combine(output,$"V14-Dice-{mode}-{requested.Width}x{requested.Height}.png"));}
            finally{window.Close();}
        });
        vm.Keys.Data.Total=12704;
        typeof(AppViewModel).GetProperty("Volume")!.SetValue(vm,(int?)67);
        foreach(var mode in new[]{ThemeMode.Light,ThemeMode.Dark})
        foreach(var kind in new[]{WidgetKind.Clock,WidgetKind.KeyStats,WidgetKind.Activity,WidgetKind.MediaController})
        foreach(double side in new[]{180.0,300.0,420.0})
        Check("updated render "+kind+mode+side,()=>
        {
            var model=new WidgetInstance{Kind=kind,Theme=mode,Width=side,Height=side,ActivityShowScreenTime=false,MediaControllerShowVolume=true,Shape=kind==WidgetKind.Clock?WidgetShape.Circle:WidgetShape.Card,ClockStyle=ClockStyle.Analog};
            var widget=new WidgetViewModel(vm,model);var window=new WidgetWindow(vm,widget);
            try
            {
                var root=(FrameworkElement)window.Content;var size=new Size(model.Width,model.Height);root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                foreach(var clock in AllChildren(root).OfType<ClockVisual>()) {clock.Now=()=>new DateTimeOffset(2026,10,13,10,8,0,TimeSpan.FromHours(9));clock.InvalidateVisual();if(clock.IsVisible && Math.Abs(clock.FaceRadius/Math.Min(clock.ActualWidth,clock.ActualHeight)-.465)>1e-6)throw new Exception("Circle face has excess gap");}
                if(kind==WidgetKind.Clock && widget.ShowCircleTitle)throw new Exception("Clock title overlaps face");
                if(kind==WidgetKind.MediaController){var level=AllChildren(root).OfType<LevelControl>().Single();if(!level.Compact || level.ActualHeight!=28)throw new Exception("Output volume is not a compact stick");level.Available=true;level.Value=67;level.Status="";level.InvalidateVisual();}
                if(kind==WidgetKind.Activity && AllText(root).Any(t=>t.IsVisible && t.Text==UsageLedger.Duration(vm.ScreenTime.Today.TotalSeconds)))throw new Exception("Activity time remains visible");
                foreach(double scale in new[]{1.0,1.5,2.0}) {var bitmap=new RenderTargetBitmap((int)(size.Width*scale),(int)(size.Height*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(root);ValidateText(root,size);}
                Capture(root,Path.Combine(output,$"V14-{kind}-{mode}-{model.Width}x{model.Height}.png"));
                if(kind==WidgetKind.MediaController){model.MediaControllerShowVolume=false;widget.RefreshAppearance();root.UpdateLayout();if(AllChildren(root).OfType<LevelControl>().Any())throw new Exception("OFF kept volume control");}
            }
            finally{window.Close();}
        });
        Check("compact slider keyboard input",()=>
        {
            var level=new LevelControl {Compact=true,Available=true,Value=50,Orientation=ControlOrientation.Horizontal};var host=new Window {Width=240,Height=80,Content=level};host.Show();Pump(20);
            try {double written=-1;level.Adjusted+=v=>written=v;level.RaiseEvent(new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,PresentationSource.FromVisual(level),0,System.Windows.Input.Key.Right){RoutedEvent=System.Windows.Input.Keyboard.KeyDownEvent});if(written!=51)throw new Exception("Compact slider does not publish input");}
            finally{host.Close();}
        });
        Motion.EnabledOverride=true;
        foreach(var mode in new[]{ThemeMode.Light,ThemeMode.Dark})
        foreach(var shape in new[]{WidgetShape.Card,WidgetShape.Circle})
        foreach(var requested in new[]{new Size(180,140),new Size(300,200),new Size(420,310),new Size(640,140),new Size(180,480)})
        Check("breathe responsive "+mode+shape+requested,()=>
        {
            var widget=new WidgetViewModel(vm,new(){Kind=WidgetKind.Breathe,Theme=mode,Shape=shape,Width=requested.Width,Height=requested.Height});var window=new WidgetWindow(vm,widget);
            try
            {
                var root=(FrameworkElement)window.Content;var size=new Size(widget.Model.Width,widget.Model.Height);root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();var breathe=AllChildren(root).OfType<BreatheVisual>().Single();
                foreach(double time in new[]{0.0,2.0,4.5}) {breathe.PreviewSeconds=time;breathe.InvalidateVisual();Capture(root,Path.Combine(output,$"V14-Breathe-{mode}-{shape}-{size.Width}x{size.Height}-{time}.png"));foreach(double scale in new[]{1.0,1.5,2.0}){var bitmap=new RenderTargetBitmap((int)(size.Width*scale),(int)(size.Height*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(root);ValidateText(root,size);}}
            }
            finally{window.Close();}
        });
        Check("breathe pauses hidden, retains phase and obeys motion OFF",()=>
        {
            var widget=new WidgetViewModel(vm,new(){Kind=WidgetKind.Breathe});var window=new WidgetWindow(vm,widget);window.Show();Pump(150);
            try
            {
                var breathe=AllChildren(window).OfType<BreatheVisual>().Single();if(!breathe.Animating)throw new Exception("Visible animation not attached");window.Hide();Pump(20);double paused=breathe.Expansion;Pump(100);if(breathe.Animating || Math.Abs(paused-breathe.Expansion)>1e-6)throw new Exception("Hidden animation advanced");
                window.Show();Pump(30);if(!breathe.Animating || breathe.Expansion<paused)throw new Exception("Resume reset phase");Motion.EnabledOverride=false;widget.RefreshAppearance();Pump(40);if(breathe.Animating || breathe.Expansion!=.5)throw new Exception("Motion OFF animates");
            }
            finally{window.Close();}
        });
        Motion.EnabledOverride=true;
        Check("breathe normal-speed preview frames",()=>
        {
            string frames=Path.Combine(output,"breathe-frames");Directory.CreateDirectory(frames);
            foreach(var shape in new[]{WidgetShape.Card,WidgetShape.Circle})
            {
                var widget=new WidgetViewModel(vm,new(){Kind=WidgetKind.Breathe,Theme=ThemeMode.Dark,Shape=shape,Width=300,Height=shape==WidgetShape.Card?200:300});var window=new WidgetWindow(vm,widget);
                try {var root=(FrameworkElement)window.Content;var size=new Size(widget.Model.Width,widget.Model.Height);root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();var breathe=AllChildren(root).OfType<BreatheVisual>().Single();for(int frame=0;frame<54;frame++){breathe.PreviewSeconds=frame/12.0;breathe.InvalidateVisual();Capture(root,Path.Combine(frames,$"{shape}-{frame:D3}.png"));}}
                finally{window.Close();}
            }
        });
        Motion.EnabledOverride=false;passed+=good;failed+=bad;
    }
}
