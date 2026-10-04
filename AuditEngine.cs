using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using ReviewMerge;

namespace PhotoAudit {
    public sealed class Word {public string text;public double x,y,width,height;}
    public sealed class Line {public string text;public List<Word> words;}
    public sealed class OcrPage {public string file,text;public int width,height;public List<Line> lines;}
    public sealed class Photo {
        public int Id;public string Original,Image,Kind,Code,Tom,Organization,Book,Chief;public string CRC;public int? Revision,Page,Pages;
        public OcrPage Ocr;public List<Word> Words;public int Ink;public int SignatureRows,RowsWithoutBlue;
    }
    public sealed class InventoryEntry {public string Code,Tom,Book;public int? Revision;public Photo Photo;}
    public sealed class Finding {
        public string Level {get;set;}public string Code {get;set;}public string Topic {get;set;}public string Detail {get;set;}
        public readonly List<int> Photos=new List<int>();
    }
    public sealed class AuditResult {
        public int Images,Volumes,Iul,MainTitles,InventoryPages,CrcMatches,CrcCompared,RevisionMatches,RevisionCompared;
        public string Report,Directory;
        public List<Photo> Photos=new List<Photo>();public List<Finding> Findings=new List<Finding>();public List<InventoryEntry> Inventory=new List<InventoryEntry>();
    }
    public static class AuditEngine {
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer {MaxJsonLength=50000000};
        static string N(string s){return Regex.Replace((s??"").ToUpperInvariant().Replace('Ё','Е').Replace('–','-').Replace('—','-'),@"\s+"," ").Trim();}
        static string Compact(string s){return Regex.Replace(N(s),@"\s+","").Replace("ИЛ0","ИЛО").Replace("ИЛO","ИЛО");}
        static string BookClean(string s){s=Regex.Replace(N(s),@"^КНИГА\s*\d+[.,]?\s*","");return Regex.Replace(s,@"[^А-ЯA-Z0-9]","");}
        static string Digits(string s){return N(s).Replace("З","3").Replace("О","0").Replace("O","0").Replace("Л","1").Replace("L","1").Replace("I","1").Replace("Н","11").Replace("Б","6").Replace("Ч","4");}
        static string FindCode(string s){
            string c=Regex.Replace(Compact(s),@"(И[.]?[ЛJI1Ј][ОO0][34ЗЧ])Л\.","$1.1.");c=Regex.Replace(c,@"(И[.]?[ЛJI1Ј][ОO0][34ЗЧ]\.[0-9]{1,2})Л(?=[^0-9]|$)","$1.1");
            var m=Regex.Match(c,@"(?<p>\d{3}-\d{2}-\d{4})-И[.]?[ЛJI1Ј][ОO0](?<s>[34ЗЧ]\.[0-9ЗОOLIЛНБ]{1,2}\.[0-9ЗОOLIЛНБ]{1,2})(?![0-9ЗОOLIЛНБ])");
            return m.Success?m.Groups["p"].Value+"-ИЛО"+Digits(m.Groups["s"].Value):null;
        }
        static string PageCode(string s){
            var prefix=Regex.Match(Compact(s),@"\d{3}-\d{2}-\d{4}");
            var tom=Regex.Match(N(s),@"\bТОМ\s*(?<v>4\s*\.\s*[34ЗЧ]\s*\.\s*[0-9ЗОOLIЛНБ]{1,2}\s*\.\s*[0-9ЗОOLIЛНБ]{1,2})(?![0-9ЗОOLIЛНБ])");
            if(prefix.Success&&tom.Success)return prefix.Value+"-ИЛО"+Regex.Replace(Digits(tom.Groups["v"].Value),@"\s+","").Substring(2);
            return FindCode(s);
        }
        static string Tom(string code){if(code==null)return null;return "4."+code.Substring(code.LastIndexOf("ИЛО",StringComparison.Ordinal)+3);}
        static List<Word> Words(OcrPage p){return p.lines.SelectMany(l=>l.words??new List<Word>()).ToList();}
        static int Score(OcrPage page){return Regex.Matches(N(page.text),@"ПРОЕКТ|ГЛАВНЫЙ|КНИГА|ТОМ|ДОКУМЕНТ|ОПИСЬ|ИНЖЕНЕР|КОНТРОЛЬ|ЛИСТ|СИСТЕМА|ЗАМЕСТИТЕЛЬ").Count*25+Regex.Matches(page.text??"",@"[А-Яа-я]{4,}").Count+(FindCode(page.text)!=null?120:0);}
        static bool ImageExt(string p){return new []{".jpg",".jpeg",".png",".bmp"}.Contains(Path.GetExtension(p).ToLowerInvariant());}
        static void Jpeg(Bitmap bitmap,string path){var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.MimeType=="image/jpeg");using(var quality=new EncoderParameters(1)){quality.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,92L);bitmap.Save(path,codec,quality);}}
        static void Prepare(string path,string folder,int id){
            using(var image=Image.FromFile(path))using(var raw=new Bitmap(image)){
                if(image.PropertyIdList.Contains(274)){try{int orientation=BitConverter.ToUInt16(image.GetPropertyItem(274).Value,0);if(orientation==3)raw.RotateFlip(RotateFlipType.Rotate180FlipNone);if(orientation==6)raw.RotateFlip(RotateFlipType.Rotate90FlipNone);if(orientation==8)raw.RotateFlip(RotateFlipType.Rotate270FlipNone);}catch{}}
                double scale=Math.Min(1.0,2200.0/Math.Max(raw.Width,raw.Height));
                using(var resized=new Bitmap(Math.Max(1,(int)(raw.Width*scale)),Math.Max(1,(int)(raw.Height*scale)))){
                    using(var g=Graphics.FromImage(resized)){g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(raw,0,0,resized.Width,resized.Height);}
                    if(resized.Width>resized.Height)resized.RotateFlip(RotateFlipType.Rotate90FlipNone);
                    Jpeg(resized,Path.Combine(folder,"p"+id.ToString("D4")+"a.jpg"));resized.RotateFlip(RotateFlipType.Rotate180FlipNone);
                    Jpeg(resized,Path.Combine(folder,"p"+id.ToString("D4")+"b.jpg"));
                }
            }
        }
        static int? FooterNumber(Photo p,string label){
            var marker=p.Words.FirstOrDefault(w=>N(w.text)==label&&w.y>p.Ocr.height*.77&&w.x>p.Ocr.width*.65);
            if(marker==null)return null;double centre=marker.x+marker.width/2;
            var candidates=p.Words.Where(w=>w.y>marker.y+marker.height*.5&&w.y<marker.y+150&&Math.Abs(w.x+w.width/2-centre)<45&&Regex.IsMatch(Digits(w.text),@"^\d{1,2}$")).OrderBy(w=>w.y).ToList();
            return candidates.Count>0?(int?)int.Parse(Digits(candidates[0].text)):null;
        }
        static string BookText(Photo p){
            string s=p.Ocr.text??"";var m=Regex.Match(s,@"Книга\s*\d+[.,]?\s*(?<name>[\s\S]*?)(?=\r?\n\s*(?:\d{3}\s*[-–—]|Том\s*\d|Главный\s|Заместител)|$)",RegexOptions.IgnoreCase);
            if(!m.Success)return null;string value=m.Groups["name"].Value;
            if(p.Kind=="ИУЛ")value=Regex.Split(value,@"\b(?:Том|CRC32|Наименование файла|Алгоритм)\b",RegexOptions.IgnoreCase)[0];
            value=Regex.Replace(value,@"\b37-26\b","");return Regex.Replace(value,@"\s+"," ").Trim(' ','.');
        }
        static string Chief(Photo p){
            var candidates=p.Words.Where(w=>N(w.text).EndsWith("ЛАВНЫЙ")&&w.x<p.Ocr.width*.27&&w.y>p.Ocr.height*(p.Kind=="ИУЛ"?.14:.65)).OrderBy(w=>w.y);
            foreach(var w in candidates){
                var role=p.Words.Where(t=>t.x<p.Ocr.width*.29&&t.y>=w.y-12&&t.y<w.y+(p.Kind=="ИУЛ"?110:55)).ToList();
                if(!role.Any(t=>N(t.text).StartsWith("ИНЖЕНЕР")) || role.Any(t=>N(t.text).StartsWith("ПРОЕКТА")))continue;
                double xmin=p.Kind=="ИУЛ"?p.Ocr.width*.24:p.Ocr.width*.62,xmax=p.Kind=="ИУЛ"?p.Ocr.width*.59:p.Ocr.width;
                var names=p.Words.Where(t=>t.x>=xmin&&t.x<xmax&&Math.Abs(t.y-w.y)<28&&Regex.IsMatch(t.text??"",@"^[А-Яа-яЁё]{4,}$")).OrderBy(t=>t.x).ThenBy(t=>t.y).ToList();
                if(names.Count>0)return N(names[0].text);
            }return null;
        }
        static int Blue(Bitmap im,Rectangle area){
            area=Rectangle.Intersect(area,new Rectangle(0,0,im.Width,im.Height));int count=0;
            for(int y=area.Top;y<area.Bottom;y+=2)for(int x=area.Left;x<area.Right;x+=2){Color c=im.GetPixel(x,y);if(c.B>c.R+17 && c.B>c.G+8 && c.B>55)count++;}return count;
        }
        static void Visual(Photo p){
            using(var im=new Bitmap(p.Image)){
                p.Ink=Blue(im,new Rectangle(0,(int)(im.Height*.65),im.Width,(int)(im.Height*.35)));
                if(p.Kind!="ИУЛ")return;
                var ys=p.Words.Where(w=>w.x<im.Width*.28&&w.y>im.Height*.04&&w.y<im.Height*.82&&Regex.IsMatch(N(w.text),@"^(ГЛАВНЫЙ|ВЕДУЩИЙ|ЗАМЕСТИТЕЛЬ|НАЧАЛЬНИК|НОРМОКОНТРОЛЬ|ИНЖЕНЕР|РУКОВОДИТЕЛЬ)$")).Select(w=>w.y).OrderBy(y=>y).ToList();
                var distinct=new List<double>();foreach(var y in ys)if(distinct.Count==0||y-distinct.Last()>65)distinct.Add(y);
                for(int i=0;i<distinct.Count;i++){int top=(int)distinct[i]-20;int bottom=(int)(i+1<distinct.Count?distinct[i+1]-10:Math.Min(im.Height*.86,distinct[i]+145));if(bottom<=top)continue;
                    p.SignatureRows++;int blue=Blue(im,new Rectangle((int)(im.Width*.45),top,(int)(im.Width*.41),bottom-top));if(blue<12)p.RowsWithoutBlue++;
                }
            }
        }
        static Photo Parse(Photo p){
            string n=N(p.Ocr.text),c=Compact(p.Ocr.text);p.Words=Words(p.Ocr);p.Code=PageCode(p.Ocr.text);p.Tom=Tom(p.Code);
            if((n.Substring(0,Math.Min(300,n.Length)).Contains("ОПИСЬ")||c.Contains("ШИФРТОМА"))&&Regex.Matches(c,@"ИЛ[ОO0][34ЗЧ]\.").Count>1){p.Kind="Опись";p.Code=null;p.Tom=null;}
            else if(c.Contains("УДОСТОВЕРЯЮЩ")||c.Contains("УДОСТОВЕРЯЮШ")||((n.Contains("КОНТРОЛЬНАЯ")||n.Contains("CRC32"))&&n.Contains("ЛИСТ"))||c.Contains("-УЛ"))p.Kind="ИУЛ";
            else if(n.Contains("ФРАГМЕНТ"))p.Kind="Титул фрагмента";
            else if(n.Contains("КНИГА")&&p.Code!=null)p.Kind="Титул";else p.Kind="Не определено";
            string top=n.Substring(0,Math.Min(n.Length,230));p.Organization=top.Contains("ЛЕНГИПРОТРАНС")?"Ленгипротранс":top.Contains("РОСЖЕЛДОР")||top.Contains("ГИПРОТРАНСПУТЬ")?"Росжелдорпроект":top.Contains("ЖЕЛДОРПРОЕКТ")?"Желдорпроект":"Не определено";
            p.Book=BookText(p);p.Chief=Chief(p);
            var crc=Regex.Match(n.Replace('С','C').Replace('Р','R').Replace('В','B').Replace('А','A').Replace('Е','E'),@"CRC\s*32[^0-9A-F]{0,6}(?<sum>[0-9A-FОOIL]{8})(?![0-9A-FОOIL])");if(crc.Success)p.CRC=Digits(crc.Groups["sum"].Value);
            if(p.CRC==null){var marker=p.Words.FirstOrDefault(w=>Compact(w.text).Replace('С','C').Replace('Р','R').StartsWith("CRC32"));if(marker!=null){var candidate=p.Words.Where(w=>Math.Abs(w.x-marker.x)<120&&w.y>=marker.y-8&&w.y<marker.y+80).OrderBy(w=>w.y).Select(w=>Digits(w.text).Replace('С','C').Replace('В','B').Replace('А','A').Replace('Е','E').Trim(',',':',';','•')).FirstOrDefault(s=>Regex.IsMatch(s,@"^[0-9A-F]{8}$"));p.CRC=candidate;}}
            if(p.Kind=="ИУЛ"){
                p.Page=FooterNumber(p,"ЛИСТ");p.Pages=FooterNumber(p,"ЛИСТОВ");
                var rev=p.Words.Where(w=>w.x>p.Ocr.width*.77&&w.y>p.Ocr.height*.15&&w.y<p.Ocr.height*.32&&Regex.IsMatch(Digits(w.text),@"^\d{1,2}$")).OrderBy(w=>w.y).FirstOrDefault();
                if(rev!=null)p.Revision=int.Parse(Digits(rev.text));
            }
            Visual(p);return p;
        }
        static List<InventoryEntry> ParseInventory(Photo p){
            var result=new List<InventoryEntry>();var volumes=p.Words.Where(w=>w.x<p.Ocr.width*.2&&Regex.IsMatch(Digits(w.text),@"^4\.[34]\." )).OrderBy(w=>w.y).ToList();
            foreach(var v in volumes){
                string volume=Digits(v.text);if(!Regex.IsMatch(volume,@"^4\.[34]\.[0-9]{1,2}\.[0-9]{1,2}$"))volume=Regex.Replace(Digits(string.Join("",p.Words.Where(w=>w.x>=v.x-1&&w.x<p.Ocr.width*.19&&Math.Abs(w.y-v.y)<12).OrderBy(w=>w.x).Select(w=>w.text))),@"[^0-9.]","");
                var prefix=Regex.Match(Compact(p.Ocr.text),@"\d{3}-\d{2}-\d{4}");
                var suffixWord=p.Words.Where(w=>w.x>p.Ocr.width*.145&&w.x<p.Ocr.width*.4&&Math.Abs(w.y-v.y)<35&&Regex.IsMatch(Compact(w.text),@"^ИЛ[ОO0]" )).OrderBy(w=>Math.Abs(w.y-v.y)).FirstOrDefault();
                string code=prefix.Success&&suffixWord!=null?FindCode(prefix.Value+"-"+suffixWord.text):null;
                if(code!=null)volume=Tom(code);
                if(!Regex.IsMatch(volume,@"^4\.[34]\.[0-9]{1,2}\.[0-9]{1,2}$"))continue;
                if(code==null&&prefix.Success)code=prefix.Value+"-ИЛО"+volume.Substring(2);
                var bookWord=p.Words.Where(w=>N(w.text).StartsWith("КНИГА")&&w.x>p.Ocr.width*.34&&Math.Abs(w.y-v.y)<65).OrderBy(w=>Math.Abs(w.y-v.y)).FirstOrDefault();
                string book=null;if(bookWord!=null){double limit=p.Ocr.height;var next=p.Words.Where(w=>N(w.text).StartsWith("КНИГА")&&w.x>p.Ocr.width*.34&&w.x<p.Ocr.width*.76&&w.y>bookWord.y+20).OrderBy(w=>w.y).FirstOrDefault();if(next!=null)limit=next.y-4;
                    var stop=p.Words.Where(w=>w.x>p.Ocr.width*.34&&w.x<p.Ocr.width*.75&&w.y>bookWord.y+15&&(N(w.text).StartsWith("ЧАСТЬ")||N(w.text).StartsWith("ПОДРАЗДЕЛ"))).OrderBy(w=>w.y).FirstOrDefault();if(stop!=null)limit=Math.Min(limit,stop.y-4);
                    var anchorLine=p.Ocr.lines.First(l=>l.words.Contains(bookWord));double start=anchorLine.words.Where(w=>w.x>=bookWord.x-2&&w.x<p.Ocr.width*.72).Average(w=>w.y+w.height/2);
                    string line=string.Join(" ",p.Ocr.lines.Select(l=>l.words.Where(w=>w.x>=bookWord.x-2&&w.x<p.Ocr.width*.72).ToList()).Where(ws=>ws.Count>0&&ws.Average(w=>w.y+w.height/2)>=start-8&&ws.Average(w=>w.y)<limit).OrderBy(ws=>ws.Average(w=>w.y+w.height/2)).Select(ws=>string.Join(" ",ws.OrderBy(w=>w.x).Select(w=>w.text))));book=Regex.Replace(line,@"^Книга\s*[0-9ЗзбБ]+[.,]?\s*","",RegexOptions.IgnoreCase).Trim();}
                var marker=p.Words.Where(w=>N(w.text).StartsWith("ИЗМ")&&w.x>p.Ocr.width*.72&&Math.Abs(w.y-v.y)<60).OrderBy(w=>Math.Abs(w.y-v.y)).FirstOrDefault();int? revision=null;
                if(marker!=null){var num=p.Words.Where(w=>w.x>marker.x&&w.x<marker.x+145&&Math.Abs(w.y-marker.y)<22&&Regex.IsMatch(w.text??"",@"^\d{1,2}$")).OrderBy(w=>w.x).FirstOrDefault();if(num!=null)revision=int.Parse(num.text);}
                if(code!=null)result.Add(new InventoryEntry {Code=code,Tom=volume,Book=book,Revision=revision,Photo=p});
            }return result;
        }
        static int Distance(string a,string b){int[] prev=Enumerable.Range(0,b.Length+1).ToArray();for(int i=1;i<=a.Length;i++){int[] cur=new int[b.Length+1];cur[0]=i;for(int j=1;j<=b.Length;j++)cur[j]=Math.Min(Math.Min(cur[j-1]+1,prev[j]+1),prev[j-1]+(a[i-1]==b[j-1]?0:1));prev=cur;}return prev[b.Length];}
        static bool Different(string a,string b){a=BookClean(a);b=BookClean(b);if(a.Length<4||b.Length<4)return false;return (double)Distance(a,b)/Math.Max(a.Length,b.Length)>.28;}
        static void Add(AuditResult r,string level,string code,string topic,string detail,params Photo[] photos){var f=new Finding {Level=level,Code=code??"Без шифра",Topic=topic,Detail=detail};foreach(var p in photos.Where(p=>p!=null))f.Photos.Add(p.Id);r.Findings.Add(f);}
        public static AuditResult Analyze(List<Photo> photos,string registry){
            var r=new AuditResult {Images=photos.Count,Photos=photos};
            foreach(var p in photos){Parse(p);if(p.Kind=="Опись")r.Inventory.AddRange(ParseInventory(p));}
            r.Inventory=r.Inventory.GroupBy(i=>i.Code).Select(g=>g.First()).ToList();r.InventoryPages=photos.Count(p=>p.Kind=="Опись");r.MainTitles=photos.Count(p=>p.Kind=="Титул");r.Iul=photos.Count(p=>p.Kind=="ИУЛ");
            var records=new Dictionary<string,List<SourceRow>>();
            if(!string.IsNullOrWhiteSpace(registry))foreach(var row in XlsxReader.Read(registry,"Все загруженные файлы").Rows){string code=FindCode(XlsxReader.Text(row.Values[2]));if(code==null)continue;if(!records.ContainsKey(code))records[code]=new List<SourceRow>();records[code].Add(row);}
            var groups=photos.Where(p=>p.Code!=null&&p.Kind!="Опись").GroupBy(p=>p.Code).ToList();r.Volumes=groups.Count;
            foreach(var inv in r.Inventory)if(!groups.Any(g=>g.Key==inv.Code))Add(r,"Проверить",inv.Code,"Нет фотографий тома","Том указан в распознанной описи, но его титулы и ИУЛ не найдены среди загруженных изображений.",inv.Photo);
            foreach(var group in groups){
                var title=group.Where(p=>p.Kind=="Титул").ToList();var iul=group.Where(p=>p.Kind=="ИУЛ").ToList();var inv=r.Inventory.FirstOrDefault(i=>i.Code==group.Key);
                string expected=inv!=null&&!string.IsNullOrWhiteSpace(inv.Book)?inv.Book:title.Select(p=>p.Book).FirstOrDefault(b=>!string.IsNullOrWhiteSpace(b));
                var badNames=iul.Where(p=>p.Book!=null&&expected!=null&&Different(expected,p.Book)).ToList();
                if(badNames.Count>0)Add(r,"Расхождение",group.Key,"Название книги в ИУЛ","Опись / титул: «"+expected+"». ИУЛ: «"+string.Join("»; «",badNames.Select(p=>p.Book).Distinct())+"». Сравните фрагменты фотографий.",badNames.Concat(inv!=null?new []{inv.Photo}:title.Take(1)).ToArray());
                foreach(var p in title.Where(p=>expected!=null&&p.Book!=null&&Different(expected,p.Book)))Add(r,"Проверить",group.Key,"Название на титуле","Опись: «"+expected+"». Титул: «"+p.Book+"». Возможна ошибка OCR.",p,inv==null?null:inv.Photo);
                var chiefs=title.Where(p=>p.Organization=="Ленгипротранс"&&p.Chief!=null).ToList();
                foreach(var t in chiefs)foreach(var p in iul.Where(p=>p.Organization==t.Organization&&p.Chief!=null&&(p.Book!=null||p.CRC!=null)))if(Different(t.Chief,p.Chief))Add(r,"Расхождение",group.Key,"Главный инженер: титул / ИУЛ","Титул: "+t.Chief+". ИУЛ: "+p.Chief+". Подписанты распознаны по строке «Главный инженер» на основной странице ИУЛ.",t,p);
                foreach(var p in iul){
                    List<SourceRow> candidates; if(records.TryGetValue(group.Key,out candidates)){
                        var sums=candidates.Select(x=>XlsxReader.Normal(x.Values[7])).Where(s=>s!="").Distinct().ToList();if(p.CRC!=null){r.CrcCompared++;
                        if(sums.Contains(p.CRC))r.CrcMatches++;else Add(r,"Проверить",group.Key,"CRC32 не совпадает","ИУЛ: "+p.CRC+"; Excel: "+string.Join(", ",sums)+". Подтвердите чтение восьми символов на снимке.",p);}
                    }
                    if(inv!=null&&inv.Revision.HasValue&&p.Revision.HasValue){r.RevisionCompared++;if(inv.Revision==p.Revision)r.RevisionMatches++;else Add(r,"Расхождение",group.Key,"Номер изменения","Опись: "+inv.Revision+"; ИУЛ: "+p.Revision+". Номер разрешения не используется как номер изменения.",inv.Photo,p);}
                }
                var distinctCrc=iul.Where(p=>p.CRC!=null).Select(p=>p.CRC).Distinct().ToList();if(distinctCrc.Count>1)Add(r,"Проверить",group.Key,"Разные CRC32 на листах ИУЛ",string.Join(" / ",distinctCrc)+". Возможна ошибка OCR.",iul.Where(p=>p.CRC!=null).ToArray());
                if(iul.Count==0)Add(r,"Проверить",group.Key,"ИУЛ не найден","В группе есть титул, но не найден ИУЛ.",title.ToArray());
                if(!title.Any(p=>p.Organization=="Росжелдорпроект"))Add(r,"Проверить",group.Key,"Титул Росжелдорпроекта не найден","Не найден отдельный основной титул изготовителя. Возможна неполная загрузка или ошибка распознавания логотипа.",group.First());
                var ros=iul.FirstOrDefault(p=>p.Organization=="Росжелдорпроект"&&(p.Page==1||p.CRC!=null));
                if(ros!=null&&!ros.Words.Any(w=>N(w.text).Trim(',','.')=="ПРОЕКТА"&&w.x<ros.Ocr.width*.3&&w.y>ros.Ocr.height*.55)&&title.Any(p=>Compact(p.Ocr.text).Contains("ИНЖЕНЕРПР")))
                    Add(r,"Проверить",group.Key,"Состав подписантов ИУЛ","В ИУЛ Росжелдорпроекта не распознана строка главного инженера проекта, которая есть на титуле. Проверьте подписную таблицу.",ros,title.First());
                int? expectedPages=iul.Select(p=>p.Pages).FirstOrDefault(n=>n.HasValue);if(expectedPages.HasValue){var found=iul.Where(p=>p.Page.HasValue).Select(p=>p.Page.Value).Distinct().ToList();var missing=Enumerable.Range(1,expectedPages.Value).Where(n=>!found.Contains(n)).ToList();if(missing.Count>0)Add(r,"Проверить",group.Key,"Не подтверждена комплектность ИУЛ","По распознанной нумерации не найдены листы: "+string.Join(", ",missing)+". Нумерация могла не распознаться.",iul.ToArray());}
            }
            foreach(var p in photos){
                if(p.Kind=="Титул"&&p.Ink<35)Add(r,"Проверить",p.Code,"Печать / подписи на титуле","В нижней части титула не найдены выраженные синие отметки. Просмотрите печать и подписи; чёрные подписи этот признак не подтверждает.",p);
                if(p.Kind=="Титул фрагмента"&&p.Ink<35)Add(r,"Наблюдение",p.Code,"Титул фрагмента без синих отметок","На дополнительном титуле фрагмента не обнаружены синие печать и подписи. Уточните необходимость его отдельного подписания.",p);
                if(p.Kind=="ИУЛ"&&p.RowsWithoutBlue>0)Add(r,"Проверить",p.Code,"Подписи в ИУЛ: визуальная проверка","В "+p.RowsWithoutBlue+" из "+p.SignatureRows+" распознанных строк не обнаружена синяя рукописная отметка. Это предварительный признак: возможны чёрная подпись, блики или неверные границы строки.",p);
                if(p.Kind=="Не определено"||p.Kind!="Опись"&&p.Code==null)Add(r,"Проверить",p.Code,"Документ не распознан уверенно","Не удалось определить вид листа или шифр. Снимок сохранён для просмотра.",p);
            }
            return r;
        }
        public static AuditResult Run(IList<string> inputs,string registry,string destination,Action<int,string> progress,CancellationToken cancel){
            string root=Path.GetFullPath(destination);Directory.CreateDirectory(root);string work=Path.Combine(root,".ocr-work");Directory.CreateDirectory(work);string candidates=Path.Combine(work,"images"),jsons=Path.Combine(work,"json");Directory.CreateDirectory(candidates);Directory.CreateDirectory(jsons);
            var sources=new List<Tuple<string,string>>();long extracted=0;
            foreach(string input in inputs){cancel.ThrowIfCancellationRequested();
                if(Directory.Exists(input)){foreach(var file in Directory.EnumerateFiles(input,"*",SearchOption.AllDirectories).Where(ImageExt).OrderBy(f=>f,StringComparer.OrdinalIgnoreCase))sources.Add(Tuple.Create(file,file));}
                else if(Path.GetExtension(input).Equals(".zip",StringComparison.OrdinalIgnoreCase))using(var fs=File.OpenRead(input))using(var zip=new ZipArchive(fs,ZipArchiveMode.Read))foreach(var e in zip.Entries.Where(e=>ImageExt(e.FullName)).OrderBy(e=>e.FullName,StringComparer.OrdinalIgnoreCase)){
                    extracted+=e.Length;if(e.Length>80000000||extracted>2000000000)throw new InvalidDataException("Архив изображений превышает допустимый размер.");string path=Path.Combine(work,"original"+sources.Count+Path.GetExtension(e.FullName));using(var es=e.Open())using(var os=File.Create(path))es.CopyTo(os);sources.Add(Tuple.Create(path,Path.GetFileName(input)+" / "+e.FullName));
                }else if(ImageExt(input))sources.Add(Tuple.Create(Path.GetFullPath(input),Path.GetFileName(input)));
            }
            if(sources.Count==0)throw new InvalidOperationException("Не найдены изображения JPG, PNG или BMP.");
            if(sources.Count>1000)throw new InvalidOperationException("Одна проверка поддерживает до 1000 изображений.");
            for(int i=0;i<sources.Count;i++){cancel.ThrowIfCancellationRequested();Prepare(sources[i].Item1,candidates,i+1);progress((int)(15.0*(i+1)/sources.Count),"Подготовлено "+(i+1)+" / "+sources.Count+" изображений");}
            string powershell=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"System32","WindowsPowerShell","v1.0","powershell.exe");
            string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Recognize.ps1");
            if(!File.Exists(script))throw new FileNotFoundException("Рядом с программой не найден Recognize.ps1.");
            var info=new ProcessStartInfo(powershell,"-NoProfile -NonInteractive -ExecutionPolicy Bypass -File \""+script+"\" -InputDirectory \""+candidates+"\" -OutputDirectory \""+jsons+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
            var errors=new StringBuilder();
            using(var process=Process.Start(info)){
                process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null)lock(errors)errors.AppendLine(e.Data);};process.BeginErrorReadLine();
                string line;int done=0;while((line=process.StandardOutput.ReadLine())!=null){if(cancel.IsCancellationRequested){try{process.Kill();}catch{}cancel.ThrowIfCancellationRequested();}done++;progress(15+(int)(65.0*done/(sources.Count*2)),"OCR: "+line);}
                process.WaitForExit();if(process.ExitCode!=0)throw new InvalidOperationException("Ошибка распознавания Windows: "+errors);
            }
            var photos=new List<Photo>();string assets=Path.Combine(root,"assets");Directory.CreateDirectory(assets);
            for(int i=0;i<sources.Count;i++){
                cancel.ThrowIfCancellationRequested();string stem="p"+(i+1).ToString("D4");var pa=Json.Deserialize<OcrPage>(File.ReadAllText(Path.Combine(jsons,stem+"a.json"),Encoding.UTF8));var pb=Json.Deserialize<OcrPage>(File.ReadAllText(Path.Combine(jsons,stem+"b.json"),Encoding.UTF8));var best=Score(pa)>=Score(pb)?pa:pb;
                string image=Path.Combine(assets,stem+".jpg");File.Copy(Path.Combine(candidates,best.file),image,true);File.Copy(Path.Combine(jsons,Path.GetFileNameWithoutExtension(best.file)+".json"),Path.Combine(assets,stem+".json"),true);
                photos.Add(new Photo {Id=i+1,Original=sources[i].Item2,Image=image,Ocr=best});
            }
            progress(83,"Сопоставление описи, титулов, ИУЛ и Excel");var result=Analyze(photos,registry);SaveResult(result,root,registry);
            cancel.ThrowIfCancellationRequested();if(Path.GetFullPath(work).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(work)==".ocr-work")Directory.Delete(work,true);
            progress(100,"Готово: "+result.Images+" изображений, "+result.Findings.Count+" пунктов для просмотра");return result;
        }
        public static AuditResult Reanalyze(string root,string registry){
            var prior=(Dictionary<string,object>)Json.DeserializeObject(File.ReadAllText(Path.Combine(root,"Результаты.json"),Encoding.UTF8));
            var photos=new List<Photo>();
            foreach(var item in (object[])prior["Photos"]){var record=(Dictionary<string,object>)item;int id=Convert.ToInt32(record["Id"]);string stem="p"+id.ToString("D4");photos.Add(new Photo {Id=id,Original=(string)record["Original"],Image=Path.Combine(root,"assets",stem+".jpg"),Ocr=Json.Deserialize<OcrPage>(File.ReadAllText(Path.Combine(root,"assets",stem+".json"),Encoding.UTF8))});}
            var result=Analyze(photos,registry);SaveResult(result,root,registry);return result;
        }
        static void SaveResult(AuditResult result,string root,string registry){
            result.Directory=root;result.Report=Path.Combine(root,"Отчет.html");WriteReport(result,registry);
            File.WriteAllText(Path.Combine(root,"Результаты.json"),Json.Serialize(new {result.Images,result.Volumes,result.Iul,result.MainTitles,result.InventoryPages,result.CrcMatches,result.CrcCompared,result.RevisionMatches,result.RevisionCompared,Inventory=result.Inventory.Select(i=>new{i.Code,i.Tom,i.Book,i.Revision,Photo=i.Photo.Id}),Findings=result.Findings,Photos=result.Photos.Select(p=>new{p.Id,p.Original,p.Kind,p.Code,p.Tom,p.Book,p.Chief,p.Organization,p.CRC,p.Revision,p.Page,p.Pages,p.Ink,p.SignatureRows,p.RowsWithoutBlue})}),new UTF8Encoding(false));
        }
        static string H(object s){return HttpUtility.HtmlEncode(s==null?"":Convert.ToString(s,Inv));}
        static string ImageHtml(Photo p){return "<figure><a href='assets/p"+p.Id.ToString("D4")+".jpg' target='_blank'><img loading='lazy' src='assets/p"+p.Id.ToString("D4")+".jpg'></a><figcaption>"+H(p.Original)+"<br>"+H(p.Kind)+"; "+H(p.Code)+"</figcaption></figure>";}
        static void WriteReport(AuditResult r,string registry){
            var b=new StringBuilder("<!doctype html><html lang='ru'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Автопроверка документов</title><style>body{font:16px/1.5 Segoe UI,Arial;margin:0;background:#f3f5f7;color:#1c2e41}main{max-width:1260px;margin:32px auto;padding:0 24px}h1{font-size:30px}h2{font-size:22px}section,article{background:white;border:1px solid #dce3ea;border-radius:8px;padding:20px;margin:16px 0}small,.muted{color:#5a6b7c}.metrics{display:flex;gap:20px;flex-wrap:wrap}.metrics span{background:#eaf0f5;padding:12px 18px;border-radius:6px}table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:10px;text-align:left;border-bottom:1px solid #dce3ea;vertical-align:top}th{background:#234564;color:white}.proof{display:flex;gap:14px;flex-wrap:wrap}figure{margin:10px 0;width:280px}img{width:100%;border:1px solid #dce3ea}figcaption{font-size:12px;word-break:break-word}.level{font-weight:bold;color:#a03d22}button,select{font:inherit;padding:8px 12px}details summary{cursor:pointer;color:#234564}pre{white-space:pre-wrap;font-size:13px}a{color:#235e7a}@media print{body{background:white}button,select{display:none}}</style><main>");
            b.Append("<h1>Автопроверка документов</h1><p>Результат распознавания и предварительной сверки. Каждый пункт сопровождается исходными снимками.</p><div class='metrics'><span>Изображений: <b>"+r.Images+"</b></span><span>Томов распознано: <b>"+r.Volumes+"</b></span><span>ИУЛ: <b>"+r.Iul+"</b></span><span>Пунктов для просмотра: <b>"+r.Findings.Count+"</b></span></div>");
            b.Append("<section><h2>Результат сверки</h2><p>CRC32: совпало "+r.CrcMatches+" из "+r.CrcCompared+" распознанных значений на листах ИУЛ. Изменения: совпало "+r.RevisionMatches+" из "+r.RevisionCompared+" сравнений с описью.</p><p class='muted'>Контрольная сумма сравнивается по напечатанному тексту и реестру, без пересчёта исходных PDF. Синий цвет — предварительный признак рукописной отметки, не подтверждение подлинности подписи или отдельной печати. Чёрные подписи, блики и ошибки OCR требуют просмотра.</p><p>Реестр: "+H(registry)+"</p></section>");
            b.Append("<h2>Возможные ошибки и неполные данные</h2><label>Показать: <select id='filter'><option value='Все'>Все пункты</option><option>Расхождение</option><option>Проверить</option><option>Наблюдение</option></select></label>");
            int n=0;foreach(var f in r.Findings.OrderBy(f=>f.Level=="Расхождение"?0:f.Level=="Проверить"?1:2)){n++;b.Append("<article data-level='"+H(f.Level)+"'><span class='level'>"+H(f.Level)+"</span><h2>"+n+". "+H(f.Code)+" — "+H(f.Topic)+"</h2><p>"+H(f.Detail)+"</p><details><summary>Открыть фотографии</summary><div class='proof'>");foreach(int id in f.Photos.Distinct())b.Append(ImageHtml(r.Photos.First(p=>p.Id==id)));b.Append("</div></details></article>");}
            b.Append("<section><h2>Распознанная опись</h2><table><tr><th>Том</th><th>Шифр</th><th>Название книги</th><th>Изм.</th></tr>");foreach(var i in r.Inventory)b.Append("<tr><td>"+H(i.Tom)+"</td><td>"+H(i.Code)+"</td><td>"+H(i.Book)+"</td><td>"+H(i.Revision)+"</td></tr>");b.Append("</table></section>");
            b.Append("<section><h2>Все изображения и распознанные поля</h2>");foreach(var p in r.Photos){b.Append("<details><summary>"+p.Id+". "+H(p.Original)+" — "+H(p.Kind)+" — "+H(p.Code)+"</summary><p>Книга: "+H(p.Book)+". Главный инженер: "+H(p.Chief)+". CRC32: "+H(p.CRC)+". Изм.: "+H(p.Revision)+". Лист: "+H(p.Page)+" / "+H(p.Pages)+". Строк подписей: "+p.SignatureRows+"; без синей отметки: "+p.RowsWithoutBlue+".</p><div class='proof'>"+ImageHtml(p)+"</div><details><summary>Текст OCR</summary><pre>"+H(p.Ocr.text)+"</pre></details></details>");}b.Append("</section><script>document.getElementById('filter').onchange=function(){document.querySelectorAll('article[data-level]').forEach(a=>a.hidden=this.value!=='Все'&&a.dataset.level!==this.value)}</script></main></html>");File.WriteAllText(r.Report,b.ToString(),new UTF8Encoding(false));
        }
    }
}
