using System;
using System.Diagnostics;
using System.Globalization;

namespace Switch2Bridge {
    public sealed class LatencySnapshot {
        public long Samples,Overflows,Expired,DriverRetries;public int WindowCount;public double P50,P95,P99,Max;
        public override string ToString(){return Samples==0?"Aguardando alterações de saída para medir…":string.Format(CultureInfo.InvariantCulture,"USB → driver: mediana {0:F3} ms · p95 {1:F3} ms · p99 {2:F3} ms\nJanela: {3} amostras · máximo {4:F3} ms · total {5}\nFila cheia: {6} · filas antigas: {7} · esperas do driver: {8}",P50,P95,P99,WindowCount,Max,Samples,Overflows,Expired,DriverRetries);}
    }
    public sealed class LatencyMetrics {
        readonly object gate=new object();readonly double[] values=new double[4096];int at,count;long total;
        public void Record(long received,long accepted){if(received<=0||accepted<received)return;lock(gate){values[at]=(accepted-received)*1000.0/Stopwatch.Frequency;at=(at+1)%values.Length;if(count<values.Length)count++;total++;}}
        public void Reset(){lock(gate){at=count=0;total=0;}}
        public LatencySnapshot Snapshot(long overflows=0,long expired=0){
            double[] copy;long samples;lock(gate){copy=new double[count];Array.Copy(values,copy,count);samples=total;}
            Array.Sort(copy);return new LatencySnapshot{Samples=samples,WindowCount=copy.Length,P50=Quantile(copy,.5),P95=Quantile(copy,.95),P99=Quantile(copy,.99),Max=Quantile(copy,1),Overflows=overflows,Expired=expired};
        }
        static double Quantile(double[] data,double p){if(data.Length==0)return 0;double at=(data.Length-1)*p;int lo=(int)Math.Floor(at),hi=(int)Math.Ceiling(at);return data[lo]+(data[hi]-data[lo])*(at-lo);}
    }
}
