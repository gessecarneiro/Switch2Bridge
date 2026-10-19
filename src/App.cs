using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;

namespace Switch2Bridge {
    internal sealed partial class AppController {
        readonly Window window;
        readonly UsbTransport usb = new UsbTransport();
        readonly object gate = new object();
        ControllerState latest = new ControllerState();
        readonly HashSet<string> tested = new HashSet<string>();uint previousBits,testedBits,renderedTestedBits;
        volatile MappingPlan mappingPlan;
        readonly object logFileGate=new object();
        readonly Dictionary<string,object> controls=new Dictionary<string,object>();
        readonly Queue<string> lines = new Queue<string>();
        readonly Dictionary<string, Border> keys = new Dictionary<string, Border>();
        readonly Dictionary<string, TextBlock> labels = new Dictionary<string, TextBlock>();
        readonly PadView pad = new PadView();
        readonly StickView left = new StickView(), right = new StickView();
        readonly DispatcherTimer timer = new DispatcherTimer();
        readonly Stopwatch clock = Stopwatch.StartNew();
        volatile bool demo;bool closed,forceVisual=true;long packets,previousPackets;double lastArrival=-1,rateAt,demoAt,metricsAt;
        LatencySnapshot performanceSnapshot=new LatencySnapshot();
        string performanceText="Aguardando alterações de saída para medir…",displayedConnection;
        int rawLX=int.MinValue,rawLY=int.MinValue,rawRX=int.MinValue,rawRY=int.MinValue;bool rawFresh;
        int[] centers = {2048,2048,2048,2048}; string connection="Aguardando USB", lastEvent="Aguardando a primeira entrada…", lastProblem="";
        T Find<T>(string name) where T : class {object value;if(!controls.TryGetValue(name,out value)){value=window.FindName(name);controls[name]=value;}return (T)value;}
        void Button(string n, Action a) { Find<Button>(n).Click += delegate { a(); }; }
        internal AppController(Window w) {
            window=w;
            InitializeProfiles();
            Find<Grid>("ControllerHost").Children.Add(pad); Find<Grid>("LeftHost").Children.Add(left); Find<Grid>("RightHost").Children.Add(right);
            foreach(string name in ControllerState.Names) {
                string label=Label(name);
                var text=new TextBlock { Text=label, HorizontalAlignment=HorizontalAlignment.Center, VerticalAlignment=VerticalAlignment.Center, FontSize=11, Foreground=Paint.Ink };
                var b=new Border { Child=text, Background=Paint.Dark, BorderBrush=Paint.Brush("#414957"), BorderThickness=new Thickness(1), CornerRadius=new CornerRadius(5), Margin=new Thickness(3), Height=31, ToolTip=name };
                Find<System.Windows.Controls.Primitives.UniformGrid>("ButtonGrid").Children.Add(b); keys[name]=b; labels[name]=text;
            }
            Button("NavTest",delegate { Page("TestPage","NavTest"); }); Button("NavDiag",delegate { Page("DiagPage","NavDiag"); }); Button("NavRoadmap",delegate { Page("RoadmapPage","NavRoadmap"); });
            Button("NavProfiles",delegate { Page("ProfilesPage","NavProfiles"); });Button("NavOutput",delegate { Page("OutputPage","NavOutput"); });
            window.PreviewKeyDown+=delegate(object sender,System.Windows.Input.KeyEventArgs e) {
                if(System.Windows.Input.Keyboard.Modifiers!=System.Windows.Input.ModifierKeys.Control)return;
                string nav=e.Key==System.Windows.Input.Key.D1?"NavTest":e.Key==System.Windows.Input.Key.D2?"NavProfiles":e.Key==System.Windows.Input.Key.D3?"NavOutput":e.Key==System.Windows.Input.Key.D4?"NavDiag":e.Key==System.Windows.Input.Key.D5?"NavRoadmap":e.Key==System.Windows.Input.Key.Enter?"ToggleOutput":null;
                if(nav!=null){Find<Button>(nav).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));e.Handled=true;}
            };
            Button("Demo",ToggleDemo); Button("Reset",Reset);
            Button("Center",delegate {
                lock(gate) {
                    if(lastArrival<0 || clock.Elapsed.TotalSeconds-lastArrival>1) { lastEvent="Conecte o controle ou ative a demonstração antes de centralizar."; return; }
                    int[] c={latest.LX,latest.LY,latest.RX,latest.RY};
                    if(c.Any(v=>v<1200 || v>2900)) { lastEvent="Solte os analógicos e tente centralizar novamente."; return; }
                    if(demo) { lastEvent="Saia da demonstração para calibrar seu controle físico.";return; }
                    centers=c;activeProfile.CenterLX=c[0];activeProfile.CenterLY=c[1];activeProfile.CenterRX=c[2];activeProfile.CenterRY=c[3];lastEvent="Centro ajustado no perfil ativo."; LogLocked(lastEvent);
                }
                MarkProfileDirty();
            });
            Button("Reconnect",delegate { if(demo) ToggleDemo(); else { usb.Stop(); ClearState(); usb.Start(); Log("Nova tentativa de conexão USB."); } });
            Button("Export",Export);
            Button("SetupUsb",delegate {
                try {
                    Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--setup-usb") { UseShellExecute=true,Verb="runas" });
                    Log("Configuração USB solicitada. Após autorizar o Windows, reconecte o cabo USB.");
                }catch(System.ComponentModel.Win32Exception e) { Log(e.NativeErrorCode==1223?"Configuração cancelada pelo usuário.":e.Message); }
            });
            usb.StateReceived += Receive;
            usb.StatusChanged += delegate(string s) { lock(gate) { connection=s; if(s!="USB conectado") { latest=new ControllerState();previousBits=0;lastArrival=-1;output.Neutral(); }else lastProblem=""; } };
            usb.Diagnostic += Log;
            window.Closed += delegate { closed=true; timer.Stop();usb.Dispose();output.Dispose();try { SaveCurrentProfile(); }catch(Exception e){Log("Perfil não salvo: "+e.Message);} };
            timer.Interval=TimeSpan.FromMilliseconds(33); timer.Tick+=delegate { Tick(); }; timer.Start();
            window.StateChanged+=delegate{timer.Interval=TimeSpan.FromMilliseconds(window.WindowState==WindowState.Minimized?1000:33);forceVisual=true;};
            Log("Switch2Bridge 0.3.1 · saída por evento, métricas USB → driver e perfis isolados.");
            usb.Start();
        }
        static string Label(string name) {
            switch(name) { case "UP":return "↑";case "DOWN":return "↓";case "LEFT":return "←";case "RIGHT":return "→";case "MINUS":return "−";case "PLUS":return "+";case "CAPTURE":return "Captura";case "HOME":return "Home";default:return name; }
        }
        void Page(string n,string nav) {
            foreach(string p in new[]{"TestPage","ProfilesPage","OutputPage","DiagPage","RoadmapPage"}) Find<StackPanel>(p).Visibility=p==n?Visibility.Visible:Visibility.Collapsed;
            foreach(string b in new[]{"NavTest","NavProfiles","NavOutput","NavDiag","NavRoadmap"}) Find<Button>(b).Background=Paint.Brush(b==nav?"#25453C":"#282D35");
            Find<ScrollViewer>("MainScroll").ScrollToTop();
            forceVisual=true;
        }
        void Receive(ControllerState s) {
            MappingPlan plan=mappingPlan;XboxReport mapped=plan==null?new XboxReport():plan.Map(s);
            lock(gate) {
                latest=s; lastArrival=clock.Elapsed.TotalSeconds; packets++;
                if(!demo&&ReferenceEquals(plan,mappingPlan))output.Update(mapped,s.ReceivedTicks);
                uint bits=s.Buttons.Bits,changes=bits^previousBits;testedBits|=bits;
                if(changes!=0)for(int i=0;i<21;i++)if((changes&(1u<<i))!=0){lastEvent=Label(ControllerState.Names[i])+((bits&(1u<<i))!=0?" pressionado":" solto");LogLocked(lastEvent);}
                previousBits=bits;
            }
        }
        void LogLocked(string text) { lines.Enqueue(DateTime.Now.ToString("HH:mm:ss")+"  "+text); while(lines.Count>180)lines.Dequeue(); }
        void Log(string text) {
            lock(gate) {
                LogLocked(text);
                if(text.Contains("Win32") || text.StartsWith("Inicialização:") || text.StartsWith("Sem relatórios"))lastProblem=text;
            }
            lock(logFileGate)try {string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Switch2Bridge-session.log");if(File.Exists(path)&&new FileInfo(path).Length>2097152)File.WriteAllText(path,"Log anterior truncado após 2 MB."+Environment.NewLine);File.AppendAllText(path,DateTime.Now.ToString("s")+" "+text+Environment.NewLine,Encoding.UTF8);}catch(IOException){}catch(UnauthorizedAccessException){}
        }
        void ClearState() { lock(gate) { latest=new ControllerState();previousBits=0;lastArrival=-1;packets=previousPackets=0;rateAt=clock.Elapsed.TotalSeconds;output.Neutral(); }forceVisual=true; }
        void Reset() { lock(gate) {testedBits=0;lastEvent="Teste limpo. Pressione cada botão para marcar novamente.";LogLocked("Contagem de botões testados reiniciada.");}forceVisual=true; }
        void ToggleDemo() {
            output.Stop();outputStatus="Saída Xbox parada";usb.Stop(); demo=!demo; ClearState(); Reset();
            if(demo) { demoAt=clock.Elapsed.TotalSeconds; connection="Demonstração"; Log("Demonstração ativada: dados simulados, leitura USB pausada."); }
            else { connection="Aguardando USB"; usb.Start(); Log("Demonstração encerrada. Voltando ao USB real."); }
            Find<Button>("Demo").Content=demo?"Sair da demonstração":"Experimentar demonstração";
        }
        void DemoFrame() {
            double t=clock.Elapsed.TotalSeconds-demoAt;
            var s=new ControllerState { LX=(int)(2048+Math.Sin(t)*1350),LY=(int)(2048+Math.Cos(t)*1350),RX=(int)(2048+Math.Sin(t*0.65)*900),RY=(int)(2048+Math.Cos(t*0.8)*900) };
            int n=(int)(t*1.5)%ControllerState.Names.Length;
            if(t*1.5%1<0.65)s.Buttons.Add(ControllerState.Names[n]);
            Receive(s);
        }
        void Tick() {
            if(closed)return;
            if(demo)DemoFrame();
            SaveProfileIfDue();
            double now=clock.Elapsed.TotalSeconds;
            if(now-metricsAt>=1){performanceSnapshot=output.Performance;performanceText=performanceSnapshot.ToString();metricsAt=now;}
            if(window.WindowState==WindowState.Minimized)return;
            ControllerState sample;double arrival;long packetCount;uint seen;string status,problem,last;string[] logLines=null;
            bool showLogs=Find<StackPanel>("DiagPage").Visibility==Visibility.Visible;
            lock(gate){sample=latest;arrival=lastArrival;packetCount=packets;seen=testedBits;status=connection;problem=lastProblem;last=lastEvent;if(showLogs)logLines=lines.ToArray();}
            bool fresh=arrival>=0&&now-arrival<1;
            ControllerState s=fresh?sample:NeutralState;
                if(displayedConnection!=status){Find<TextBlock>("Connection").Text="●  "+status;displayedConnection=status;}
                Find<TextBlock>("Connection").Foreground=demo?Paint.Brush("#E8C47C"):fresh?Paint.Green:Paint.Muted;
                Find<TextBlock>("BannerText").Text=demo?"DEMONSTRAÇÃO · Entradas simuladas. Saia deste modo para testar seu controle físico.":fresh?"Controle recebendo dados por USB. Teste também C, GL, GR e o clique dos analógicos.":problem.Length>0?problem:"Conecte o controle com um cabo USB de dados. A detecção e a reconexão são automáticas.";
            if(Find<StackPanel>("TestPage").Visibility==Visibility.Visible){
                bool padChanged=forceVisual||pad.State.Buttons.Bits!=s.Buttons.Bits||seen!=renderedTestedBits;
                if(seen!=renderedTestedBits||forceVisual){tested.Clear();for(int i=0;i<21;i++)if((seen&(1u<<i))!=0)tested.Add(ControllerState.Names[i]);renderedTestedBits=seen;}
                if(padChanged){Find<TextBlock>("Tested").Text=tested.Count+" / 21 testados";Find<TextBlock>("PressedCount").Text=s.Buttons.Count+" ativos";}
                pad.State=s;pad.Tested=tested;
                if(padChanged)
                foreach(string b in ControllerState.Names) {
                    bool active=s.Buttons.Contains(b); keys[b].Background=active?Paint.Green:Paint.Dark; keys[b].BorderBrush=tested.Contains(b)?Paint.Green:Paint.Brush("#414957"); labels[b].Foreground=active?Paint.Dark:Paint.Ink;
                }
                double zone=Find<Slider>("Zone").Value/100;
                double lx=fresh?ControllerState.Normalize(s.LX,centers[0]):0, ly=fresh?ControllerState.Normalize(s.LY,centers[1]):0;
                double rx=fresh?ControllerState.Normalize(s.RX,centers[2]):0, ry=fresh?ControllerState.Normalize(s.RY,centers[3]):0;
                double a,bx,c,d;ControllerState.ApplyDeadzone(lx,ly,zone,out a,out bx);ControllerState.ApplyDeadzone(rx,ry,zone,out c,out d);
                bool leftChanged=forceVisual||left.X!=a||left.Y!=bx||left.Zone!=zone,rightChanged=forceVisual||right.X!=c||right.Y!=d||right.Zone!=zone;
                left.X=pad.LX=a;left.Y=pad.LY=bx;right.X=pad.RX=c;right.Y=pad.RY=d;left.Zone=right.Zone=zone;
                if(leftChanged)left.InvalidateVisual();if(rightChanged)right.InvalidateVisual();if(padChanged||leftChanged||rightChanged)pad.InvalidateVisual();
                if(leftChanged)Find<TextBlock>("LeftValue").Text=Values(a,bx);if(rightChanged)Find<TextBlock>("RightValue").Text=Values(c,d);
                if(forceVisual||rawFresh!=fresh||rawLX!=s.LX||rawLY!=s.LY)Find<TextBlock>("LeftRaw").Text=fresh?"Bruto: "+s.LX+" / "+s.LY:"Sem amostra";
                if(forceVisual||rawFresh!=fresh||rawRX!=s.RX||rawRY!=s.RY)Find<TextBlock>("RightRaw").Text=fresh?"Bruto: "+s.RX+" / "+s.RY:"Sem amostra";
                rawFresh=fresh;rawLX=s.LX;rawLY=s.LY;rawRX=s.RX;rawRY=s.RY;
                Find<TextBlock>("ZoneValue").Text=(zone*100).ToString("0")+"%";
                Find<TextBlock>("LastEvent").Text=last;
                if(now-rateAt>=1) { Find<TextBlock>("Rate").Text=((packetCount-previousPackets)/(now-rateAt)).ToString("0")+" relatórios/s · "+(demo?"dados simulados":fresh?"fluxo USB":"nenhum dado recente");previousPackets=packetCount;rateAt=now; }
                }
                if(showLogs) {
                    Find<TextBox>("Hex").Text=demo?"Demonstração: sem relatório HID físico.":sample.Report.Length==0?"Nenhum relatório recebido.":BitConverter.ToString(sample.Report).Replace("-"," ");
                    string log=string.Join(Environment.NewLine,logLines);if(Find<TextBox>("Logs").Text!=log){Find<TextBox>("Logs").Text=log;Find<TextBox>("Logs").ScrollToEnd();}
                }
                UpdateProfileUi(s,fresh);
                forceVisual=false;
        }
        static readonly ControllerState NeutralState=new ControllerState();
        static string Values(double x,double y) { return string.Format(CultureInfo.InvariantCulture,"X {0:+0.000;-0.000;+0.000}   Y {1:+0.000;-0.000;+0.000}",x,y); }
        void Export() {
            var dialog=new SaveFileDialog { Title="Salvar diagnóstico",FileName="Switch2Bridge-diagnostico-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".txt",Filter="Texto|*.txt" };
            if(dialog.ShowDialog(window)!=true)return;
            try {
                ControllerState state;string status;string[] logLines;uint seen;lock(gate){state=latest;status=connection;logLines=lines.ToArray();seen=testedBits;}
                string report="Switch2Bridge 0.3.1\r\nData: "+DateTime.Now.ToString("s")+"\r\nSistema: "+Environment.OSVersion+"\r\nModo: "+(demo?"simulado":"USB")+"\r\nEstado: "+status+"\r\nPerfil: "+activeProfile.Name+"\r\nSaída Xbox: "+outputStatus+"\r\nDispositivo esperado: 057E:2069\r\nCentro: "+string.Join(",",centers)+"\r\nZona morta: "+Find<Slider>("Zone").Value+"%\r\nTestados (máscara): 0x"+seen.ToString("X")+"\r\nRelatório: "+BitConverter.ToString(state.Report)+"\r\n\r\n"+output.Performance+"\r\n\r\n"+string.Join("\r\n",logLines);
                File.WriteAllText(dialog.FileName,report,Encoding.UTF8);Log("Diagnóstico salvo localmente.");
            } catch(Exception e) { MessageBox.Show(window,e.Message,"Não foi possível salvar",MessageBoxButton.OK,MessageBoxImage.Error); }
        }
    }
    internal static class Program {
        [STAThread] public static int Main(string[] args) {
            try {
                if(args.Contains("--setup-usb"))return UsbSetup.Configure();
                // Preserve the previous product's mutex to prevent two versions reading USB together.
                using(var single=new System.Threading.Mutex(false,"Local\\Switch2Lab.USB.Session")) {
                    if(!single.WaitOne(0,false)) { MessageBox.Show("O Switch2Bridge já está aberto. Feche a outra versão antes de iniciar esta atualização.","Switch2Bridge",MessageBoxButton.OK,MessageBoxImage.Information);return 0; }
                var app=new Application();
                using(var stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("MainWindow.xaml")) {
                    var window=(Window)XamlReader.Load(stream);
                    var controller=new AppController(window);
                    app.Run(window); GC.KeepAlive(controller);
                }
                single.ReleaseMutex();
                }
                return 0;
            } catch(Exception e) { MessageBox.Show(e.ToString(),"Switch2Bridge · erro",MessageBoxButton.OK,MessageBoxImage.Error);return 1; }
        }
    }
}
