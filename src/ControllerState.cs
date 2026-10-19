using System;
using System.Collections.Generic;
using System.Collections;

namespace Switch2Bridge {
    public struct ButtonSet : IEnumerable<string> {
        public uint Bits { get; internal set; }
        public int Count { get { uint b=Bits;int n=0;while(b!=0){b&=b-1;n++;}return n; } }
        public bool Contains(string name) { int i=Array.IndexOf(ControllerState.Names,name);return i>=0&&(Bits&(1u<<i))!=0; }
        public bool Add(string name) { int i=Array.IndexOf(ControllerState.Names,name);if(i<0)return false;uint bit=1u<<i;bool added=(Bits&bit)==0;Bits|=bit;return added; }
        public bool Remove(string name) { int i=Array.IndexOf(ControllerState.Names,name);if(i<0)return false;uint bit=1u<<i;bool removed=(Bits&bit)!=0;Bits&=~bit;return removed; }
        public void Clear(){Bits=0;}
        public Enumerator GetEnumerator(){return new Enumerator(Bits);}
        IEnumerator<string> IEnumerable<string>.GetEnumerator(){return GetEnumerator();}
        IEnumerator IEnumerable.GetEnumerator(){return GetEnumerator();}
        public struct Enumerator : IEnumerator<string> {
            readonly uint bits;int index;
            internal Enumerator(uint value){bits=value;index=-1;}
            public string Current {get{return ControllerState.Names[index];}}
            object IEnumerator.Current {get{return Current;}}
            public bool MoveNext(){while(++index<21)if((bits&(1u<<index))!=0)return true;return false;}
            public void Reset(){index=-1;}public void Dispose(){}
        }
    }
    // Wire layout documented by SDL (0x05) and amccomb's bridge (0x09).
    // This is an independent C# implementation. See THIRD_PARTY_NOTICES.md.
    public sealed class ControllerState {
        public static readonly string[] Names = { "A", "B", "X", "Y", "UP", "DOWN", "LEFT", "RIGHT", "L", "R", "ZL", "ZR", "MINUS", "PLUS", "L3", "R3", "HOME", "CAPTURE", "C", "GL", "GR" };
        public ButtonSet Buttons;
        public int LX = 2048, LY = 2048, RX = 2048, RY = 2048;
        public long ReceivedTicks;
        static readonly byte[] EmptyReport=new byte[0];
        public byte[] Report=EmptyReport;
        static readonly int[] Offsets05={5,5,5,5,7,7,7,7,7,5,7,5,6,6,6,6,6,6,6,8,8};
        static readonly int[] Bits05={3,2,1,0,1,0,3,2,6,6,7,7,0,1,3,2,4,5,6,1,0};
        static readonly int[] Offsets09={3,3,3,3,4,4,4,4,4,3,4,3,4,3,4,3,5,5,5,5,5};
        static readonly int[] Bits09={1,0,3,2,3,0,2,1,4,4,5,5,6,6,7,7,0,1,4,3,2};
        public static bool TryParse(byte[] report, out ControllerState state) {
            return Parse(report,true,out state);
        }
        internal static bool TryParseOwned(byte[] report,out ControllerState state){return Parse(report,false,out state);}
        static bool Parse(byte[] report,bool copy,out ControllerState state) {
            state = null;
            if (report == null || report.Length < 64 || (report[0] != 5 && report[0] != 9)) return false;
            var s = new ControllerState();
            s.Report = copy?(byte[])report.Clone():report;
            int[] offsets=report[0]==5?Offsets05:Offsets09,bits=report[0]==5?Bits05:Bits09;
            uint pressed=0;for(int i=0;i<21;i++)if((report[offsets[i]]&(1<<bits[i]))!=0)pressed|=1u<<i;
            s.Buttons.Bits=pressed;
            if (report[0] == 5) {
                Unpack(report, 11, out s.LX, out s.LY);
                Unpack(report, 14, out s.RX, out s.RY);
            } else {
                Unpack(report, 6, out s.LX, out s.LY);
                Unpack(report, 9, out s.RX, out s.RY);
            }
            state = s; return true;
        }
        static void Add(ControllerState s, byte bits, string[] names) {
            for (int i = 0; i < names.Length; i++) if (names[i] != null && (bits & (1 << i)) != 0) s.Buttons.Add(names[i]);
        }
        static void Unpack(byte[] b, int i, out int x, out int y) {
            x = b[i] | ((b[i + 1] & 15) << 8); y = (b[i + 1] >> 4) | (b[i + 2] << 4);
        }
        public static double Normalize(int raw, int center) {
            return Math.Max(-1, Math.Min(1, (raw - center) / (double)(raw < center ? Math.Max(1, center) : Math.Max(1, 4095 - center))));
        }
        public static double[] ApplyDeadzone(double x, double y, double zone) {
            double a,b;ApplyDeadzone(x,y,zone,out a,out b);return new[]{a,b};
        }
        public static void ApplyDeadzone(double x,double y,double zone,out double a,out double b) {
            zone = Math.Max(0, Math.Min(0.5, zone));
            double r = Math.Sqrt(x * x + y * y);
            if (r <= zone || r == 0) {a=b=0;return;}
            double v = Math.Min(1, (r - zone) / (1 - zone));
            a=x/r*v;b=y/r*v;
        }
    }
    public interface IControllerTransport : IDisposable {
        event Action<ControllerState> StateReceived;
        event Action<string> StatusChanged;
        event Action<string> Diagnostic;
        void Start();
        void Stop();
    }
}
