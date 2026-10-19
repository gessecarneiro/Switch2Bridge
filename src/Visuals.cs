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
        static Geometry Shape(string path) {var geometry=Geometry.Parse(path);geometry.Freeze();return geometry;}
        static readonly Geometry Body=Shape("M 222,86 C 191,80 157,98 143,135 C 125,184 98,268 88,321 C 79,369 112,388 144,363 L 230,293 C 319,318 441,318 530,293 L 616,363 C 648,388 681,369 672,321 C 662,268 635,184 617,135 C 603,98 569,80 538,86 Z");
        static readonly Geometry Face=Shape("M 226,94 C 199,90 167,105 155,140 L 129,220 C 123,240 131,260 151,273 L 212,290 C 310,318 450,318 548,290 L 609,273 C 629,260 637,240 631,220 L 605,140 C 593,105 561,90 534,94 Z");
        static readonly Geometry Dpad=Shape("M 260,218 L 284,218 Q 288,218 288,222 L 288,244 L 310,244 Q 314,244 314,248 L 314,272 Q 314,276 310,276 L 288,276 L 288,298 Q 288,302 284,302 L 260,302 Q 256,302 256,298 L 256,276 L 234,276 Q 230,276 230,272 L 230,248 Q 230,244 234,244 L 256,244 L 256,222 Q 256,218 260,218 Z");
        static readonly Geometry HomeIcon=Shape("M -6,0 L 0,-5 L 6,0 M -4,-1 L -4,5 L -1,5 L -1,2 L 1,2 L 1,5 L 4,5 L 4,-1");
        static readonly Brush ShellFill=Gradient("#56616F","#303944"), FaceFill=Gradient("#303944","#202731"), CapFill=Gradient("#394350","#202731"), StickFill=Gradient("#515D6D","#323C49");
        static readonly Brush Idle=Paint.Brush("#19212B"), TestedFill=Paint.Brush("#243B34"), Outline=Paint.Brush("#5C6878"), Soft=Paint.Brush("#36414F");
        static readonly Pen Edge=Line(Outline,1.4), QuietEdge=Line(Soft,1), TestedEdge=Line(Paint.Brush("#4D9A80"),1.5), ActiveEdge=Line(Paint.Green,2);
        static readonly DrawingGroup Shell=CreateShell();
        static Brush Gradient(string top,string bottom) {
            var brush=new LinearGradientBrush((Color)ColorConverter.ConvertFromString(top),(Color)ColorConverter.ConvertFromString(bottom),90);brush.Freeze();return brush;
        }
        static Pen Line(Brush brush,double width) {var pen=new Pen(brush,width);pen.Freeze();return pen;}
        static DrawingGroup CreateShell() {
            var drawing=new DrawingGroup();
            using(var dc=drawing.Open()) {
                // Cached layers give the shell depth without a live blur effect.
                dc.DrawEllipse(Paint.Brush("#090D12"),null,new Point(380,355),249,29);
                dc.PushTransform(new TranslateTransform(0,7));dc.DrawGeometry(Paint.Brush("#10151C"),null,Body);dc.Pop();
                dc.DrawGeometry(ShellFill,Edge,Body);
                dc.DrawGeometry(FaceFill,Line(Paint.Brush("#647080"),1),Face);
                dc.DrawGeometry(null,Line(Paint.Brush("#1A212A"),2),Shape("M 157,279 C 150,305 135,330 116,351 M 603,279 C 610,305 625,330 644,351"));
                dc.DrawLine(Line(Paint.Brush("#7B8796"),1),new Point(276,94),new Point(484,94));
                Paint.Text(dc,"S2",380,161,23,Paint.Brush("#6F7C8D"));
                Paint.Text(dc,"B R I D G E",380,186,8,Paint.Brush("#667384"));
                dc.DrawRoundedRectangle(Paint.Brush("#19212A"),QuietEdge,new Rect(307,394,146,34),17,17);
                Paint.Text(dc,"TRASEIROS",380,411,9,Paint.Muted);
            }
            drawing.Freeze();return drawing;
        }
        internal ControllerState State = new ControllerState();
        internal HashSet<string> Tested = new HashSet<string>();
        internal double LX, LY, RX, RY;
        Pen Border(string name) {return State.Buttons.Contains(name)?ActiveEdge:Tested.Contains(name)?TestedEdge:Edge;}
        Brush Fill(string name) {return State.Buttons.Contains(name)?Paint.Green:Tested.Contains(name)?TestedFill:CapFill;}
        void Key(DrawingContext dc,string name,string label,double x,double y,double radius) {
            bool pressed=State.Buttons.Contains(name);
            if(pressed)dc.DrawEllipse(Paint.Brush("#244D40"),null,new Point(x,y),radius+5,radius+5);
            dc.DrawEllipse(Paint.Brush("#141A22"),null,new Point(x,y+3),radius+1,radius+1);
            dc.DrawEllipse(Fill(name),Border(name),new Point(x,y),radius,radius);
            if(name=="HOME") {
                dc.PushTransform(new TranslateTransform(x,y));dc.DrawGeometry(null,pressed?Line(Paint.Dark,1.5):Line(Paint.Ink,1.5),HomeIcon);dc.Pop();
            } else if(name=="CAPTURE") {
                var ink=pressed?Paint.Dark:Paint.Ink;
                dc.DrawRoundedRectangle(null,pressed?Line(Paint.Dark,1.3):Line(Paint.Ink,1.3),new Rect(x-5,y-5,10,10),2,2);
                dc.DrawEllipse(ink,null,new Point(x,y),2,2);
            } else Paint.Text(dc,label,x,y-0.5,radius>19?17:12,pressed?Paint.Dark:Paint.Ink);
        }
        void Shoulder(DrawingContext dc,string name,double x,double y,double width,double height) {
            dc.DrawRoundedRectangle(Paint.Brush("#0B1016"),null,new Rect(x,y+3,width,height),height/2,height/2);
            dc.DrawRoundedRectangle(Fill(name),Border(name),new Rect(x,y,width,height),height/2,height/2);
            Paint.Text(dc,name,x+width/2,y+height/2,12,State.Buttons.Contains(name)?Paint.Dark:Paint.Ink);
        }
        void Stick(DrawingContext dc,double x,double y,double sx,double sy,string key) {
            bool pressed=State.Buttons.Contains(key);
            dc.DrawEllipse(Paint.Brush("#121922"),QuietEdge,new Point(x,y),49,49);
            dc.DrawEllipse(null,Line(Paint.Brush("#4F5E6E"),2),new Point(x,y),43,43);
            var center=new Point(x+sx*13,y-sy*13);
            dc.DrawEllipse(Paint.Brush("#0C1118"),null,new Point(center.X,center.Y+4),34,34);
            dc.DrawEllipse(pressed?Paint.Green:StickFill,Border(key),center,33,33);
            dc.DrawEllipse(null,pressed?Line(Paint.Brush("#326F59"),1):Line(Paint.Brush("#657384"),1),center,27,27);
            Paint.Text(dc,key,center.X,center.Y,10,pressed?Paint.Dark:Paint.Brush("#B2BECD"));
        }
        void Direction(DrawingContext dc,string key,double x,double y,string arrow,bool vertical) {
            var rect=new Rect(x-(vertical?11:13),y-(vertical?13:11),vertical?22:26,vertical?26:22);
            bool pressed=State.Buttons.Contains(key),seen=Tested.Contains(key);
            dc.DrawRoundedRectangle(pressed?Paint.Green:seen?TestedFill:Idle,pressed?ActiveEdge:seen?TestedEdge:null,rect,3,3);
            Paint.Text(dc,arrow,x,y,11,pressed?Paint.Dark:Paint.Muted);
        }
        protected override void OnRender(DrawingContext dc) {
            RenderCount++;
            double scale = Math.Min(ActualWidth/760,ActualHeight/440);
            if (scale <= 0) return;
            dc.PushTransform(new TranslateTransform((ActualWidth-760*scale)/2,(ActualHeight-440*scale)/2));dc.PushTransform(new ScaleTransform(scale,scale));
            Shoulder(dc,"ZL",174,35,112,30);Shoulder(dc,"ZR",474,35,112,30);
            Shoulder(dc,"L",151,65,145,22);Shoulder(dc,"R",464,65,145,22);
            dc.DrawDrawing(Shell);
            Stick(dc,228,168,LX,LY,"L3");Stick(dc,470,261,RX,RY,"R3");
            Key(dc,"X","X",553,122,21);Key(dc,"Y","Y",514,161,21);Key(dc,"A","A",592,161,21);Key(dc,"B","B",553,200,21);
            dc.DrawGeometry(Idle,Edge,Dpad);
            Direction(dc,"UP",272,233,"▴",true);Direction(dc,"DOWN",272,287,"▾",true);Direction(dc,"LEFT",245,260,"◂",false);Direction(dc,"RIGHT",299,260,"▸",false);
            dc.DrawEllipse(Paint.Brush("#3C4755"),null,new Point(272,260),3,3);
            Key(dc,"MINUS","−",319,130,13);Key(dc,"PLUS","+",441,130,13);
            Key(dc,"CAPTURE","",319,191,12);Key(dc,"HOME","",441,191,12);Key(dc,"C","C",405,237,14);
            Shoulder(dc,"GL",241,397,55,28);Shoulder(dc,"GR",464,397,55,28);
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
