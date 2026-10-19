using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32.SafeHandles;

namespace Switch2Bridge {
    public interface IXboxBackend : IDisposable { void Connect();void Submit(XboxReport report); }
    // Independent managed client of the documented ViGEm bus ABI. No client DLL needed.
    // ABI reference: nefarius/ViGEmClient/include/ViGEm/km/BusShared.h (MIT).
    public sealed class VigemBackend : IXboxBackend {
        internal static readonly Guid BusGuid=new Guid("96E42B22-F5E9-42F8-B043-ED0F932F014F");
        SafeFileHandle bus; uint serial;IntPtr ioBuffer,ioOverlapped,ioSignal;
        public event Action<string> Diagnostic;
        public Func<bool> CancellationRequested { get;set; }
        public int ReportRetryCount {get;private set;}
        void Trace(string message) { if(Diagnostic!=null)Diagnostic(message); }
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool DeviceIoControl(SafeFileHandle h,uint code,IntPtr input,uint inSize,IntPtr output,uint outSize,out uint returned,IntPtr ov);
        public static bool IsInstalled() { return Native.Paths(BusGuid,false).Count>0; }
        static uint Ioctl(uint index,bool readWrite=false) { return (0x2au<<16)|((readWrite?3u:2u)<<14)|(index<<2); }
        static byte[] Request(params uint[] words) { var data=new byte[words.Length*4];for(int i=0;i<words.Length;i++)Buffer.BlockCopy(BitConverter.GetBytes(words[i]),0,data,i*4,4);return data; }
        string Description(uint index,int length,bool output) {
            string command=index==0x803?"verificar versão":index==0x801?"criar controle":index==0x804?"aguardar controle pronto":index==0xA02?"enviar relatório":index==0x802?"remover controle":"comando";
            return command+" (IOCTL 0x"+Ioctl(index,output).ToString("X8")+", "+length+" bytes)";
        }
        void InitializeIo(){
            ioBuffer=Marshal.AllocHGlobal(64);ioOverlapped=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.Overlapped)));
            ioSignal=Native.CreateEvent(IntPtr.Zero,true,false,null);if(ioSignal==IntPtr.Zero)throw Native.Error("Criar evento Xbox");
            Marshal.StructureToPtr(new Native.Overlapped{Event=ioSignal},ioOverlapped,false);
        }
        Exception IoError(uint index,int length,bool output){int code=Marshal.GetLastWin32Error();return new Win32Exception(code,"ViGEmBus: "+Description(index,length,output)+" · Win32 "+code+": "+new Win32Exception(code).Message);}
        void Invoke(uint index,int length,bool output,uint timeout){
                Native.ResetEvent(ioSignal);Marshal.WriteIntPtr(ioOverlapped,IntPtr.Zero);Marshal.WriteIntPtr(ioOverlapped,IntPtr.Size,IntPtr.Zero);
                uint returned;
                if(!DeviceIoControl(bus,Ioctl(index,output),ioBuffer,(uint)length,output?ioBuffer:IntPtr.Zero,output?(uint)length:0,out returned,ioOverlapped)) {
                    if(Marshal.GetLastWin32Error()!=997)throw IoError(index,length,output);
                    uint wait=Native.WaitForSingleObject(ioSignal,timeout);
                    if(wait!=0) { Native.CancelIoEx(bus,ioOverlapped);Native.GetOverlappedResult(bus,ioOverlapped,out returned,true);throw new TimeoutException("ViGEmBus: "+Description(index,length,output)+" não respondeu no tempo esperado."); }
                    if(!Native.GetOverlappedResult(bus,ioOverlapped,out returned,false))throw IoError(index,length,output);
                }
        }
        byte[] Call(uint index,byte[] data,bool output=false,uint timeout=1500) {
            if(data.Length>64)throw new ArgumentOutOfRangeException("data");
            Marshal.Copy(data,0,ioBuffer,data.Length);Invoke(index,data.Length,output,timeout);
            if(output)Marshal.Copy(ioBuffer,data,0,data.Length);return data;
        }
        public void Connect() {
            if(bus!=null)throw new InvalidOperationException("Saída Xbox já iniciada.");
            var paths=Native.Paths(BusGuid,false);
            if(paths.Count==0)throw new InvalidOperationException("Driver ViGEmBus ausente. Instale a versão oficial pela página indicada e tente novamente.");
            bus=Native.CreateFile(paths[0],0xC0000000,3,IntPtr.Zero,3,0xC0000080,IntPtr.Zero);
            if(bus.IsInvalid) { var e=Native.Error("Abrir ViGEmBus");bus.Dispose();bus=null;throw e; }
            try {
                InitializeIo();
                Trace("Verificando versão do protocolo.");Call(0x803,Request(8,1));Trace("Versão do protocolo aceita.");
                for(uint candidate=1;candidate<=64;candidate++) {
                    try { Call(0x801,Request(16,candidate,0,0x028E045E));serial=candidate;Trace("Controle virtual criado, serial "+serial+".");break; }
                    catch(Win32Exception e) { if(e.NativeErrorCode!=87)throw; }
                }
                if(serial==0)throw new InvalidOperationException("Não há posição livre para outro controle virtual.");
                WaitForReady(delegate{Call(0x804,Request(8,serial),false,2500);},CancellationRequested,Thread.Sleep,Trace);Trace("Controle virtual pronto.");
                // ViGEm's boot blob contains non-zero axes, but its report cache
                // starts at zero. An initial zero report is therefore ignored.
                // Prime a single least-significant axis unit, then clear it before
                // publishing Active. No buttons/triggers are pressed.
                Submit(new XboxReport{LX=1});Submit(new XboxReport());Trace("Relatório neutro aceito.");
            }catch { Dispose();throw; }
        }
        // The driver itself completes WAIT_DEVICE_READY with Win32 483 after ONE
        // second, regardless of our IO timeout. First-time XUSB driver binding can
        // take longer. Keep the SAME target alive while retrying that wait only.
        public static void WaitForReady(Action wait,Func<bool> cancelled,Action<int> delay,Action<string> trace) {
            for(int attempt=1;attempt<=10;attempt++) {
                if(cancelled!=null&&cancelled())throw new OperationCanceledException("Inicialização Xbox cancelada.");
                try {wait();return;}
                catch(Win32Exception e) {
                    if(e.NativeErrorCode!=483)throw;
                    if(attempt==10)throw new InvalidOperationException("O Windows não concluiu a inicialização do Xbox virtual após 10 tentativas. Se acabou de instalar o ViGEmBus, confira se o instalador solicitou reiniciar o PC. Detalhe: "+e.Message,e);
                    if(trace!=null)trace("Windows ainda inicializando o Xbox virtual; nova espera "+(attempt+1)+"/10.");
                    delay(100);
                }
            }
        }
        public void Submit(XboxReport report) {
            if(bus==null||serial==0)throw new InvalidOperationException("Saída Xbox não está conectada.");
            for(int attempt=0;;attempt++)try{SubmitRaw(report);return;}catch(Win32Exception e){
                // XUSB may not yet have queued its next USB IN request. Keep this
                // transition rather than failing or replacing it with a later one.
                if(e.NativeErrorCode!=259||attempt>=15)throw;
                if(CancellationRequested!=null&&CancellationRequested())throw new OperationCanceledException("Envio Xbox cancelado.");
                ReportRetryCount++;Thread.Sleep(1);
            }
        }
        void SubmitRaw(XboxReport report){
            Marshal.WriteInt32(ioBuffer,20);Marshal.WriteInt32(ioBuffer,4,(int)serial);
            Marshal.WriteInt16(ioBuffer,8,unchecked((short)report.Buttons));Marshal.WriteByte(ioBuffer,10,report.LT);Marshal.WriteByte(ioBuffer,11,report.RT);
            Marshal.WriteInt16(ioBuffer,12,report.LX);Marshal.WriteInt16(ioBuffer,14,report.LY);Marshal.WriteInt16(ioBuffer,16,report.RX);Marshal.WriteInt16(ioBuffer,18,report.RY);
            Invoke(0xA02,20,false,250);
        }
        public void Dispose() {
            if(bus==null)return;
            if(!bus.IsInvalid&&serial!=0) {
                try { Submit(new XboxReport()); }catch{}
                try { Call(0x802,Request(8,serial)); }catch{}
            }
            serial=0;bus.Dispose();bus=null;
            if(ioSignal!=IntPtr.Zero)Native.CloseHandle(ioSignal);if(ioOverlapped!=IntPtr.Zero)Marshal.FreeHGlobal(ioOverlapped);if(ioBuffer!=IntPtr.Zero)Marshal.FreeHGlobal(ioBuffer);
            ioSignal=ioOverlapped=ioBuffer=IntPtr.Zero;
        }
    }
    public sealed class XboxOutput : IDisposable {
        readonly Func<IXboxBackend> factory; readonly object gate=new object();
        readonly ManualResetEvent stop=new ManualResetEvent(false);readonly AutoResetEvent changed=new AutoResetEvent(false);
        struct Pending {public XboxReport Report;public long Received;}
        readonly Pending[] queue=new Pending[256];int head,count;
        Thread worker;XboxReport latest;long lastInput,overflows,expired;bool haveInput;volatile bool active;
        readonly LatencyMetrics metrics=new LatencyMetrics();
        volatile VigemBackend nativeBackend;
        public event Action<string> StatusChanged;
        public event Action<string> Diagnostic;
        public bool Active { get { return active; } }
        public bool Running { get { return worker!=null&&worker.IsAlive; } }
        public XboxOutput(Func<IXboxBackend> backendFactory) { factory=backendFactory; }
        void Status(string text) { if(StatusChanged!=null)StatusChanged(text); }
        public LatencySnapshot Performance {get{long o,e;lock(gate){o=overflows;e=expired;}var result=metrics.Snapshot(o,e);var native=nativeBackend;result.DriverRetries=native==null?0:native.ReportRetryCount;return result;}}
        public void Update(XboxReport report){Update(report,Stopwatch.GetTimestamp());}
        public void Update(XboxReport report,long received) {
            bool wake=false;
            lock(gate) {
                lastInput=received>0?received:Stopwatch.GetTimestamp();
                if(!haveInput||!report.Equals(latest)) {
                    var item=new Pending{Report=report,Received=lastInput};
                    // Coalesce analogue movement only. Preserve every button/trigger edge.
                    int tail=(head+count-1+queue.Length)%queue.Length;
                    if(count>0&&queue[tail].Received>0&&queue[tail].Report.Buttons==report.Buttons&&queue[tail].Report.LT==report.LT&&queue[tail].Report.RT==report.RT)queue[tail]=item;
                    else {
                        if(count==queue.Length){head=count=0;queue[0]=new Pending();count=1;overflows++;}
                        queue[(head+count)%queue.Length]=item;count++;
                    }
                    latest=report;haveInput=true;wake=true;
                }
            }
            if(wake)changed.Set();
        }
        public void Neutral() {lock(gate){latest=new XboxReport();lastInput=0;haveInput=false;head=0;count=1;queue[0]=new Pending();}changed.Set();}
        public void Start() {
            if(Running)return;Neutral();metrics.Reset();lock(gate){overflows=expired=0;}stop.Reset();
            worker=new Thread(Run) { IsBackground=true,Name="Switch2 Xbox output" };worker.Start();
        }
        void Run() {
            IXboxBackend backend=null;
            try {
                Status("Conectando controle virtual…");backend=factory();
                var native=backend as VigemBackend;
                nativeBackend=native;
                if(native!=null) {
                    native.CancellationRequested=delegate{return stop.WaitOne(0);};
                    native.Diagnostic+=delegate(string message){if(Diagnostic!=null)Diagnostic(message);if(!active)Status(message);};
                }
                backend.Connect();active=true;Status("Xbox 360 virtual ativo");
                XboxReport previous=new XboxReport();long sent=0;
                while(!stop.WaitOne(0)) {
                    Pending item=new Pending();bool send=false;int timeout=100;long now=Stopwatch.GetTimestamp();
                    lock(gate) {
                        bool stale=!haveInput||lastInput==0||(now-lastInput)/(double)Stopwatch.Frequency>=0.25;
                        if(stale){head=count=0;item.Report=new XboxReport();send=sent==0||!previous.Equals(item.Report)||(now-sent)/(double)Stopwatch.Frequency>=.1;}
                        else if(count>0) {
                            item=queue[head];head=(head+1)%queue.Length;count--;send=true;
                            if(item.Received>0&&(now-item.Received)/(double)Stopwatch.Frequency>=.25){head=count=0;expired++;item=new Pending{Report=latest,Received=lastInput};}
                        }else if(sent==0||(now-sent)/(double)Stopwatch.Frequency>=.1){item.Report=latest;send=true;}
                        if(!stale)timeout=Math.Min(timeout,Math.Max(1,(int)Math.Ceiling(250-(now-lastInput)*1000.0/Stopwatch.Frequency)));
                    }
                    if(send){backend.Submit(item.Report);sent=Stopwatch.GetTimestamp();previous=item.Report;if(item.Received>0)metrics.Record(item.Received,sent);continue;}
                    changed.WaitOne(timeout);
                }
                Status("Saída Xbox parada");
            }catch(Exception e) { Status(e.Message); }
            finally { active=false;if(backend!=null){try{backend.Submit(new XboxReport());}catch{}backend.Dispose();} }
        }
        public void Stop() { stop.Set();changed.Set();if(worker!=null){worker.Join();worker=null;}Neutral(); }
        public void Dispose() { Stop();changed.Dispose();stop.Dispose(); }
    }
}
