using System;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;

// In-process WPF test: constructs and renders a separate visual tree.
// Does not automate or capture any window on the user's desktop.
class UiSmoke {
    static int checks;
    static void Check(bool c,string n){if(!c)throw new Exception(n);checks++;}
    [STAThread] static int Main(string[] args){
        Window window=null;object controller=null;
        try{
            var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));
            using(var stream=assembly.GetManifestResourceStream("MainWindow.xaml"))window=(Window)XamlReader.Load(stream);
            var type=assembly.GetType("Switch2Bridge.AppController",true);
            controller=Activator.CreateInstance(type,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{window},null);
            Action tick=()=>type.GetMethod("Tick",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);
            Func<string,object> find=name=>window.FindName(name);
            Action<string> click=name=>((Button)find(name)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(delegate{}));
            click("NavProfiles");Check(((StackPanel)find("ProfilesPage")).Visibility==Visibility.Visible,"profiles navigation");
            Render(window,args[1],"perfis-inicial.png");
            var grid=(System.Windows.Controls.Primitives.UniformGrid)find("MappingGrid");Check(grid.Children.Count==21,"all remapping rows");
            var last=(Border)grid.Children[19];var combo=(ComboBox)((Grid)last.Child).Children[1];
            combo.SelectedIndex=1;
            window.Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(delegate{}));
            Check(combo.SelectedValue!=null&&combo.SelectedValue.ToString()=="A","combo selected value");
            ((TextBox)find("ProfileName")).Text="Teste UI";click("SaveProfile");Check(((TextBlock)find("ProfileSaveStatus")).Text.StartsWith("Salvo"),"UI saves remapping and name");
            string[] files=Directory.GetFiles(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"profiles"),"*.xml");Check(Array.Exists(files,f=>File.ReadAllText(f).Contains("Source=\"GL\" Target=\"A\"")),"GL binding persisted from combo");
            click("CopyProfile");Check(((ComboBox)find("ProfilePicker")).Items.Count>=2,"copy creates profile");
            click("PresetNintendo");click("SaveProfile");
            ((Slider)find("Zone")).Value=12;click("SaveProfile");Check(((TextBlock)find("ProfileSaveStatus")).Text.StartsWith("Salvo"),"zone saved");
            ((CheckBox)find("InvertLY")).IsChecked=true;click("SaveProfile");
            tick();Render(window,args[1],"perfis.png");
            click("NavOutput");tick();Check(((StackPanel)find("OutputPage")).Visibility==Visibility.Visible,"output navigation");
            click("ToggleOutput");Thread.Sleep(250);tick();
            Check(((TextBlock)find("OutputStatus")).Text!="Saída Xbox parada","driver action feedback surfaced in UI");
            Check(((TextBlock)find("PerformanceMetrics")).Text.Length>0,"latency metrics present");
            Render(window,args[1],"saida-xbox.png");
            click("NavTest");tick();Render(window,args[1],"teste-estatico.png");
            var pad=type.GetField("pad",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(controller);var counter=pad.GetType().GetField("RenderCount",BindingFlags.Instance|BindingFlags.NonPublic);long renders=(long)counter.GetValue(pad);
            tick();tick();Render(window,args[1],"teste-estatico.png");Check((long)counter.GetValue(pad)==renders,"unchanged pad not redrawn");
            window.WindowState=WindowState.Minimized;tick();Check((long)counter.GetValue(pad)==renders,"minimized UI skips drawing");window.WindowState=WindowState.Normal;
            click("Demo");tick();Check(((TextBlock)find("BannerText")).Text.StartsWith("DEMONSTRAÇÃO"),"demo banner");
            click("NavOutput");click("ToggleOutput");tick();Check(((TextBlock)find("OutputStatus")).Text.Contains("Saia da demonstração"),"demo cannot start virtual output");
            click("Demo");
            Console.WriteLine("PASS: "+checks+" in-process WPF UI checks, profiles and output pages rendered.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}
        finally{if(window!=null)window.Close();}
    }
    static void Render(Window window,string directory,string filename){
        Directory.CreateDirectory(directory);var root=(FrameworkElement)window.Content;
        root.Measure(new Size(1264,860));root.Arrange(new Rect(0,0,1264,860));root.UpdateLayout();
        var bitmap=new RenderTargetBitmap(1264,860,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
        var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(Path.Combine(directory,filename)))encoder.Save(stream);
    }
}
