using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Switch2Bridge {
    internal static class Paint {
        static readonly Dictionary<string,Brush> colors=new Dictionary<string,Brush>();
        internal static Brush Brush(string hex) {Brush brush;if(colors.TryGetValue(hex,out brush))return brush;var b=(SolidColorBrush)new BrushConverter().ConvertFromString(hex);b.Freeze();colors[hex]=b;return b;}
        internal static readonly Brush Green = Brush("#65E4B5"), Muted = Brush("#929BA9"), Ink = Brush("#EDF1F5"), Dark = Brush("#242A33");
        internal static void Text(DrawingContext dc, string text, double x, double y, double size, Brush color) {
            var f = new FormattedText(text, CultureInfo.GetCultureInfo("pt-BR"), FlowDirection.LeftToRight, new Typeface("Segoe UI"), size, color, 1.0);
            dc.DrawText(f, new Point(x - f.Width / 2, y - f.Height / 2));
        }
    }
    internal sealed class PadView : FrameworkElement {
        internal long RenderCount;
        static readonly Geometry Body=CreateBody();
        static Geometry CreateBody(){var g=Geometry.Parse("M 154,58 C 119,54 96,76 85,115 L 45,262 C 35,301 71,317 99,288 L 160,230 C 236,254 364,254 440,230 L 501,288 C 529,317 565,301 555,262 L 515,115 C 504,76 481,54 446,58 Z");g.Freeze();return g;}
        internal ControllerState State = new ControllerState();
        internal HashSet<string> Tested = new HashSet<string>();
        internal double LX, LY, RX, RY;
        void Key(DrawingContext dc, string name, string label, double x, double y, double r) {
            bool pressed = State.Buttons.Contains(name);
            Brush fill = pressed ? Paint.Green : Paint.Brush("#20252D");
            dc.DrawEllipse(fill, new Pen(Tested.Contains(name) ? Paint.Green : Paint.Brush("#515A68"), 1.5), new Point(x,y),r,r);
            Paint.Text(dc,label,x,y, r > 15 ? 14 : 10, pressed ? Paint.Dark : Paint.Ink);
        }
        void Stick(DrawingContext dc, double x, double y, double sx, double sy, string key) {
            dc.DrawEllipse(Paint.Brush("#11151B"), new Pen(Paint.Brush("#4B5461"),2),new Point(x,y),36,36);
            dc.DrawEllipse(Paint.Brush("#333C48"), new Pen(State.Buttons.Contains(key) ? Paint.Green : Paint.Brush("#657082"),2), new Point(x+sx*12,y-sy*12),24,24);
            Paint.Text(dc,key,x+sx*12,y-sy*12,10,Paint.Muted);
        }
        protected override void OnRender(DrawingContext dc) {
            RenderCount++;
            double scale = Math.Min(ActualWidth/600,ActualHeight/330);
            if (scale <= 0) return;
            dc.PushTransform(new TranslateTransform((ActualWidth-600*scale)/2,(ActualHeight-330*scale)/2)); dc.PushTransform(new ScaleTransform(scale,scale));
            dc.DrawGeometry(Paint.Brush("#303742"),new Pen(Paint.Brush("#596371"),2),Body);
            dc.DrawRoundedRectangle(Paint.Brush("#252B34"),null,new Rect(200,73,200,145),28,28);
            Key(dc,"ZL","ZL",137,36,17); Key(dc,"ZR","ZR",463,36,17);
            Key(dc,"L","L",190,55,17); Key(dc,"R","R",410,55,17);
            Stick(dc,163,123,LX,LY,"L3"); Stick(dc,359,205,RX,RY,"R3");
            Key(dc,"X","X",454,100,19); Key(dc,"Y","Y",421,133,19); Key(dc,"A","A",487,133,19); Key(dc,"B","B",454,166,19);
            Key(dc,"UP","↑",224,183,14); Key(dc,"DOWN","↓",224,235,14); Key(dc,"LEFT","←",198,209,14); Key(dc,"RIGHT","→",250,209,14);
            Key(dc,"MINUS","−",256,102,13); Key(dc,"PLUS","+",344,102,13);
            Key(dc,"CAPTURE","▣",271,154,12); Key(dc,"HOME","⌂",329,154,12); Key(dc,"C","C",300,200,13);
            Key(dc,"GL","GL",263,282,15); Key(dc,"GR","GR",337,282,15);
            Paint.Text(dc,"TRASEIROS",300,313,9,Paint.Muted);
            dc.Pop(); dc.Pop();
        }
    }
    internal sealed class StickView : FrameworkElement {
        internal double X, Y, Zone;
        protected override void OnRender(DrawingContext dc) {
            double cx=ActualWidth/2,cy=ActualHeight/2,r=Math.Min(ActualHeight,ActualWidth)/2-10;
            if(r<=0)return;
            var point=new Point(cx,cy);
            dc.DrawEllipse(Paint.Brush("#12161B"),new Pen(Paint.Brush("#3C4552"),1),point,r,r);
            dc.DrawLine(new Pen(Paint.Brush("#303A46"),1),new Point(cx-r,cy),new Point(cx+r,cy));
            dc.DrawLine(new Pen(Paint.Brush("#303A46"),1),new Point(cx,cy-r),new Point(cx,cy+r));
            dc.DrawEllipse(Paint.Brush("#30473F"),null,point,r*Zone,r*Zone);
            dc.DrawLine(new Pen(Paint.Green,1.5),point,new Point(cx+X*r,cy-Y*r));
            dc.DrawEllipse(Paint.Green,null,new Point(cx+X*r,cy-Y*r),4,4);
        }
    }
}
