using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
#if LEGACY
using Switch2Lab;
#else
using Switch2Bridge;
#endif

// Synthetic benchmark; no hardware, driver, game or global timer settings.
class PerformanceBench {
    sealed class Sink : IXboxBackend {
        public readonly long[] Arrivals=new long[751];
        public readonly double[] Samples=new double[750];
        readonly bool[] seen=new bool[751];public volatile int Count;
        public void Connect(){}
        public void Dispose(){}
        public void Submit(XboxReport report){int id=report.LX;if(id<1||id>750||seen[id])return;seen[id]=true;Samples[Count]=(Stopwatch.GetTimestamp()-Arrivals[id])*1000.0/Stopwatch.Frequency;Count++;}
    }
    static double Percentile(double[] data,double p){return data.Length==0?0:data[(int)Math.Ceiling((data.Length-1)*p)];}
    static int Main(string[] args){
        AppDomain.MonitoringIsEnabled=true;
        var result=new System.Text.StringBuilder();
        var sink=new Sink();using(var output=new XboxOutput(()=>sink)){
            output.Start();var startup=Stopwatch.StartNew();while(!output.Active&&startup.ElapsedMilliseconds<2000)Thread.Sleep(1);
            if(!output.Active)throw new Exception("Output did not start");
            long baseTick=Stopwatch.GetTimestamp(),period=Stopwatch.Frequency/250;
            for(int i=1;i<=750;i++){
                long due=baseTick+i*period;while(Stopwatch.GetTimestamp()<due)Thread.SpinWait(80);
                sink.Arrivals[i]=Stopwatch.GetTimestamp();output.Update(new XboxReport{LX=(short)i,Buttons=(ushort)(i%2==0?4096:0)});
            }
            Thread.Sleep(50);output.Stop();
            var data=new double[sink.Count];Array.Copy(sink.Samples,data,data.Length);Array.Sort(data);
            result.AppendLine("Synthetic input -> fake backend, 750 state changes at scheduled 250 Hz");
            result.AppendLine("delivered="+data.Length+"/750 p50_ms="+Percentile(data,.50).ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" p95_ms="+Percentile(data,.95).ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" p99_ms="+Percentile(data,.99).ToString("F4",System.Globalization.CultureInfo.InvariantCulture)+" max_ms="+Percentile(data,1).ToString("F4",System.Globalization.CultureInfo.InvariantCulture));
        }
        var p=Profile.Default(false);var bytes=new byte[64];bytes[0]=9;bytes[7]=8;bytes[8]=128;bytes[10]=8;bytes[11]=128;
#if OPTIMIZED
        var plan=new MappingPlan(p);
#endif
        ControllerState state;XboxReport value=new XboxReport();
        for(int i=0;i<2000;i++){ControllerState.TryParse(bytes,out state);value=Mapper.Map(state,p);}
        GC.Collect();GC.WaitForPendingFinalizers();long allocated=AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;var timer=Stopwatch.StartNew();int n=50000;
        for(int i=0;i<n;i++){
            ControllerState.TryParse(bytes,out state);
#if OPTIMIZED
            value=plan.Map(state);
#else
            value=Mapper.Map(state,p);
#endif
        }
        timer.Stop();long after=AppDomain.CurrentDomain.MonitoringTotalAllocatedMemorySize;
        result.AppendLine("Decode + mapping (public parser preserves report ownership), "+n+" iterations");
        result.AppendLine("ms="+timer.Elapsed.TotalMilliseconds.ToString("F3",System.Globalization.CultureInfo.InvariantCulture)+" managed_bytes_per_report="+((after-allocated)/(double)n).ToString("F2",System.Globalization.CultureInfo.InvariantCulture));
        GC.KeepAlive(value);Console.Write(result.ToString());if(args.Length>0)File.WriteAllText(args[0],result.ToString());return 0;
    }
}
