using System;
using System.Threading;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Switch2Bridge;

class ProtocolTests {
    static int count;
    static void Check(bool value, string name) { if(!value)throw new Exception(name); count++; }
    static byte[] Fixture(byte id) {
        var b=new byte[64];b[0]=id;
        int at=id==5?11:6;
        Pack(b,at,2048,2048);Pack(b,at+3,2048,2048);return b;
    }
    static void Pack(byte[] b,int i,int x,int y) { b[i]=(byte)x;b[i+1]=(byte)(((x>>8)&15)|((y&15)<<4));b[i+2]=(byte)(y>>4); }
    static void Buttons(byte id,int[] offsets,int[] bits) {
        for(int i=0;i<ControllerState.Names.Length;i++) {
            byte[] b=Fixture(id);b[offsets[i]]|=(byte)(1<<bits[i]);ControllerState s;
            Check(ControllerState.TryParse(b,out s),"parse");
            Check(s.Buttons.Count==1 && s.Buttons.Contains(ControllerState.Names[i]),"button "+id+" "+ControllerState.Names[i]);
        }
    }
    static int Main(string[] args) {
        try {
            ControllerState s;
            Check(!ControllerState.TryParse(null,out s),"null");Check(!ControllerState.TryParse(new byte[0],out s),"empty");
            for(int n=0;n<64;n++) { var b=new byte[n];if(n>0)b[0]=9;Check(!ControllerState.TryParse(b,out s),"truncated "+n); }
            var bad=Fixture(4);Check(!ControllerState.TryParse(bad,out s),"unknown ID");
            foreach(byte id in new byte[]{5,9}) {
                var b=Fixture(id);Check(ControllerState.TryParse(b,out s)&&s.Buttons.Count==0,"neutral");
                Check(s.LX==2048&&s.LY==2048&&s.RX==2048&&s.RY==2048,"neutral axes");
                int at=id==5?11:6;Pack(b,at,0,4095);Pack(b,at+3,4095,0);ControllerState.TryParse(b,out s);
                Check(s.LX==0&&s.LY==4095&&s.RX==4095&&s.RY==0,"packed endpoints");
                b[at]=123;Check(s.Report[at]!=123,"report ownership");
                for(int k=0;k<4096;k+=137) { Pack(b,at,k,4095-k);ControllerState.TryParse(b,out s);Check(s.LX==k&&s.LY==4095-k,"packed sweep"); }
            }
            Buttons(9,new[]{3,3,3,3,4,4,4,4,4,3,4,3,4,3,4,3,5,5,5,5,5},new[]{1,0,3,2,3,0,2,1,4,4,5,5,6,6,7,7,0,1,4,3,2});
            Buttons(5,new[]{5,5,5,5,7,7,7,7,7,5,7,5,6,6,6,6,6,6,6,8,8},new[]{3,2,1,0,1,0,3,2,6,6,7,7,0,1,3,2,4,5,6,1,0});
            Check(ControllerState.Normalize(2048,2048)==0,"center");Check(ControllerState.Normalize(0,2048)==-1,"min");Check(ControllerState.Normalize(4095,2048)==1,"max");
            var z=ControllerState.ApplyDeadzone(0.04,0.04,0.08);Check(z[0]==0&&z[1]==0,"radial deadzone");
            var diagonal=ControllerState.ApplyDeadzone(1,1,0.08);Check(Math.Abs(diagonal[0]*diagonal[0]+diagonal[1]*diagonal[1]-1)<1e-8,"circular clamp");
            var end=ControllerState.ApplyDeadzone(1,0,0.08);Check(end[0]==1&&end[1]==0,"full range");
            if(args.Contains("--usb-stress"))for(int i=0;i<20;i++)using(var usb=new UsbTransport()) { usb.Start();Thread.Sleep(10);usb.Stop(); }
            MappingTests();StorageTests();OutputTests();ReadyTests();OptimizedMappingTests();EventOutputTests();MetricTests();
            Console.WriteLine("PASS: "+count+" assertions (protocol, profiles, mapping, watchdog, output lifecycle). Physical Xbox output still requires a driver/hardware test.");return 0;
        }catch(Exception e) { Console.Error.WriteLine(e);return 1; }
    }
    static void MappingTests() {
        Check(Marshal.SizeOf(typeof(XboxReport))==12,"Xbox ABI report size");
        var s=new ControllerState();var p=Profile.Default(false);p.Validate();
        Check(Mapper.Map(s,p).Equals(new XboxReport()),"neutral mapping");
        s.Buttons.Add("B");Check(Mapper.Map(s,p).Buttons==0x1000,"physical south to Xbox A");
        s.Buttons.Clear();s.Buttons.Add("A");Check(Mapper.Map(s,p).Buttons==0x2000,"physical east to Xbox B");
        var n=Profile.Default(true);Check(Mapper.Map(s,n).Buttons==0x1000,"label preset");
        foreach(XboxAction action in Enum.GetValues(typeof(XboxAction))) {
            p.Bindings.First(b=>b.Source=="GL").Target=action;s.Buttons.Clear();s.Buttons.Add("GL");var r=Mapper.Map(s,p);
            Check(r.Buttons==Mapper.Mask(action)&&r.LT==(action==XboxAction.LT?255:0)&&r.RT==(action==XboxAction.RT?255:0),"GL remap "+action);
        }
        p=Profile.Default(false);p.Bindings.First(b=>b.Source=="GR").Target=XboxAction.A;
        s.Buttons.Clear();s.Buttons.Add("B");s.Buttons.Add("GR");Check(Mapper.Map(s,p).Buttons==0x1000,"multiple sources combined");
        s.Buttons.Remove("B");Check(Mapper.Map(s,p).Buttons==0x1000,"one source release keeps other source pressed");
        s.Buttons.Clear();Check(Mapper.Map(s,p).Buttons==0,"all releases");
        s.Buttons.Add("UP");s.Buttons.Add("DOWN");s.Buttons.Add("LEFT");s.Buttons.Add("RIGHT");Check(Mapper.Map(s,p).Buttons==0,"opposing directions");
        s.Buttons.Clear();s.Buttons.Add("ZL");s.Buttons.Add("ZR");Check(Mapper.Map(s,p).LT==255&&Mapper.Map(s,p).RT==255,"digital triggers");
        s=new ControllerState{LX=0,LY=2048,RX=2048,RY=2048};Check(Mapper.Map(s,p).LX==-32768,"signed min");
        s.LX=4095;Check(Mapper.Map(s,p).LX==32767,"signed max");p.InvertLX=true;Check(Mapper.Map(s,p).LX==-32768,"axis inversion");
        p.SwapSticks=true;Check(Mapper.Map(s,p).RX==-32768&&Mapper.Map(s,p).LX==0,"stick swap");
        var clone=p.Clone();clone.Bindings[0].Target=XboxAction.None;Check(p.Bindings[0].Target!=XboxAction.None,"deep clone");
        clone=p.Clone();clone.Bindings.Add(clone.Bindings[0]);bool rejected=false;try{clone.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"reject duplicate binding");
        clone=p.Clone();clone.Deadzone=double.NaN;rejected=false;try{clone.Validate();}catch(InvalidDataException){rejected=true;}Check(rejected,"reject NaN zone");
        Check(Mapper.Map(null,p).Equals(new XboxReport()),"no physical sample is fully neutral");
    }
    static void StorageTests() {
        string root=Path.GetFullPath(Path.Combine(Environment.CurrentDirectory,"work"));Directory.CreateDirectory(root);
        string folder=Path.Combine(root,"profile-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try {
            var store=new ProfileStore(folder);var list=store.LoadAll(null);Check(list.Count==1,"default profile persisted");var p=list[0];
            p.Name="Ação & direção <teste>";p.CenterLX=2010;p.InvertRY=true;p.Bindings.First(b=>b.Source=="C").Target=XboxAction.Guide;store.Save(p);store.SetActive(p);
            var loaded=store.LoadAll(null).Single();Check(loaded.Name==p.Name&&loaded.CenterLX==2010&&loaded.InvertRY&&loaded.Bindings.First(b=>b.Source=="C").Target==XboxAction.Guide,"profile round trip");Check(store.ActiveId()==p.Id,"restore selected profile");
            p.Name="Atualizado";store.Save(p);Check(store.LoadAll(null).Single().Name=="Atualizado","atomic update");
            p.Id="../../escape";bool rejected=false;try{store.Save(p);}catch(InvalidDataException){rejected=true;}Check(rejected,"reject path traversal ID");
            string bad=Path.Combine(folder,"bad.xml");File.WriteAllText(bad,"<!DOCTYPE x [<!ENTITY test SYSTEM 'file:///C:/Windows/win.ini'>]><Profile>&test;</Profile>");rejected=false;try{ProfileStore.Read(bad);}catch(Exception){rejected=true;}Check(rejected,"reject external entities");
            int errors=0;list=store.LoadAll(delegate{errors++;});Check(list.Count==1&&errors==1&&File.Exists(bad),"keep malformed file and load valid profiles");
        }finally { if(Path.GetFullPath(folder).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))Directory.Delete(folder,true); }
    }
    sealed class FakeBackend : IXboxBackend {
        public readonly List<XboxReport> Reports=new List<XboxReport>();public volatile bool Connected,Disposed;public int FailAfter=int.MaxValue;public ManualResetEvent HoldConnect;
        public void Connect(){Connected=true;if(HoldConnect!=null)HoldConnect.WaitOne();}
        public void Submit(XboxReport r){lock(Reports){if(Reports.Count>=FailAfter)throw new IOException("simulated bus loss");Reports.Add(r);}}
        public void Dispose(){Disposed=true;}
        public XboxReport Last(){lock(Reports)return Reports.Count==0?new XboxReport():Reports.Last();}
        public bool Saw(ushort button){lock(Reports)return Reports.Any(r=>r.Buttons==button);}
        public int Size(){lock(Reports)return Reports.Count;}
        public bool Pulse(){lock(Reports){int i=Reports.FindIndex(r=>r.Buttons==4096);return i>=0&&Reports.Skip(i+1).Any(r=>r.Buttons==0);}}
    }
    static bool WaitFor(Func<bool> condition,int ms=1500){var w=System.Diagnostics.Stopwatch.StartNew();while(w.ElapsedMilliseconds<ms){if(condition())return true;Thread.Sleep(5);}return condition();}
    static void OutputTests() {
        var fake=new FakeBackend();using(var output=new XboxOutput(()=>fake)){
            output.Start();Check(WaitFor(()=>output.Active),"output starts");output.Update(new XboxReport{Buttons=4096});Check(WaitFor(()=>fake.Saw(4096)),"mapped input submitted");
            Check(WaitFor(()=>fake.Last().Equals(new XboxReport())),"250 ms stale watchdog releases held button");
            output.Update(new XboxReport{RT=255,LX=32767});Check(WaitFor(()=>fake.Last().RT==255),"input resumes after stall");
            output.Neutral();Check(WaitFor(()=>fake.Last().Equals(new XboxReport())),"explicit disconnect neutral");
            output.Update(new XboxReport{Buttons=8192});Check(WaitFor(()=>fake.Saw(8192)),"next input");output.Stop();Check(fake.Disposed&&fake.Last().Equals(new XboxReport()),"stop releases then disposes");
        }
        fake=new FakeBackend{FailAfter=1};string status="";using(var output=new XboxOutput(()=>fake)){
            output.StatusChanged+=s=>status=s;output.Start();output.Update(new XboxReport{Buttons=4096});Check(WaitFor(()=>fake.Disposed),"bus failure disposes backend");Check(!output.Active&&status=="simulated bus loss","bus failure surfaced");
        }
        using(var output=new XboxOutput(()=>{throw new InvalidOperationException("driver missing");})){output.StatusChanged+=s=>status=s;output.Start();Check(WaitFor(()=>status=="driver missing"),"missing driver surfaced");Check(!output.Active,"missing driver stays inactive");}
    }
    static void ReadyTests() {
        int calls=0,delays=0,traces=0;
        VigemBackend.WaitForReady(delegate{calls++;if(calls<3)throw new System.ComponentModel.Win32Exception(483);},null,delegate{delays++;},delegate{traces++;});
        Check(calls==3&&delays==2&&traces==2,"driver first initialization retries readiness on same target");
        calls=0;bool failed=false;try{VigemBackend.WaitForReady(delegate{calls++;throw new System.ComponentModel.Win32Exception(5);},null,delegate{throw new Exception("unexpected retry");},null);}catch(System.ComponentModel.Win32Exception e){failed=e.NativeErrorCode==5;}
        Check(failed&&calls==1,"non-transient driver error is not retried");
        calls=0;failed=false;try{VigemBackend.WaitForReady(delegate{calls++;throw new System.ComponentModel.Win32Exception(483);},null,delegate{},null);}catch(InvalidOperationException){failed=true;}
        Check(failed&&calls==10,"readiness retry is bounded");
        calls=0;failed=false;try{VigemBackend.WaitForReady(delegate{calls++;},delegate{return true;},delegate{},null);}catch(OperationCanceledException){failed=true;}
        Check(failed&&calls==0,"readiness cancellation before IO");
    }
    static void OptimizedMappingTests(){
        var random=new Random(3471);
        for(int n=0;n<1000;n++){
            var p=Profile.Default(n%2==0);p.Deadzone=(n%31)/100.0;p.InvertLX=n%2==0;p.InvertLY=n%3==0;p.InvertRX=n%5==0;p.InvertRY=n%7==0;p.SwapSticks=n%11==0;
            p.CenterLX=random.Next(1800,2300);p.CenterLY=random.Next(1800,2300);p.CenterRX=random.Next(1800,2300);p.CenterRY=random.Next(1800,2300);
            foreach(var b in p.Bindings)b.Target=(XboxAction)random.Next(18);
            var s=new ControllerState{LX=random.Next(4096),LY=random.Next(4096),RX=random.Next(4096),RY=random.Next(4096)};
            foreach(string name in ControllerState.Names)if(random.Next(2)==0)s.Buttons.Add(name);
            Check(new MappingPlan(p).Map(s).Equals(Mapper.Map(s,p)),"compiled mapping equivalence "+n);
        }
        var profile=Profile.Default(false);var plan=new MappingPlan(profile);var sample=new ControllerState();sample.Buttons.Add("B");
        profile.Bindings.First(b=>b.Source=="B").Target=XboxAction.None;profile.Deadzone=.3;
        Check(plan.Map(sample).Buttons==4096,"runtime snapshot independent from profile edits");
        var set=new ButtonSet();Check(set.Add("GL")&&!set.Add("GL")&&set.Count==1,"button set duplicate");Check(set.Contains("GL")&&!set.Contains("unknown"),"button set lookup");Check(set.Remove("GL")&&set.Count==0,"button set release");
        foreach(string name in ControllerState.Names)set.Add(name);Check(set.Count==21&&set.ToArray().SequenceEqual(ControllerState.Names),"button set enumeration");set.Clear();Check(set.Count==0,"button set clear");
    }
    static void EventOutputTests(){
        using(var allow=new ManualResetEvent(false)){
            var fake=new FakeBackend{HoldConnect=allow};using(var output=new XboxOutput(()=>fake)){
                output.Start();try{
                    Check(WaitFor(()=>fake.Connected),"gated backend entered");
                    for(int i=0;i<100;i++)output.Update(new XboxReport{Buttons=(ushort)(i%2==0?4096:0),LX=(short)(i+1)});
                }finally{allow.Set();}
                Check(WaitFor(()=>{lock(fake.Reports)return fake.Reports.Where(r=>r.LX>=1&&r.LX<=100).Select(r=>r.LX).Distinct().Count()==100;}),"all 100 rapid button edges preserved");
                Check(fake.Pulse(),"press and release both delivered");
                Check(output.Performance.Samples>=100&&output.Performance.Overflows==0,"latency samples count successful edge sends");
            }
        }
        using(var allow=new ManualResetEvent(false)){
            var fake=new FakeBackend{HoldConnect=allow};using(var output=new XboxOutput(()=>fake)){
                output.Start();try{Check(WaitFor(()=>fake.Connected),"cancel queued backend entered");output.Update(new XboxReport{Buttons=4096});output.Neutral();}finally{allow.Set();}
                Check(WaitFor(()=>fake.Size()>0)&&!fake.Saw(4096),"neutral flushes unsent presses");
            }
        }
        using(var allow=new ManualResetEvent(false)){
            var fake=new FakeBackend{HoldConnect=allow};using(var output=new XboxOutput(()=>fake)){
                output.Start();try{Check(WaitFor(()=>fake.Connected),"overflow backend entered");for(int i=0;i<600;i++)output.Update(new XboxReport{Buttons=(ushort)(i%2==0?4096:0)});Check(output.Performance.Overflows>0,"queue overflow reported and bounded");}finally{allow.Set();}
                Check(WaitFor(()=>fake.Size()>0),"overflow queue recovers");
            }
        }
        using(var allow=new ManualResetEvent(false)){
            var fake=new FakeBackend{HoldConnect=allow};using(var output=new XboxOutput(()=>fake)){
                output.Start();try{Check(WaitFor(()=>fake.Connected),"stale backend entered");output.Update(new XboxReport{Buttons=4096});Thread.Sleep(300);}finally{allow.Set();}
                Check(WaitFor(()=>fake.Size()>0)&&!fake.Saw(4096),"stale queued button is not replayed");
            }
        }
        using(var allow=new ManualResetEvent(false)){
            var fake=new FakeBackend{HoldConnect=allow};using(var output=new XboxOutput(()=>fake)){
                output.Start();try{Check(WaitFor(()=>fake.Connected),"coalescing backend entered");output.Update(new XboxReport{Buttons=4096,LX=10});output.Update(new XboxReport{Buttons=4096,LX=20});output.Update(new XboxReport{LX=30});}finally{allow.Set();}
                Check(WaitFor(()=>fake.Last().LX==30),"coalesced analogue arrives");lock(fake.Reports)Check(fake.Reports.Any(r=>r.Buttons==4096&&r.LX==20)&&!fake.Reports.Any(r=>r.LX==10),"analogue coalescing preserves button transitions");
            }
        }
    }
    static void MetricTests(){
        var metrics=new LatencyMetrics();long start=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int i=1;i<=100;i++)metrics.Record(start,start+(long)(i*System.Diagnostics.Stopwatch.Frequency/1000.0));
        var s=metrics.Snapshot();Check(s.Samples==100&&Math.Abs(s.P50-50.5)<.01&&Math.Abs(s.P99-99.01)<.01,"latency quantiles");
        metrics.Record(0,start);metrics.Record(start,start-1);Check(metrics.Snapshot().Samples==100,"invalid latency samples ignored");
        for(int i=0;i<5000;i++)metrics.Record(start,start+System.Diagnostics.Stopwatch.Frequency/1000);
        s=metrics.Snapshot(2,3);Check(s.WindowCount==4096&&s.Samples==5100&&s.Overflows==2&&s.Expired==3,"fixed latency window and counters");metrics.Reset();Check(metrics.Snapshot().Samples==0,"latency reset");
    }
}
