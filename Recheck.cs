using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace PhotoAudit {
    public static class Recheck {
        static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=50000000};}
        public static string ReportRoot(string path){
            string root=Path.GetFullPath(path);if(File.Exists(root))root=Path.GetDirectoryName(root);
            if(!File.Exists(Path.Combine(root,"Результаты.json")))throw new InvalidDataException("Выберите Отчет.html или Результаты.json из папки завершённой проверки.");
            return root;
        }
        public static List<Photo> Load(string path){
            string root=ReportRoot(path),assets=Path.Combine(root,"assets");var json=Json();
            var prior=json.DeserializeObject(File.ReadAllText(Path.Combine(root,"Результаты.json"),Encoding.UTF8)) as Dictionary<string,object>;
            if(prior==null||!prior.ContainsKey("Photos")||!(prior["Photos"] is object[]))throw new InvalidDataException("В отчёте нет списка сохранённых страниц.");
            var photos=new List<Photo>();var ids=new HashSet<int>();
            foreach(var item in (object[])prior["Photos"]){
                var record=item as Dictionary<string,object>;if(record==null)throw new InvalidDataException("Повреждён список страниц отчёта.");
                var p=json.ConvertToType<Photo>(record);if(p.Id<1||p.Id>1000||!ids.Add(p.Id))throw new InvalidDataException("Некорректный или повторяющийся номер страницы в отчёте.");
                string stem="p"+p.Id.ToString("D4"),file=p.ImageFile;
                if(string.IsNullOrEmpty(file))file=stem+(File.Exists(Path.Combine(assets,stem+"-processed.jpg"))?"-processed.jpg":".jpg");
                if(!Regex.IsMatch(file,"^"+stem+@"(?:-processed)?\.jpg$",RegexOptions.IgnoreCase))throw new InvalidDataException("Некорректное имя сохранённого изображения.");
                p.Image=Path.Combine(assets,file);p.FullImage=null;p.ImageFile=file;
                string ocr=Path.Combine(assets,stem+".json");
                if(!File.Exists(ocr)||!File.Exists(p.Image)||!File.Exists(Path.Combine(assets,stem+".jpg")))throw new InvalidDataException("Нет сохранённого изображения или OCR страницы "+p.Id+". Сохраните всю папку отчёта вместе с assets.");
                p.Ocr=json.Deserialize<OcrPage>(File.ReadAllText(ocr,Encoding.UTF8));if(p.Ocr==null||p.Ocr.width<1||p.Ocr.height<1||p.Ocr.lines==null)throw new InvalidDataException("Повреждено сохранённое OCR страницы "+p.Id);
                photos.Add(p);
            }
            if(photos.Count==0)throw new InvalidDataException("В отчёте нет сохранённых страниц.");return photos.OrderBy(p=>p.Id).ToList();
        }
        public static AuditResult Run(string source,string registry,string destination,IList<int> pageIds,bool repeatOcr,Action<int,string> progress,CancellationToken cancel,int requestedWorkers=0,Action<string,string,int,bool,Action<string>,CancellationToken> recognize=null){
            cancel.ThrowIfCancellationRequested();var clock=System.Diagnostics.Stopwatch.StartNew();string root=ReportRoot(source),dest=Path.GetFullPath(destination);
            if(Directory.Exists(dest)||File.Exists(dest))throw new InvalidOperationException("Повторная проверка сохраняется в новую папку. Выберите другую папку результата.");
            if(dest.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Папка повторной проверки должна находиться вне исходной папки отчёта.");
            var photos=Load(root);var selected=new HashSet<int>(pageIds??new List<int>());
            if(selected.Any(id=>!photos.Any(p=>p.Id==id)))throw new ArgumentException("Выбрана страница, которой нет в сохранённом отчёте.");
            if(repeatOcr&&selected.Count==0)throw new ArgumentException("Выберите хотя бы одну страницу для повторного OCR.");
            if(!repeatOcr)selected.Clear();progress=progress??((p,s)=>{});
            string oldAssets=Path.Combine(root,"assets"),assets=Path.Combine(dest,"assets");
            var files=Directory.GetFiles(oldAssets).Where(f=>Regex.IsMatch(Path.GetFileName(f),@"^p\d{4}(?:[-][А-Яа-яA-Za-z0-9._-]+)?\.(?:jpg|png|json)$",RegexOptions.IgnoreCase)).ToList();
            long bytes=files.Sum(f=>new FileInfo(f).Length);ParallelWork.CheckDiskSpace(dest,bytes+selected.Count*12L*1024*1024);Directory.CreateDirectory(assets);
            progress(0,"Повторное OCR: "+selected.Count+" из "+photos.Count+". Остальные страницы используются из сохранённого отчёта; исходные файлы не открываются.");
            foreach(var file in files){cancel.ThrowIfCancellationRequested();File.Copy(file,Path.Combine(assets,Path.GetFileName(file)));}
            foreach(var p in photos){p.Image=Path.Combine(assets,p.ImageFile);p.ScanImage=Path.Combine(assets,"p"+p.Id.ToString("D4")+"-scan.png");}
            string work=Path.Combine(dest,".recheck-ocr");
            if(repeatOcr){
                string input=Path.Combine(work,"images"),output=Path.Combine(work,"json");Directory.CreateDirectory(input);Directory.CreateDirectory(output);
                var candidates=new Dictionary<int,List<string>>();
                foreach(var p in photos.Where(p=>selected.Contains(p.Id))){
                    string stem="p"+p.Id.ToString("D4");var names=new[]{stem+".jpg",stem+"-processed.jpg",stem+"-scan.png"}.Where(n=>File.Exists(Path.Combine(assets,n))).ToList();candidates[p.Id]=names;
                    foreach(var name in names)File.Copy(Path.Combine(assets,name),Path.Combine(input,name));
                }
                int workers=ParallelWork.Workers(requestedWorkers,192);int done=0,total=candidates.Values.Sum(n=>n.Count);
                if(recognize==null)recognize=NativeWindows.Recognize;recognize(input,output,workers,false,s=>{int n=Interlocked.Increment(ref done);progress(10+(int)(70.0*n/Math.Max(1,total)),"OCR выбранных страниц: "+s);},cancel);
                var json=Json();
                for(int i=0;i<photos.Count;i++)if(selected.Contains(photos[i].Id)){
                    var old=photos[i];string stem="p"+old.Id.ToString("D4");
                    var readings=candidates[old.Id].Select(n=>new {Name=n,Ocr=json.Deserialize<OcrPage>(File.ReadAllText(Path.Combine(output,Path.GetFileNameWithoutExtension(n)+".json"),Encoding.UTF8))}).ToList();
                    if(readings.Any(r=>r.Ocr==null||r.Ocr.lines==null||r.Ocr.width<1||r.Ocr.height<1))throw new InvalidDataException("Некорректный результат повторного OCR страницы "+old.Id);
                    var best=readings.OrderByDescending(r=>AuditEngine.Score(r.Ocr)).ThenByDescending(r=>r.Name.Contains("processed")).First();
                    string color=best.Name.EndsWith("-scan.png",StringComparison.OrdinalIgnoreCase)?(candidates[old.Id].Contains(stem+"-processed.jpg")?stem+"-processed.jpg":stem+".jpg"):best.Name;
                    var p=new Photo{Id=old.Id,Original=old.Original,ManualInventory=old.ManualInventory,Ocr=best.Ocr,Image=Path.Combine(assets,color),ImageFile=color,ProcessingNote=(old.ProcessingNote??"")+"; повторное OCR выбранной страницы"};photos[i]=p;
                    File.WriteAllText(Path.Combine(assets,stem+".json"),json.Serialize(p.Ocr),new UTF8Encoding(false));
                    var scan=readings.FirstOrDefault(r=>r.Name.EndsWith("-scan.png",StringComparison.OrdinalIgnoreCase));if(scan!=null)File.WriteAllText(Path.Combine(assets,stem+"-scan.json"),json.Serialize(scan.Ocr),new UTF8Encoding(false));
                    // Old crop readings and coordinates must not override fresh whole-page OCR.
                    foreach(var file in Directory.GetFiles(assets,stem+"-*").Where(f=>!new[]{stem+"-processed.jpg",stem+"-scan.png",stem+"-scan.json"}.Contains(Path.GetFileName(f))))File.Delete(file);
                }
            }
            cancel.ThrowIfCancellationRequested();var inspect=new HashSet<int>(selected);
            var result=AuditEngine.Analyze(photos,registry,progress,cancel,ParallelWork.Workers(requestedWorkers,96),inspect);
            foreach(var p in photos.Where(p=>inspect.Contains(p.Id))){cancel.ThrowIfCancellationRequested();FormAnalysis.SaveSignatureCrops(p,assets);}
            result.RecheckMode=repeatOcr?"OCR выбранных страниц":"Пересчёт по сохранённому OCR";result.RecheckPages=selected.OrderBy(id=>id).ToList();
            cancel.ThrowIfCancellationRequested();AuditEngine.SaveResult(result,dest,registry);
            File.WriteAllText(Path.Combine(dest,"Повторная_проверка.json"),Json().Serialize(new {Version=typeof(Recheck).Assembly.GetName().Version.ToString(),Mode=result.RecheckMode,Pages=result.RecheckPages,CachedPages=photos.Count-selected.Count,Seconds=clock.Elapsed.TotalSeconds}),new UTF8Encoding(false));
            if(Directory.Exists(work))Directory.Delete(work,true);progress(100,"Готово: повторное OCR "+selected.Count+" страниц; остальных из кэша: "+(photos.Count-selected.Count));return result;
        }
    }
}
