using System;
using System.Runtime.InteropServices;

namespace Switch2Bridge {
    [StructLayout(LayoutKind.Sequential)]
    public struct XboxReport : IEquatable<XboxReport> {
        public ushort Buttons;
        public byte LT,RT;
        public short LX,LY,RX,RY;
        public bool Equals(XboxReport r) { return Buttons==r.Buttons&&LT==r.LT&&RT==r.RT&&LX==r.LX&&LY==r.LY&&RX==r.RX&&RY==r.RY; }
        public override bool Equals(object obj) { return obj is XboxReport&&Equals((XboxReport)obj); }
        public override int GetHashCode() { return Buttons^LX^LY^RX^RY^LT^RT; }
        public override string ToString() { return "Botões 0x"+Buttons.ToString("X4")+"  LT "+LT+"  RT "+RT+Environment.NewLine+"L "+LX+", "+LY+"    R "+RX+", "+RY; }
    }
    public static class Mapper {
        public static ushort Mask(XboxAction a) {
            switch(a) {
                case XboxAction.Up:return 1;case XboxAction.Down:return 2;case XboxAction.Left:return 4;case XboxAction.Right:return 8;
                case XboxAction.Start:return 16;case XboxAction.Back:return 32;case XboxAction.L3:return 64;case XboxAction.R3:return 128;
                case XboxAction.LB:return 256;case XboxAction.RB:return 512;case XboxAction.Guide:return 1024;
                case XboxAction.A:return 4096;case XboxAction.B:return 8192;case XboxAction.X:return 16384;case XboxAction.Y:return 32768;default:return 0;
            }
        }
        internal static short Axis(double value) { return (short)Math.Round(Math.Max(-1,Math.Min(1,value))*(value<0?32768:32767)); }
        public static XboxReport Map(ControllerState s,Profile p) {
            var report=new XboxReport();if(s==null)return report;
            foreach(var b in p.Bindings)if(s.Buttons.Contains(b.Source)) {
                if(b.Target==XboxAction.LT)report.LT=255;else if(b.Target==XboxAction.RT)report.RT=255;else report.Buttons|=Mask(b.Target);
            }
            // Opposing directions cancel. Multiple sources targeting one button are ORed.
            if((report.Buttons&3)==3)report.Buttons&=unchecked((ushort)~3);
            if((report.Buttons&12)==12)report.Buttons&=unchecked((ushort)~12);
            var l=ControllerState.ApplyDeadzone(ControllerState.Normalize(s.LX,p.CenterLX),ControllerState.Normalize(s.LY,p.CenterLY),p.Deadzone);
            var r=ControllerState.ApplyDeadzone(ControllerState.Normalize(s.RX,p.CenterRX),ControllerState.Normalize(s.RY,p.CenterRY),p.Deadzone);
            double lx=l[0]*(p.InvertLX?-1:1),ly=l[1]*(p.InvertLY?-1:1),rx=r[0]*(p.InvertRX?-1:1),ry=r[1]*(p.InvertRY?-1:1);
            report.LX=Axis(p.SwapSticks?rx:lx);report.LY=Axis(p.SwapSticks?ry:ly);report.RX=Axis(p.SwapSticks?lx:rx);report.RY=Axis(p.SwapSticks?ly:ry);
            return report;
        }
    }
    // Immutable runtime snapshot. Editing/saving the profile never touches this instance.
    public sealed class MappingPlan {
        readonly ushort[] masks=new ushort[21];readonly uint lt,rt;
        readonly int lx,ly,rx,ry;readonly double zone,sx,sy,tx,ty;readonly bool swap;
        public MappingPlan(Profile p){
            lx=p.CenterLX;ly=p.CenterLY;rx=p.CenterRX;ry=p.CenterRY;zone=p.Deadzone;
            sx=p.InvertLX?-1:1;sy=p.InvertLY?-1:1;tx=p.InvertRX?-1:1;ty=p.InvertRY?-1:1;swap=p.SwapSticks;
            uint left=0,right=0;
            foreach(var b in p.Bindings){int i=Array.IndexOf(ControllerState.Names,b.Source);if(i<0)continue;masks[i]=Mapper.Mask(b.Target);if(b.Target==XboxAction.LT)left|=1u<<i;if(b.Target==XboxAction.RT)right|=1u<<i;}
            lt=left;rt=right;
        }
        public XboxReport Map(ControllerState s){
            var r=new XboxReport();if(s==null)return r;uint bits=s.Buttons.Bits;
            for(int i=0;i<21;i++)if((bits&(1u<<i))!=0)r.Buttons|=masks[i];
            if((r.Buttons&3)==3)r.Buttons&=unchecked((ushort)~3);if((r.Buttons&12)==12)r.Buttons&=unchecked((ushort)~12);
            if((bits&lt)!=0)r.LT=255;if((bits&rt)!=0)r.RT=255;
            double a,b,c,d;
            ControllerState.ApplyDeadzone(ControllerState.Normalize(s.LX,lx),ControllerState.Normalize(s.LY,ly),zone,out a,out b);
            ControllerState.ApplyDeadzone(ControllerState.Normalize(s.RX,rx),ControllerState.Normalize(s.RY,ry),zone,out c,out d);
            a*=sx;b*=sy;c*=tx;d*=ty;
            r.LX=Mapper.Axis(swap?c:a);r.LY=Mapper.Axis(swap?d:b);r.RX=Mapper.Axis(swap?a:c);r.RY=Mapper.Axis(swap?b:d);return r;
        }
    }
}
