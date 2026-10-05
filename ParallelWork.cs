using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace PhotoAudit {
    public static class ParallelWork {
        [StructLayout(LayoutKind.Sequential)]sealed class MemoryStatus {
            public uint Length=64,Load;public ulong TotalPhysical,AvailablePhysical,TotalPage,AvailablePage,TotalVirtual,AvailableVirtual,AvailableExtended;
        }
        [DllImport("kernel32.dll",SetLastError=true)]static extern bool GlobalMemoryStatusEx([In,Out]MemoryStatus status);
        public static ulong AvailableMemory(){try{var m=new MemoryStatus();return GlobalMemoryStatusEx(m)?m.AvailablePhysical:512UL*1024*1024;}catch{return 512UL*1024*1024;}}
        public static int Limit(int requested,int processors,ulong available,int megabytesPerWorker) {
            if(requested<0||requested>8)throw new ArgumentOutOfRangeException("requested");
            int cpu=Math.Max(1,Math.Min(8,processors-1));int wanted=requested==0?cpu:Math.Min(requested,Math.Max(1,processors));
            ulong reserve=256UL*1024*1024,budget=available>reserve?available-reserve:0;
            int memory=(int)Math.Min(8UL,budget/(ulong)(megabytesPerWorker*1024L*1024));return Math.Max(1,Math.Min(wanted,memory));
        }
        public static int Workers(int requested,int memoryPerWorker){return Limit(requested,Environment.ProcessorCount,AvailableMemory(),memoryPerWorker);}
        public static int ImageBudget(int width,int height){return Math.Max(128,(int)Math.Ceiling((long)width*height*24.0/(1024*1024))+64);}
        public static void RelieveMemory(){if(AvailableMemory()<768UL*1024*1024)GC.Collect(2,GCCollectionMode.Forced,false);}
        public static void CheckFreeSpace(long available,long required,string drive){
            long reserve=64L*1024*1024;
            if(available<required+reserve)throw new IOException("Недостаточно свободного места на диске "+drive+". Свободно: "+(available/1048576L)+" МБ; требуется не менее "+((required+reserve+1048575)/1048576L)+" МБ. Освободите место или выберите папку отчётов на другом диске и запустите обработку снова.");
        }
        public static void CheckDiskSpace(string path,long required=0){
            var drive=SpaceDrive(path);if(drive!=null)CheckFreeSpace(drive.AvailableFreeSpace,required,drive.Name);
        }
        static DriveInfo SpaceDrive(string path){try{var drive=new DriveInfo(Path.GetPathRoot(Path.GetFullPath(path)));return drive.IsReady?drive:null;}catch(ArgumentException){return null;}catch(IOException){return null;}}
        public static Exception StorageError(Exception error,string path){
            if(error is OperationCanceledException||string.IsNullOrWhiteSpace(path))return error;
            if(!(error is IOException)&&!(error is ExternalException))return error;
            try{CheckDiskSpace(path);}catch(IOException ex){return new IOException(ex.Message,error);}catch(UnauthorizedAccessException){}
            return error;
        }
        public static void SaveImage(string path,Action save){CheckDiskSpace(path);try{save();}catch(ExternalException ex){throw StorageError(ex,path);}}
        public static void For(int count,int workers,CancellationToken cancel,Action<int> action) {
            cancel.ThrowIfCancellationRequested();if(count==0)return;
            if(workers<=1){for(int i=0;i<count;i++){cancel.ThrowIfCancellationRequested();action(i);}return;}
            using(var linked=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
                Exception first=null;
                try{Parallel.For(0,count,new ParallelOptions{MaxDegreeOfParallelism=Math.Min(count,workers),CancellationToken=linked.Token},i=>{
                    try{linked.Token.ThrowIfCancellationRequested();action(i);}catch(Exception ex){if(!(ex is OperationCanceledException))Interlocked.CompareExchange(ref first,ex,null);linked.Cancel();throw;}
                });}catch(AggregateException){cancel.ThrowIfCancellationRequested();if(first!=null)ExceptionDispatchInfo.Capture(first).Throw();throw;}catch(OperationCanceledException){cancel.ThrowIfCancellationRequested();if(first!=null)ExceptionDispatchInfo.Capture(first).Throw();throw;}
            }
        }
    }
    public sealed class RunTiming {
        readonly Stopwatch clock=Stopwatch.StartNew();readonly Dictionary<string,double> stages=new Dictionary<string,double>();string stage;double start;
        public void Mark(string name){double now=clock.Elapsed.TotalSeconds;if(stage!=null)stages[stage]=now-start;stage=name;start=now;}
        public Dictionary<string,double> Finish(){Mark(null);return new Dictionary<string,double>(stages);}
        public double Seconds{get{return clock.Elapsed.TotalSeconds;}}
    }
}
