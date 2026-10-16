using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Interop;
using Nook.Core;
using Nook.App.Controls;
using Nook.App.Services;
using Nook.App.ViewModels;
using Nook.App.Views;
using ThemeMode = Nook.Core.ThemeMode;

internal static partial class Program
{
    private static void RunV13Checks(string output, AppViewModel vm, AppSettings settings, ThemeService theme, ref int passed, ref int failed)
    {
        int good = 0, bad = 0;
        void Check(string name, Action test)
        { try { test(); good++; Console.WriteLine("PASS V13 " + name); } catch (Exception ex) { bad++; Console.WriteLine("FAIL V13 " + name + ": " + ex); } }
        void Await(Task task) { var watch = Stopwatch.StartNew(); while (!task.IsCompleted && watch.Elapsed.TotalSeconds < 8) Pump(10); if (!task.IsCompleted) throw new Exception("Async operation did not finish"); task.GetAwaiter().GetResult(); }
        foreach (var mode in new[] { ThemeMode.Light, ThemeMode.Dark })
        foreach (double side in new[] { 180.0, 300.0, 420.0 })
        foreach (var kind in new[] { WidgetKind.ScreenTime, WidgetKind.Volume, WidgetKind.Brightness, WidgetKind.Timer })
        Check($"circle template {kind}/{mode}/{side}", () =>
        {
            var widget = new WidgetViewModel(vm,new(){Kind=kind,Shape=WidgetShape.Circle,Theme=mode,Width=side,Height=side}); var window = new WidgetWindow(vm,widget);
            try
            {
                var root=(FrameworkElement)window.Content; root.Measure(new(side,side));root.Arrange(new(0,0,side,side));root.UpdateLayout();
                foreach(var level in AllChildren(root).OfType<LevelControl>()) { level.Available=true;level.Value=68;level.Status="";level.InvalidateVisual(); }
                SaveV10Image(root,Path.Combine(output,$"V13-Ring-{kind}-{mode}-{side}.png"));
                var title=(TextBlock)window.FindName("CircleTitleLabel");var titleBounds=title.TransformToAncestor(root).TransformBounds(new Rect(title.RenderSize));
                var feature=(Border)window.FindName("CircleFeatureHost");double surface=feature.ActualWidth;
                // All title text stays inside the ring, below its upper boundary.
                if(titleBounds.Top < 6+surface*.20 || titleBounds.Bottom > 6+surface*.44) throw new Exception("Title escaped reserved ring interior");
                foreach(var control in AllChildren(root).OfType<LevelControl>().Where(l=>l.IsVisible))
                    if(control.IsAdjustmentPoint(new(surface/2,surface*.5)) || !control.IsAdjustmentPoint(new(surface*.9,surface*.5))) throw new Exception("Knob blocks movement or loses ring input");
            }
            finally{window.Close();}
        });
        foreach(var size in new[]{new Size(240,300),new Size(300,400),new Size(480,460),new Size(640,480)})
        Check("calculator layout " + size, () =>
        {
            var widget=new WidgetViewModel(vm,new(){Kind=WidgetKind.Calculator,Width=size.Width,Height=size.Height});var window=new WidgetWindow(vm,widget);
            try
            {
                var root=(FrameworkElement)window.Content;root.Measure(size);root.Arrange(new Rect(size));root.UpdateLayout();
                var view=AllChildren(root).OfType<CalculatorView>().Single(); bool scientific=size.Width>=404 && size.Height>=357;
                if(view.Scientific!=scientific)throw new Exception("Wrong scientific density");
                var input=AllChildren(view).OfType<TextBox>().Single();input.Text="sin(30)+sqrt(9)";
                if(!AllText(view).Any(t=>t.Text=="3.5"))throw new Exception("Live result failed");
                AllChildren(view).OfType<Button>().Single(b=>Equals(b.Content,"=")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(widget.Calculator.CompletedExpression!="sin(30)+sqrt(9)" || widget.Calculator.Answer!=3.5)throw new Exception("Expression/result lost after equals");
                widget.Model.Theme=ThemeMode.Dark;widget.RefreshAppearance();root.UpdateLayout();
                if(widget.Calculator.Expression!="sin(30)+sqrt(9)")throw new Exception("Resize/theme reset expression");
                ValidateText(root,size);SaveV10Image(root,Path.Combine(output,$"V13-Calculator-{size.Width}x{size.Height}.png"));
                foreach(var button in AllChildren(root).OfType<Button>().Where(b=>b.IsVisible && b.Content is string))if(button.ActualHeight<20)throw new Exception("Calculator key too small");
            }
            finally{window.Close();}
        });
        Check("memo top-left, no border, calculator routing and focused append",()=>
        {
            var ownSettings=new AppSettings();using var own=new AppViewModel(ownSettings,new SettingsStore(Path.Combine(output,"routing")),vm.Metrics,theme);
            var memo=own.Add(WidgetKind.Memo);var calculator=own.Add(WidgetKind.Calculator);var window=new WidgetWindow(own,memo);window.Show();Pump(20);
            try
            {
                var root=(FrameworkElement)window.Content;var input=AllChildren(root).OfType<TextBox>().Single();input.Focus();input.Text="first";
                if(input.VerticalContentAlignment!=VerticalAlignment.Top || input.HorizontalContentAlignment!=HorizontalAlignment.Left)throw new Exception("Memo alignment wrong");
                if(AllChildren(input).OfType<Border>().Any(b=>b.BorderThickness.Left>0))throw new Exception("Memo focus border remains");
                if(!own.SendCalculation(calculator,"2+3","5") || !input.Text.Contains("2+3 = 5"))throw new Exception("Single/focused memo did not receive result");
                var memo2=own.Add(WidgetKind.Memo); calculator.Model.CalculatorMemoId=memo2.Model.Id;
                if(!own.SendCalculation(calculator,"6*7","42") || memo2.Model.MemoText!="6*7 = 42" || memo.Model.MemoText.Contains("42"))throw new Exception("Configured target wrong");
                bool editor=false;own.WidgetEditorRequested+=w=>editor=true;calculator.Model.CalculatorMemoId=null;
                if(own.SendCalculation(calculator,"1+1","2") || !editor)throw new Exception("Multiple memos require choice");
                SaveV10Image(root,Path.Combine(output,"V13-Memo.png"));
            }
            finally{window.Close();}
        });
        Check("system toggle is immediate, shared, and rolls back on failure",()=>
        {
            using var own=new AppViewModel(new(),new SettingsStore(Path.Combine(output,"system-pending")),vm.Metrics,theme);
            typeof(AppViewModel).GetProperty("Muted")!.SetValue(own,(bool?)false);
            var first=own.Add(WidgetKind.SystemShelf);var second=own.Add(WidgetKind.SystemShelf);
            using var a=new WidgetFeaturesView(own,first);using var b=new WidgetFeaturesView(own,second);
            foreach(var view in new[]{a,b}) { view.Measure(new(300,200));view.Arrange(new(0,0,300,200));view.UpdateLayout(); }
            var completion=new TaskCompletionSource<bool?>();typeof(AppViewModel).GetProperty("SystemToggleOverride",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(own,(Func<SystemAction,Task<bool?>>)(_=>completion.Task));
            var action=own.ToggleSystemAsync(SystemAction.Mute);
            if(own.SystemState(SystemAction.Mute)!=true || !own.SystemBusy(SystemAction.Mute) || action.IsCompleted)throw new Exception("No immediate optimistic state");
            foreach(var view in new[]{a,b})if(AllChildren(view).OfType<Button>().Single(x=>System.Windows.Automation.AutomationProperties.GetName(x)=="음소거").IsEnabled)throw new Exception("Rapid repeated toggle was not blocked");
            completion.SetResult(true);Await(action);if(own.SystemBusy(SystemAction.Mute) || own.SystemState(SystemAction.Mute)!=true)throw new Exception("Actual state not applied");
            var failure=new TaskCompletionSource<bool?>();typeof(AppViewModel).GetProperty("SystemToggleOverride",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(own,(Func<SystemAction,Task<bool?>>)(_=>failure.Task));
            action=own.ToggleSystemAsync(SystemAction.Mute);if(own.SystemState(SystemAction.Mute)!=false)throw new Exception("Second action did not respond");failure.SetException(new IOException("test"));
            try{Await(action);}catch(IOException){}
            if(own.SystemBusy(SystemAction.Mute) || own.SystemState(SystemAction.Mute)!=true)throw new Exception("Failed action did not restore state");
        });
        Check("output devices: simulated switch, removal and failed switching",()=>
        {
            string active="a";bool removed=false,fail=false;int writes=0;
            var service=new AudioOutputService(()=>removed?[new("a","Speaker",true)]:[new("a","Speaker",active=="a"),new("b","Headphones",active=="b")],id=>{writes++;if(fail)throw new IOException("failed");active=id;});
            Await(service.RefreshAsync(true));Await(service.SelectAsync("b"));if(service.Error is not null || !service.Devices.Single(d=>d.Id=="b").Default)throw new Exception("Default endpoint selection failed");
            removed=true;Await(service.SelectAsync("b"));if(writes!=1 || service.Error is null)throw new Exception("Disconnected endpoint was written");
            removed=false;fail=true;Await(service.SelectAsync("a"));if(service.Busy || service.Error is null || active!="b")throw new Exception("Failure left stale active/busy state");
        });
        Check("real output enumeration (read-only)",()=>
        {
            Await(vm.AudioOutput.RefreshAsync(true));if(vm.AudioOutput.Error is not null)throw new Exception(vm.AudioOutput.Error);
            if(vm.AudioOutput.Devices.Any(d=>string.IsNullOrWhiteSpace(d.Id)||string.IsNullOrWhiteSpace(d.Name)))throw new Exception("Invalid endpoint properties");
            File.WriteAllLines(Path.Combine(output,"audio-output-live.txt"),vm.AudioOutput.Devices.Select(d=>$"{d.Name} · default={d.Default} · {d.Id}"));
            var widget=new WidgetViewModel(vm,new(){Kind=WidgetKind.MediaController,Width=420,Height=310});var window=new WidgetWindow(vm,widget);
            try{var root=(FrameworkElement)window.Content;root.Measure(new(420,310));root.Arrange(new(0,0,420,310));root.UpdateLayout();SaveV10Image(root,Path.Combine(output,"V13-Output.png"));}finally{window.Close();}
        });
        Check("Screen Time setting filters existing Nook without deleting records",()=>
        {
            using var own=new AppViewModel(new(),new SettingsStore(Path.Combine(output,"usage-exclude")),vm.Metrics,theme);
            own.ScreenTime.Ledger.Days.Add(new(){Date=DateOnly.FromDateTime(DateTime.Now),Apps=[new(){Id="Nook",Seconds=60},new(){Id="code",Seconds=40}]});
            var main=new MainWindow(own);main.Show();Pump(20);
            try
            {var check=AllChildren(main).OfType<ToggleButton>().Single(x=>Equals(x.Content,"Screen Time에서 Nook 제외"));check.IsChecked=true;check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));if(own.ScreenTime.Today.TotalSeconds!=40 || own.ScreenTime.Ledger.Days[0].TotalSeconds!=100)throw new Exception("Filter changed history or failed");}
            finally{main.Exiting=true;main.Close();}
        });
        Check("key stats migration retains total and removes per-key persistence",()=>
        {
            string directory=Path.Combine(output,"keys-migrate");Directory.CreateDirectory(directory);File.WriteAllText(Path.Combine(directory,"key-counts.json"),"{\"65\":10,\"32\":5}");
            using(var keys=new KeyStatsService(directory)){if(keys.Data.Total!=15)throw new Exception("Legacy total lost");keys.Save();}
            string saved=File.ReadAllText(Path.Combine(directory,"key-counts.json"));if(saved.Contains("65")||!saved.Contains("Total"))throw new Exception("Per-key data remains");
        });
        Check("presets replace real windows and editor view models while retaining memos",()=>
        {
            var config=new AppSettings();using var own=new AppViewModel(config,new SettingsStore(Path.Combine(output,"presets-live")),vm.Metrics,theme);
            var memo=own.Add(WidgetKind.Memo);memo.Model.MemoText="A";memo.Model.Left=120;memo.Model.Top=100;memo.Model.Width=400;
            var group=own.CreateGroup("work");memo.Model.GroupId=group.Id;
            var main=new MainWindow(own);main.Show();using var coordinator=new WindowCoordinator(own,main);Pump(30);
            try
            {
                var a=own.SavePreset("A");var b=own.SavePreset("B");memo.Model.MemoText="B";b.ShareMemoContents=true;
                own.ApplyPreset(a);Pump(30);if(own.Widgets.Single().Model.MemoText!="A")throw new Exception("Independent memo failed");
                main.OpenWidgetEditor(own.Widgets.Single());Pump(20);var card=((Panel)main.FindName("WidgetsHost")).Children.OfType<ExpandableCard>().Single();
                if(!ReferenceEquals(card.DataContext,own.Widgets.Single()))throw new Exception("Card still targets previous view model");
                if(config.ActivePresetId!=a.Id || own.Groups.Single().Name!="work")throw new Exception("Preset state not restored");
                own.ApplyPreset(b);Pump(30);coordinator.CaptureAll();
                if(own.Widgets.Count!=1 || own.Widgets.Single().Model.Width<399)throw new Exception("Native windows not restored");
                ((Button)main.FindName("PresetsNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(20);SaveV10Image((FrameworkElement)main.Content,Path.Combine(output,"V13-Presets.png"));
            }
            finally{main.Exiting=true;main.Close();}
        });
        Check("manage category filters and group creation modal",()=>
        {
            using var own=new AppViewModel(new(),new SettingsStore(Path.Combine(output,"manage")),vm.Metrics,theme);var cpu=own.Add(WidgetKind.Cpu);own.Add(WidgetKind.Memo);
            var main=new MainWindow(own);main.Show();Pump(20);
            try
            {
                ((Button)main.FindName("ManageNav")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));var filter=(ComboBox)main.FindName("ManageFilter");filter.SelectedIndex=2;
                var chips=(Panel)main.FindName("ManageTagsHost");chips.Children.OfType<Button>().Single(b=>Equals(b.Content,"시스템")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                if(((Panel)main.FindName("WidgetsHost")).Children.OfType<ExpandableCard>().Count(c=>c.Visibility==Visibility.Visible)!=1)throw new Exception("Category filter failed");
                bool modal=false;
                main.Dispatcher.BeginInvoke(()=>
                {
                    var dialog=Application.Current.Windows.OfType<Window>().Single(w=>w.Owner==main && w.Title=="그룹 추가");modal=true;
                    var input=AllChildren(dialog).OfType<TextBox>().Single();input.Text="작업";input.ApplyTemplate();
                    if(!AllChildren(input).OfType<Border>().Any(b=>b.CornerRadius.TopLeft==10))throw new Exception("Modal input lacks rounded corners");
                    AllChildren(dialog).OfType<Button>().Single(b=>Equals(b.Content,"저장")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                });
                ((Button)main.FindName("CreateGroupButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(20);
                if(!modal || own.Groups.Single().Name!="작업" || filter.SelectedIndex!=1)throw new Exception("Group modal did not create/open group");
                cpu.Model.GroupId=own.Groups[0].Id;own.Changed();Pump(20);SaveV10Image((FrameworkElement)main.Content,Path.Combine(output,"V13-Manage.png"));
            }
            finally{main.Exiting=true;main.Close();}
        });
        Check("diagonal chrome hit targets preserve caption buttons",()=>
        {
            foreach(var pair in new[]{(new Point(3,20),13),(new Point(20,3),13),(new Point(1097,20),14),(new Point(1080,3),14),(new Point(3,760),16),(new Point(20,777),16),(new Point(1097,760),17),(new Point(1080,777),17),(new Point(1080,26),0)})
                if(MainWindow.ChromeHitTest(new(1100,780),pair.Item1)!=pair.Item2)throw new Exception("Unexpected caption/resize hit target");
            var main=new MainWindow(vm);main.Show();Pump(20);
            try
            {
                var point=main.PointToScreen(new Point(3,22));var packed=new IntPtr(((int)point.Y&0xffff)<<16|((int)point.X&0xffff));
                if(SendMessage(new WindowInteropHelper(main).Handle,0x0084,IntPtr.Zero,packed).ToInt32()!=13)throw new Exception("Native corner hook not used");
            }
            finally{main.Exiting=true;main.Close();}
        });
        Check("Dice native drag, magnetic, presets and direct size enforce aspect",()=>
        {
            var widget=new WidgetViewModel(vm,new(){Kind=WidgetKind.Dice,Width=320,Height=200});var window=new WidgetWindow(vm,widget);window.Show();Pump(30);
            try
            {
                window.ResizeAnimated(999,999);Pump(20);var bounds=V10Bounds(window);if(Math.Min(bounds.Width,bounds.Height)>281 || window.ActualWidth>420)throw new Exception("Oversize preset escaped limits");
                window.Width=500;window.Height=180;Pump(20);bounds=V10Bounds(window);if(bounds.Width/bounds.Height is < .499 or > 2.001)throw new Exception("Programmatic size escaped ratio");
                var grip=AllChildren((FrameworkElement)window.Content).OfType<Thumb>().Single(t=>Equals(t.Tag,"RB")&&t.IsVisible);
                typeof(WidgetWindow).GetProperty("GetPointerPosition",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,(Func<(double X,double Y)?>)(()=>null));
                foreach(bool magnetic in new[]{false,true})
                {settings.MagneticMode=magnetic;grip.RaiseEvent(new DragStartedEventArgs(0,0));grip.RaiseEvent(new DragDeltaEventArgs(70,30));grip.RaiseEvent(new DragCompletedEventArgs(70,30,false));Pump(20);bounds=V10Bounds(window);if(bounds.Width/bounds.Height is < .499 or > 2.001 || widget.Model.Width/widget.Model.Height is < .499 or > 2.001)throw new Exception("Resize saved/native aspect differs");}
            }
            finally{settings.MagneticMode=false;window.Close();}
        });
        passed+=good;failed+=bad;
    }
}
