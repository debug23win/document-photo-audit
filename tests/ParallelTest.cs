using System;
using System.IO;
using System.Linq;
using System.Threading;
using PhotoAudit;
class ParallelTest {
    static void Assert(bool value,string name){if(!value)throw new Exception(name);}
    static int Main(){
        int active=0,peak=0,finished=0;var output=new int[24];
        ParallelWork.For(output.Length,3,CancellationToken.None,i=>{int n=Interlocked.Increment(ref active),old;do{old=peak;if(n<=old)break;}while(Interlocked.CompareExchange(ref peak,n,old)!=old);Thread.Sleep(10+(i%3)*5);output[i]=i*i;Interlocked.Decrement(ref active);Interlocked.Increment(ref finished);});
        Assert(peak>=2&&peak<=3,"Pool must run concurrently within its limit");Assert(finished==24&&active==0&&output.Select((v,i)=>v==i*i).All(v=>v),"Work lost or source ordering changed");
        bool failed=false;try{ParallelWork.For(30,3,CancellationToken.None,i=>{if(i==2)throw new InvalidDataException("source failed");Thread.Sleep(3);});}catch(InvalidDataException ex){failed=ex.Message=="source failed";}Assert(failed,"Original worker error must be propagated");
        using(var cancel=new CancellationTokenSource()){int seen=0;bool cancelled=false;try{ParallelWork.For(100,3,cancel.Token,i=>{if(Interlocked.Increment(ref seen)==5)cancel.Cancel();Thread.Sleep(5);});}catch(OperationCanceledException){cancelled=true;}Assert(cancelled&&seen<100,"Cancellation must stop queued work");}
        Assert(ParallelWork.Limit(0,12,2UL*1024*1024*1024,512)==3,"Memory limit missing");Assert(ParallelWork.Limit(8,4,16UL*1024*1024*1024,192)==4,"CPU limit missing");Assert(ParallelWork.Limit(0,1,0,512)==1&&ParallelWork.Limit(1,12,16UL*1024*1024*1024,192)==1,"Serial and low-memory fallback missing");
        Console.WriteLine("Parallel tests passed: bounded concurrency, stable result slots, errors, cancellation, CPU/memory limits and serial fallback");return 0;
    }
}
