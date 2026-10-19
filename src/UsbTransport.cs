using System;
using System.Threading;

namespace Switch2Bridge {
    public sealed class UsbTransport : IControllerTransport {
        public event Action<ControllerState> StateReceived;
        public event Action<string> StatusChanged;
        public event Action<string> Diagnostic;
        readonly ManualResetEvent stop = new ManualResetEvent(false);
        Thread worker;
        void Status(string s) { if (StatusChanged != null) StatusChanged(s); }
        void Log(string s) { if (Diagnostic != null) Diagnostic(s); }
        public void Start() {
            if (worker != null && worker.IsAlive) return;
            stop.Reset(); worker = new Thread(Run) { IsBackground = true, Name = "Switch2 USB" }; worker.Start();
        }
        public void Stop() { stop.Set(); if (worker != null) { worker.Join(); worker = null; } }
        public void Dispose() { Stop(); stop.Dispose(); }
        void Run() {
            string lastError = null;
            while (!stop.WaitOne(0)) {
                try {
                    Guid guid; Native.HidD_GetHidGuid(out guid);
                    var paths = Native.Paths(guid);
                    if (paths.Count == 0) { Status("Aguardando USB"); stop.WaitOne(1500); continue; }
                    Status("Inicializando USB");
                    // Open the input endpoint first so the initial packets are not lost.
                    HidReader reader = null;
                    Exception openError = null;
                    foreach (string path in paths) {
                        try { reader = new HidReader(path); break; } catch (Exception e) { openError = e; }
                    }
                    if (reader == null) throw openError ?? new InvalidOperationException("Interface HID indisponível.");
                    using (reader) {
                        try { Wake(); } catch (Exception e) { Log("Inicialização: " + e.Message + " Tentando ler um fluxo já ativo."); }
                        int idle = 0; bool active = false; bool unknownLogged = false;
                        while (!stop.WaitOne(0)) {
                            byte[] bytes = reader.Read();
                            ControllerState state;
                            if (bytes == null || !ControllerState.TryParseOwned(bytes, out state)) {
                                if (bytes != null && !unknownLogged) { Log("Relatório não reconhecido: " + BitConverter.ToString(bytes)); unknownLogged = true; }
                                if (++idle >= 8) throw new InvalidOperationException("Sem relatórios compatíveis; reconectando. Confira cabo de dados e feche outros leitores do controle.");
                                if (bytes != null) stop.WaitOne(20);
                                continue;
                            }
                            state.ReceivedTicks=reader.LastReadTimestamp;
                            idle = 0;
                            if (!active) { active = true; lastError = null; Status("USB conectado"); Log("Fluxo HID ativo · relatório 0x" + bytes[0].ToString("X2") + " · " + bytes.Length + " bytes."); }
                            if (StateReceived != null) StateReceived(state);
                        }
                    }
                } catch (Exception e) {
                    Status("Conexão interrompida");
                    if (lastError != e.Message) { Log(e.Message); lastError = e.Message; }
                    stop.WaitOne(1500);
                }
            }
            Status("USB pausado");
        }
        void Wake() {
            var paths = Native.Paths(new Guid(UsbSetup.InterfaceGuid));
            // Interface GUIDs are installation-specific. Keep pre-existing registrations.
            if (paths.Count == 0) {
                using(var root=Microsoft.Win32.Registry.LocalMachine.OpenSubKey(UsbSetup.DeviceKey)) {
                    if(root!=null)foreach(string instance in root.GetSubKeyNames()) using(var key=root.OpenSubKey(instance+@"\Device Parameters")) {
                        if(key==null)continue;
                        var guids=new System.Collections.Generic.List<string>((key.GetValue("DeviceInterfaceGUIDs") as string[])??new string[0]);
                        string single=key.GetValue("DeviceInterfaceGUID") as string;if(single!=null)guids.Add(single);
                        foreach(string value in guids) { Guid guid; if(Guid.TryParse(value,out guid)) foreach(string path in Native.Paths(guid)) if(!paths.Contains(path))paths.Add(path); }
                    }
                }
            }
            if (paths.Count == 0) throw new InvalidOperationException(UsbSetup.RegistrationMissing()?"O driver WinUSB está instalado, mas sua interface não foi registrada. Use Configurar USB e reconecte o cabo.":"Interface WinUSB não encontrada. Se acabou de configurar, desconecte e reconecte o cabo USB.");
            using (var h = Native.CreateFile(paths[0], 0xC0000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero)) {
                if (h.IsInvalid) throw Native.Error("Abrir WinUSB");
                IntPtr usb;
                if (!Native.WinUsb_Initialize(h, out usb)) throw Native.Error("Inicializar WinUSB");
                try {
                    Native.InterfaceDescriptor descriptor;
                    if (!Native.WinUsb_QueryInterfaceSettings(usb, 0, out descriptor)) throw Native.Error("Consultar interface USB");
                    byte output = 0, input = 0;
                    for (byte i = 0; i < descriptor.Endpoints; i++) {
                        Native.Pipe pipe;
                        if (!Native.WinUsb_QueryPipe(usb, 0, i, out pipe)) throw Native.Error("Consultar endpoint USB");
                        if (pipe.Type == 2) { if ((pipe.Id & 0x80) != 0) input = pipe.Id; else output = pipe.Id; }
                    }
                    if (input == 0 || output == 0) throw new InvalidOperationException("Endpoints bulk não encontrados.");
                    uint timeout = 200;
                    if (!Native.WinUsb_SetPipePolicy(usb, output, 3, 4, ref timeout) || !Native.WinUsb_SetPipePolicy(usb, input, 3, 4, ref timeout)) throw Native.Error("Configurar timeout USB");
                    // Volatile initialization only. No flash/firmware writes, no rumble.
                    byte[][] commands = {
                        new byte[] { 3,0x91,0,13,0,8,0,0,1,0,255,255,255,255,255,255 },
                        new byte[] { 7,0x91,0,1,0,0,0,0 },
                        new byte[] { 0x16,0x91,0,1,0,0,0,0 },
                        new byte[] { 3,0x91,0,10,0,4,0,0,9,0,0,0 },
                        new byte[] { 9,0x91,0,7,0,8,0,0,1,0,0,0,0,0,0,0 }
                    };
                    foreach (byte[] cmd in commands) {
                        if (stop.WaitOne(0)) return;
                        uint count;
                        if (!Native.WinUsb_WritePipe(usb, output, cmd, (uint)cmd.Length, out count, IntPtr.Zero)) throw Native.Error("Enviar inicialização USB");
                        if (count != cmd.Length) throw new InvalidOperationException("Comando USB incompleto.");
                        byte[] reply = new byte[64];
                        if (!Native.WinUsb_ReadPipe(usb, input, reply, 64, out count, IntPtr.Zero)) {
                            int error = System.Runtime.InteropServices.Marshal.GetLastWin32Error();
                            if (error != 121) throw Native.Error("Receber resposta USB");
                        }
                        stop.WaitOne(20);
                    }
                    Log("Sequência USB enviada. Aguardando dados do controle.");
                } finally { Native.WinUsb_Free(usb); }
            }
        }
    }
}
