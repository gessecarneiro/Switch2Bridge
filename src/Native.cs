using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Switch2Bridge {
    internal static class Native {
        [StructLayout(LayoutKind.Sequential)] internal struct DeviceInterface { public int Size; public Guid Guid; public int Flags; public IntPtr Reserved; }
        [StructLayout(LayoutKind.Sequential)] internal struct Overlapped { public IntPtr Internal, InternalHigh; public uint Offset, OffsetHigh; public IntPtr Event; }
        [StructLayout(LayoutKind.Sequential, Pack = 1)] internal struct InterfaceDescriptor { public byte Length, Type, Number, Alternate, Endpoints, Class, SubClass, Protocol, Index; }
        [StructLayout(LayoutKind.Sequential)] internal struct Pipe { public int Type; public byte Id; public ushort MaxPacket; public byte Interval; }
        [StructLayout(LayoutKind.Sequential)] internal struct Caps {
            public ushort Usage, UsagePage, InputLength, OutputLength, FeatureLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)] public ushort[] Reserved;
            public ushort LinkNodes, InButtons, InValues, InIndices, OutButtons, OutValues, OutIndices, FeatureButtons, FeatureValues, FeatureIndices;
        }
        [DllImport("hid.dll")] internal static extern void HidD_GetHidGuid(out Guid guid);
        [DllImport("hid.dll")] internal static extern bool HidD_GetPreparsedData(SafeFileHandle h, out IntPtr data);
        [DllImport("hid.dll")] internal static extern bool HidD_FreePreparsedData(IntPtr data);
        [DllImport("hid.dll")] internal static extern int HidP_GetCaps(IntPtr data, out Caps caps);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr SetupDiGetClassDevs(ref Guid g, string e, IntPtr w, uint f);
        [DllImport("setupapi.dll", SetLastError = true)] static extern bool SetupDiEnumDeviceInterfaces(IntPtr h, IntPtr d, ref Guid g, uint i, ref DeviceInterface data);
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr h, ref DeviceInterface d, IntPtr detail, uint size, out uint needed, IntPtr info);
        [DllImport("setupapi.dll")] static extern bool SetupDiDestroyDeviceInfoList(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] internal static extern SafeFileHandle CreateFile(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool ReadFile(SafeFileHandle h, IntPtr buffer, uint size, out uint read, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetOverlappedResult(SafeFileHandle h, IntPtr ov, out uint read, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool CancelIoEx(SafeFileHandle h, IntPtr ov);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern IntPtr CreateEvent(IntPtr attr, bool manual, bool initial, string name);
        [DllImport("kernel32.dll")] internal static extern uint WaitForSingleObject(IntPtr h, uint ms);
        [DllImport("kernel32.dll")] internal static extern bool ResetEvent(IntPtr h);
        [DllImport("kernel32.dll")] internal static extern bool CloseHandle(IntPtr h);
        [DllImport("winusb.dll", SetLastError = true)] internal static extern bool WinUsb_Initialize(SafeFileHandle h, out IntPtr usb);
        [DllImport("winusb.dll")] internal static extern bool WinUsb_Free(IntPtr usb);
        [DllImport("winusb.dll", SetLastError = true)] internal static extern bool WinUsb_QueryInterfaceSettings(IntPtr usb, byte alternate, out InterfaceDescriptor d);
        [DllImport("winusb.dll", SetLastError = true)] internal static extern bool WinUsb_QueryPipe(IntPtr usb, byte alternate, byte index, out Pipe pipe);
        [DllImport("winusb.dll", SetLastError = true)] internal static extern bool WinUsb_SetPipePolicy(IntPtr usb, byte pipe, uint type, uint size, ref uint value);
        [DllImport("winusb.dll", SetLastError = true)] internal static extern bool WinUsb_WritePipe(IntPtr usb, byte pipe, byte[] b, uint size, out uint written, IntPtr ov);
        [DllImport("winusb.dll", SetLastError = true)] internal static extern bool WinUsb_ReadPipe(IntPtr usb, byte pipe, byte[] b, uint size, out uint read, IntPtr ov);
        internal static Exception Error(string action) {
            int code = Marshal.GetLastWin32Error();
            return new Win32Exception(code, action + " · Win32 " + code + ": " + new Win32Exception(code).Message);
        }
        internal static bool IsTarget(string path) { string p = path.ToLowerInvariant(); return p.Contains("vid_057e") && p.Contains("pid_2069"); }
        internal static List<string> Paths(Guid guid, bool targetOnly = true) {
            var result = new List<string>();
            IntPtr h = SetupDiGetClassDevs(ref guid, null, IntPtr.Zero, 18);
            if (h == new IntPtr(-1)) throw Error("Enumerar interfaces");
            try {
                for (uint i = 0; ; i++) {
                    var d = new DeviceInterface { Size = Marshal.SizeOf(typeof(DeviceInterface)) };
                    if (!SetupDiEnumDeviceInterfaces(h, IntPtr.Zero, ref guid, i, ref d)) {
                        if (Marshal.GetLastWin32Error() != 259) throw Error("Enumerar dispositivo");
                        break;
                    }
                    uint required;
                    SetupDiGetDeviceInterfaceDetail(h, ref d, IntPtr.Zero, 0, out required, IntPtr.Zero);
                    if (required < 6 || required > 65536) continue;
                    IntPtr buffer = Marshal.AllocHGlobal((int)required);
                    try {
                        Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                        if (SetupDiGetDeviceInterfaceDetail(h, ref d, buffer, required, out required, IntPtr.Zero)) {
                            string path = Marshal.PtrToStringUni(IntPtr.Add(buffer, 4));
                            if (!targetOnly || IsTarget(path)) result.Add(path);
                        }
                    } finally { Marshal.FreeHGlobal(buffer); }
                }
            } finally { SetupDiDestroyDeviceInfoList(h); }
            return result;
        }
    }

    // All pending native IO is cancelled and reaped BEFORE releasing its memory.
    internal sealed class HidReader : IDisposable {
        SafeFileHandle handle; IntPtr buffer, ov, signal; int length;
        internal long LastReadTimestamp {get;private set;}
        internal HidReader(string path) {
            handle = Native.CreateFile(path, 0x80000000, 3, IntPtr.Zero, 3, 0x40000000, IntPtr.Zero);
            if (handle.IsInvalid) { var error = Native.Error("Abrir interface HID"); handle.Dispose(); throw error; }
            IntPtr prep;
            try {
                if (!Native.HidD_GetPreparsedData(handle, out prep)) throw Native.Error("Ler descritor HID");
                try {
                    Native.Caps caps;
                    if (Native.HidP_GetCaps(prep, out caps) != 0x110000) throw new InvalidOperationException("Descritor HID inválido.");
                    length = caps.InputLength;
                    if (length < 64 || length > 1024) throw new InvalidOperationException("Interface HID sem relatórios compatíveis.");
                } finally { Native.HidD_FreePreparsedData(prep); }
                buffer = Marshal.AllocHGlobal(length); ov = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.Overlapped)));
                signal = Native.CreateEvent(IntPtr.Zero, true, false, null);
                if (signal == IntPtr.Zero) throw Native.Error("Criar evento HID");
                Marshal.StructureToPtr(new Native.Overlapped { Event=signal },ov,false);
            } catch { Dispose(); throw; }
        }
        internal byte[] Read() {
            Native.ResetEvent(signal);
            Marshal.WriteIntPtr(ov,IntPtr.Zero);Marshal.WriteIntPtr(ov,IntPtr.Size,IntPtr.Zero);
            uint read;
            if (!Native.ReadFile(handle, buffer, (uint)length, out read, ov)) {
                if (Marshal.GetLastWin32Error() != 997) throw Native.Error("Ler HID");
                uint wait = Native.WaitForSingleObject(signal, 250);
                if (wait != 0) {
                    Native.CancelIoEx(handle, ov);
                    Native.GetOverlappedResult(handle, ov, out read, true);
                    if (wait == 258) return null;
                    throw Native.Error("Aguardar HID");
                }
                if (!Native.GetOverlappedResult(handle, ov, out read, false)) throw Native.Error("Concluir leitura HID");
            }
            if (read == 0) return null;
            LastReadTimestamp=System.Diagnostics.Stopwatch.GetTimestamp();
            byte[] bytes = new byte[read]; Marshal.Copy(buffer, bytes, 0, (int)read); return bytes;
        }
        public void Dispose() {
            if (handle != null) handle.Dispose();
            if (signal != IntPtr.Zero) Native.CloseHandle(signal);
            if (ov != IntPtr.Zero) Marshal.FreeHGlobal(ov);
            if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
            handle = null; signal = ov = buffer = IntPtr.Zero;
        }
    }
}
