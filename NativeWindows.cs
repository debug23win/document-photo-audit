using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Windows.Data.Pdf;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;

namespace PhotoAudit {
    // Each worker owns its OCR engines and disposable bitmap/stream objects.
    public static class NativeWindows {
        sealed class Engines {
            public readonly OcrEngine Russian=OcrEngine.TryCreateFromLanguage(new Language("ru")),English=OcrEngine.TryCreateFromLanguage(new Language("en"));
            public Engines(){if(Russian==null)throw new InvalidOperationException("В Windows недоступно распознавание русского текста. Установите русский компонент OCR в параметрах языков Windows.");}
        }
        public static void Recognize(string input,string output,int workers,bool cache,Action<string> progress,CancellationToken cancel) {
            Directory.CreateDirectory(output);var files=Directory.EnumerateFiles(input).Where(f=>new[]{".jpg",".png"}.Contains(Path.GetExtension(f).ToLowerInvariant())).OrderBy(f=>Path.GetFileName(f),StringComparer.OrdinalIgnoreCase).ToList();int done=0;
            using(var engines=new ThreadLocal<Engines>(()=>new Engines()))ParallelWork.For(files.Count,workers,cancel,i=>{
                string file=files[i],target=Path.Combine(output,Path.GetFileNameWithoutExtension(file)+".json");var json=new JavaScriptSerializer{MaxJsonLength=50000000};bool reused=false;
                if(cache&&File.Exists(target))try{var prior=json.Deserialize<OcrPage>(File.ReadAllText(target,Encoding.UTF8));reused=prior.width>0&&prior.height>0&&prior.lines!=null;}catch{}
                if(!reused){
                    var storage=StorageFile.GetFileFromPathAsync(Path.GetFullPath(file)).AsTask(cancel).GetAwaiter().GetResult();
                    using(var stream=storage.OpenAsync(FileAccessMode.Read).AsTask(cancel).GetAwaiter().GetResult()) {
                        var decoder=BitmapDecoder.CreateAsync(stream).AsTask(cancel).GetAwaiter().GetResult();
                        using(var bitmap=decoder.GetSoftwareBitmapAsync().AsTask(cancel).GetAwaiter().GetResult()) {
                            if(bitmap.PixelWidth>OcrEngine.MaxImageDimension||bitmap.PixelHeight>OcrEngine.MaxImageDimension)throw new InvalidDataException("Размер изображения превышает предел Windows OCR: "+Path.GetFileName(file));
                            var engine=engines.Value.Russian;if(System.Text.RegularExpressions.Regex.IsMatch(file,@"-CRC-[23]\.png$",System.Text.RegularExpressions.RegexOptions.IgnoreCase)&&engines.Value.English!=null)engine=engines.Value.English;
                            var recognized=engine.RecognizeAsync(bitmap).AsTask(cancel).GetAwaiter().GetResult();
                            var lines=recognized.Lines.Select(l=>new Line{text=l.Text,words=l.Words.Select(w=>new Word{text=w.Text,x=w.BoundingRect.X,y=w.BoundingRect.Y,width=w.BoundingRect.Width,height=w.BoundingRect.Height}).ToList()}).ToList();
                            var page=new OcrPage{language=engine.RecognizerLanguage.LanguageTag,file=Path.GetFileName(file),width=bitmap.PixelWidth,height=bitmap.PixelHeight,text=string.Join("\n",lines.Select(l=>l.text)),lines=lines};
                            cancel.ThrowIfCancellationRequested();string temp=target+".tmp";File.WriteAllText(temp,json.Serialize(page),new UTF8Encoding(false));if(File.Exists(target))File.Delete(target);File.Move(temp,target);
                        }
                    }
                }
                int n=Interlocked.Increment(ref done);progress(n+"/"+files.Count+" "+(reused?"cache ":"")+Path.GetFileName(file));
            });
        }
        public static int RenderPdf(string input,string output,int maxPages,int workers,Action<string> progress,CancellationToken cancel) {
            if(maxPages<1||maxPages>1000)throw new ArgumentOutOfRangeException("maxPages");
            var file=StorageFile.GetFileFromPathAsync(Path.GetFullPath(input)).AsTask(cancel).GetAwaiter().GetResult();PdfDocument document;
            try{document=PdfDocument.LoadFromFileAsync(file).AsTask(cancel).GetAwaiter().GetResult();}catch(Exception ex){throw new InvalidDataException("Не удалось открыть PDF. Проверьте целостность файла и отсутствие защиты паролем.",ex);}
            if(document.IsPasswordProtected)throw new InvalidDataException("PDF защищён паролем. Сохраните доступную для чтения копию и загрузите её.");
            int count=(int)document.PageCount;if(count>maxPages)throw new InvalidDataException("Слишком много страниц PDF: "+count+". Осталось допустимых страниц: "+maxPages);ParallelWork.CheckDiskSpace(output,count*8L*1024*1024);Directory.CreateDirectory(output);int done=0;
            ParallelWork.For(count,workers,cancel,i=>{
                using(var page=document.GetPage((uint)i)){
                    ParallelWork.CheckDiskSpace(output);
                    string target=Path.Combine(output,"page-"+(i+1).ToString("D4")+".png");File.WriteAllBytes(target,new byte[0]);var imageFile=StorageFile.GetFileFromPathAsync(target).AsTask(cancel).GetAwaiter().GetResult();
                    using(var stream=imageFile.OpenAsync(FileAccessMode.ReadWrite).AsTask(cancel).GetAwaiter().GetResult()){
                        double scale=Math.Min(3.125,5000.0/Math.Max(page.Size.Width,page.Size.Height));var options=new PdfPageRenderOptions{DestinationWidth=(uint)Math.Max(1,Math.Round(page.Size.Width*scale)),DestinationHeight=(uint)Math.Max(1,Math.Round(page.Size.Height*scale))};
                        page.RenderToStreamAsync(stream,options).AsTask(cancel).GetAwaiter().GetResult();
                    }
                }
                progress(Interlocked.Increment(ref done)+"/"+count+" PDF");
            });return count;
        }
    }
}
