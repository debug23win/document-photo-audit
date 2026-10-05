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
using System.Security.Cryptography;
using System.Threading;
using System.Web;
using System.Web.Script.Serialization;
using ReviewMerge;

namespace PhotoAudit {
    public sealed class Word {public string text;public double x,y,width,height;}
    public sealed class Line {public string text;public List<Word> words;}
    public sealed class OcrPage {public string file,text,language;public int width,height;public List<Line> lines;}
    public sealed class Photo {
        public int Id;public string Original,Image,Kind,Code,Tom,Organization,Book,Chief;public string CRC;public int? Revision,Page,Pages;public bool ManualInventory;public bool? InventoryOverride;
        public OcrPage Ocr,ScanOcr;public string ScanImage;public List<Word> Words;public int Ink;public int SignatureRows,RowsWithoutBlue;
        public string FullImage,ImageFile,ProcessingNote,SectionText,KindHint,CodeHint,OrganizationHint;public List<Word> DetailWords;
        public List<Reading> Readings=new List<Reading>();public List<SignatureCheck> Signatures=new List<SignatureCheck>();
        public Dictionary<string,string> Sections=new Dictionary<string,string>();public SealCheck Seal;
    }
    public sealed class InventoryEntry {public string Code,Tom,Book,DocumentationKind;public int? Revision,Box;public Photo Photo;public Dictionary<string,string> Sections;public double AnchorY;}
    public sealed class Finding {
        public string Level {get;set;}public string Code {get;set;}public string Topic {get;set;}public string Detail {get;set;}
        public readonly List<int> Photos=new List<int>();
    }
    public sealed class AuditResult {
        public int Images,Volumes,Iul,MainTitles,InventoryPages,CrcMatches,CrcCompared,RevisionMatches,RevisionCompared;
        public string Report,Directory;
        public bool InventoryOnly;
        public string RecheckMode;public List<int> RecheckPages=new List<int>();
        public List<Photo> Photos=new List<Photo>();public List<Finding> Findings=new List<Finding>();public List<InventoryEntry> Inventory=new List<InventoryEntry>();
    }
    public sealed class InventoryPreview {
        public int Images,AutomaticPages,ManualPages;
        public List<InventoryPreviewPage> Items=new List<InventoryPreviewPage>();
        public int Pages {get{return AutomaticPages+ManualPages;}}
        public void Refresh(){Images=Items.Count;ManualPages=Items.Count(p=>p.Selected&&(p.Manual||!p.Automatic));AutomaticPages=Items.Count(p=>p.Selected&&p.Automatic&&!p.Manual);}
        public void Apply(IList<Photo> photos){
            if(Items.Count!=photos.Count||Items.Select(p=>p.Id).Distinct().Count()!=photos.Count||Items.Any(p=>!photos.Any(v=>v.Id==p.Id)))throw new InvalidDataException("Список выбранных страниц описи не соответствует загруженным страницам.");
            var choices=Items.ToDictionary(p=>p.Id);foreach(var p in photos){var choice=choices[p.Id];p.InventoryOverride=choice.Selected;p.ManualInventory=choice.Selected&&(choice.Manual||!choice.Automatic);}Refresh();
        }
    }
    public sealed class InventoryPreviewPage {public int Id;public string Original,Image;public bool Automatic,Manual,Selected;}
    public sealed class InventoryReviewGate:IDisposable {
        readonly ManualResetEventSlim ready=new ManualResetEventSlim(false);bool proceed;
        public void Complete(bool start){proceed=start;ready.Set();}
        public bool Wait(CancellationToken cancel){ready.Wait(cancel);return proceed;}
        public void Dispose(){ready.Dispose();}
    }
    public static class AuditEngine {
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        [ThreadStatic]static JavaScriptSerializer localJson;
        static JavaScriptSerializer Json {get{return localJson??(localJson=new JavaScriptSerializer{MaxJsonLength=50000000});}}
        static string N(string s){return Regex.Replace((s??"").ToUpperInvariant().Replace('Ё','Е').Replace('–','-').Replace('—','-'),@"\s+"," ").Trim();}
        static string Compact(string s){return Regex.Replace(N(s),@"\s+","").Replace("ИЛ0","ИЛО").Replace("ИЛO","ИЛО");}
        static string BookClean(string s){s=Regex.Replace(N(s),@"^КНИГА\s*\d+[.,]?\s*","");return Regex.Replace(s,@"[^А-ЯA-Z0-9]","");}
        static string Digits(string s){return N(s).Replace("З","3").Replace("О","0").Replace("O","0").Replace("Л","1").Replace("L","1").Replace("I","1").Replace("Н","11").Replace("Б","6").Replace("Ч","4");}
        const string PrefixPattern=@"[0-9ОOILЛЗ]{3}-[0-9ОOILЛЗ]{2}-[0-9ОOILЛЗ]{4}";
        static string CodeText(string s){
            string c=Compact(s);c=Regex.Replace(c,PrefixPattern,m=>Digits(m.Value));c=Regex.Replace(c,@"[ТT][КK][РP]","ТКР");
            c=Regex.Replace(c,@"(ТКР[0-9ЗОOLIЛНБ]{1,2})Л\.","$1.1.");return c;
        }
        static string FindCode(string s){
            if(!(s??"").Contains("\n"))return LegacyCode(s);
            string[] lines=s.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries);
            foreach(string line in lines){string code=LegacyCode(line);if(code!=null)return code;}
            for(int i=0;i+1<lines.Length;i++)if(lines[i].TrimEnd().EndsWith("-")){string code=LegacyCode(lines[i]+lines[i+1]);if(code!=null)return code;}
            return LegacyCode(s);
        }
        static string LegacyCode(string s){
            string c=Regex.Replace(CodeText(s),@"(И[.]?[ЛJI1Ј][ОO0][34ЗЧ])Л\.","$1.1.");c=Regex.Replace(c,@"(И[.]?[ЛJI1Ј][ОO0][34ЗЧ]\.[0-9]{1,2})Л(?=[^0-9]|$)","$1.1");
            var m=Regex.Match(c,@"(?<p>\d{3}-\d{2}-\d{4})-И[.]?[ЛJI1Ј][ОO0](?<s>[34ЗЧ]\.[0-9ЗОOLIЛНБ]{1,2}\.[0-9ЗОOLIЛНБ]{1,2})(?![0-9ЗОOLIЛНБ])");
            if(m.Success)return m.Groups["p"].Value+"-ИЛО"+Digits(m.Groups["s"].Value);
            m=Regex.Match(c,@"(?<p>\d{3}-\d{2}-\d{4})-ТКР(?<s>[0-9ЗОOLIЛНБ]{1,2}(?:[.,][0-9ЗОOLIЛНБ]{1,3}){0,3})(?![0-9ЗОOLIЛНБ])");
            return m.Success?m.Groups["p"].Value+"-ТКР"+Digits(m.Groups["s"].Value).Replace(',','.'):null;
        }
        static string CodeValue(string text){
            string value=DocumentIdentity.Value(text);if(value==null)return null;
            if(Regex.IsMatch(CodeText(value),@"^\d{3}-\d{2}-\d{4}-(?:И[.]?[ЛJI1Ј][ОO0]|ТКР)[0-9ЗЧОOLIЛНБ.,]+(?:-УЛ)?$")){string legacy=FindCode(value);if(legacy!=null)return legacy;}
            return value;
        }
        public static string DocumentCode(string text){if((text??"").Contains("\n"))return FindCode(text)??CodeValue(text);return Regex.IsMatch(text??"",@"\.(?:PDF|DOCX?|XLSX?|DWG|ZIP)$",RegexOptions.IgnoreCase)?FindCode(text)??DocumentIdentity.Value(text):CodeValue(text);}
        static string PageCode(string s){
            string explicitCode=FindCode(s);if(explicitCode!=null&&explicitCode.Contains("-ТКР"))return explicitCode;
            var prefix=Regex.Match(Compact(s),@"\d{3}-\d{2}-\d{4}");
            var tom=Regex.Match(N(s),@"\bТОМ\s*(?<v>4\s*\.\s*[34ЗЧ]\s*\.\s*[0-9ЗОOLIЛНБ]{1,2}\s*\.\s*[0-9ЗОOLIЛНБ]{1,2})(?![0-9ЗОOLIЛНБ])");
            if(prefix.Success&&tom.Success)return prefix.Value+"-ИЛО"+Regex.Replace(Digits(tom.Groups["v"].Value),@"\s+","").Substring(2);
            return FindCode(s);
        }
        static string Tom(string code){if(code==null)return null;int tkr=code.LastIndexOf("ТКР",StringComparison.Ordinal);int ilo=code.LastIndexOf("ИЛО",StringComparison.Ordinal);return tkr>=0?"3."+code.Substring(tkr+3):ilo>=0?"4."+code.Substring(ilo+3):null;}
        static List<Word> Words(OcrPage p){return p.lines.SelectMany(l=>l.words??new List<Word>()).ToList();}
        internal static int Score(OcrPage page){return Regex.Matches(N(page.text),@"ПРОЕКТ|ГЛАВНЫЙ|КНИГА|ТОМ|ДОКУМЕНТ|ОПИСЬ|ИНЖЕНЕР|КОНТРОЛЬ|ЛИСТ|СИСТЕМА|ЗАМЕСТИТЕЛЬ").Count*25+Regex.Matches(page.text??"",@"[А-Яа-я]{4,}").Count+(FindCode(page.text)!=null?120:0);}
        static bool ImageExt(string p){return new []{".jpg",".jpeg",".png",".bmp"}.Contains(Path.GetExtension(p).ToLowerInvariant());}
        static void Jpeg(Bitmap bitmap,string path){ParallelWork.SaveImage(path,()=>{var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.MimeType=="image/jpeg");using(var quality=new EncoderParameters(1)){quality.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,92L);bitmap.Save(path,codec,quality);}});}
        static void FullJpeg(Bitmap bitmap,string path){ParallelWork.SaveImage(path,()=>{var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.MimeType=="image/jpeg");using(var quality=new EncoderParameters(1)){quality.Param[0]=new EncoderParameter(System.Drawing.Imaging.Encoder.Quality,100L);bitmap.Save(path,codec,quality);}});}
        static string Fingerprint(IList<string> inputs){var s=new StringBuilder("pipeline-1.4.0\n");using(var hash=SHA256.Create())foreach(var input in inputs){if(Directory.Exists(input))foreach(var file in Directory.EnumerateFiles(input,"*",SearchOption.AllDirectories).Where(InputExt).OrderBy(f=>f,StringComparer.OrdinalIgnoreCase)){using(var stream=File.OpenRead(file))s.Append(Path.GetFullPath(file)).Append('|').Append(BitConverter.ToString(hash.ComputeHash(stream))).Append('\n');}else using(var stream=File.OpenRead(input))s.Append(Path.GetFullPath(input)).Append('|').Append(BitConverter.ToString(hash.ComputeHash(stream))).Append('\n');}return s.ToString();}
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
        internal static string IulDocumentText(Photo p){
            if(p.Kind!="ИУЛ")return null;
            var anchor=p.Words.Where(w=>Regex.IsMatch(N(w.text),@"^(?:РАЗДЕЛ|ПОДРАЗДЕЛ|ЧАСТЬ|КНИГА)$")&&w.y>p.Ocr.height*.25&&w.x>p.Ocr.width*.15).OrderBy(w=>w.y).FirstOrDefault();
            var end=anchor==null?null:p.Words.Where(w=>N(w.text)=="ТОМ"&&w.y>anchor.y+anchor.height&&w.x>=anchor.x-p.Ocr.width*.03).OrderBy(w=>w.y).FirstOrDefault();
            if(anchor==null||end==null||end.y-anchor.y>p.Ocr.height*.50)return null;
            var revision=AdvancedAudit.RevisionBounds(p.Ocr);double right=revision.HasValue?revision.Value.Left:p.Ocr.width*.82;
            var rows=new List<List<Word>>();foreach(var word in p.Words.Where(w=>w.x>=anchor.x-p.Ocr.width*.02&&w.x+w.width/2<right&&w.y>=anchor.y-anchor.height*.5&&w.y<end.y-end.height*.25).OrderBy(w=>w.y).ThenBy(w=>w.x)){
                var row=rows.FirstOrDefault(r=>Math.Abs(r.Average(w=>w.y+w.height/2)-(word.y+word.height/2))<Math.Max(word.height,r.Average(w=>w.height))*.65);if(row==null){row=new List<Word>();rows.Add(row);}row.Add(word);
            }
            return string.Join("\n",rows.OrderBy(r=>r.Average(w=>w.y)).Select(r=>string.Join(" ",r.OrderBy(w=>w.x).Select(w=>w.text))));
        }
        static string BookText(Photo p){
            string s=IulDocumentText(p)??p.Ocr.text??"";
            var m=Regex.Match(s,@"Книга\s*\d+[.,]?\s*(?<name>[\s\S]*?)(?=\r?\n\s*(?:\d{3}\s*[-–—]|Том\s*\d|Главный\s|Заместител)|$)",RegexOptions.IgnoreCase);
            if(!m.Success)return null;string value=m.Groups["name"].Value;
            if(p.Kind=="ИУЛ")value=Regex.Split(value,@"\b(?:Том|CRC32|Наименование файла|Алгоритм)\b",RegexOptions.IgnoreCase)[0];
            return Regex.Replace(value,@"\s+"," ").Trim(' ','.');
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
        static bool IsInventory(OcrPage page){
            string text=N(page.text),compact=Compact(page.text);
            bool heading=Regex.IsMatch(text.Substring(0,Math.Min(400,text.Length)),@"(?:^| )ОПИСЬ(?: |$)");
            bool columns=(compact.Contains("ШИФР")||compact.Contains("ОБОЗНАЧЕНИЕ"))&&compact.Contains("НАИМЕНОВАНИЕ");
            bool entries=Regex.IsMatch(compact,@"ИЛ[ОO0][34ЗЧ]")||CodeText(page.text).Contains("ТКР")||Regex.IsMatch(compact,@"(?:4\.[34ЗЧ]|3\.[0-9]{1,2})\.[0-9ЗОOLIЛНБ]");
            bool iul=compact.Replace('С','C').Replace('Р','R').Contains("CRC32")||compact.Contains("УДОСТОВЕРЯЮ")||compact.Contains("-УЛ");
            return !iul&&(heading||columns)&&(entries||DocumentIdentity.InventoryRows(page).Count>0);
        }
        static Photo Parse(Photo p,bool details=true){
            string n=N(p.Ocr.text),c=Compact(p.Ocr.text);p.Words=Words(p.Ocr);bool strongCode;string explicitCode=DocumentIdentity.FieldCode(p.Ocr,p.Image,out strongCode),legacyCode=PageCode(p.Ocr.text);
            bool inlineCode=Regex.IsMatch(p.Ocr.text??"",@"(?:Обозначение\s+документа|Шифр(?:\s+(?:документа|тома))?)\s*[:=]",RegexOptions.IgnoreCase);if(explicitCode!=null&&(inlineCode||legacyCode==null||!explicitCode.Contains(" ")&&DocumentIdentity.Key(explicitCode).Contains(DocumentIdentity.Key(legacyCode))))p.Code=CodeValue(explicitCode);else p.Code=legacyCode??DocumentIdentity.TitleCode(p.Ocr)??p.CodeHint;p.Tom=Tom(p.Code);
            if(p.InventoryOverride??(p.ManualInventory||IsInventory(p.Ocr))){p.Kind="Опись";p.Code=null;p.Tom=null;}
            else if(c.Replace('С','C').Replace('Р','R').Contains("CRC32")||c.Contains("УДОСТОВЕРЯЮЩ")||c.Contains("УДОСТОВЕРЯЮШ")||((n.Contains("КОНТРОЛЬНАЯ")||n.Contains("CRC32"))&&n.Contains("ЛИСТ"))||c.Contains("-УЛ"))p.Kind="ИУЛ";
            else if(n.Contains("ФРАГМЕНТ"))p.Kind="Титул фрагмента";
            else if(n.Contains("КНИГА")&&p.Code!=null)p.Kind="Титул";else p.Kind="Не определено";
            p.Organization=FormAnalysis.DocumentOrganization(p.Ocr);
            if(p.Kind=="Не определено"&&p.KindHint!=null&&!(p.InventoryOverride==false&&p.KindHint=="Опись"))p.Kind=p.KindHint;if(p.KindHint=="ИУЛ"&&p.Kind=="Титул")p.Kind="ИУЛ";if(p.Kind!="Опись"&&p.Code==null&&p.CodeHint!=null){p.Code=p.CodeHint;p.Tom=Tom(p.Code);}if(p.Organization=="Не определено"&&p.OrganizationHint!=null)p.Organization=p.OrganizationHint;
            if(p.InventoryOverride!=false&&(p.KindHint=="Опись"||p.Kind=="Опись")){p.Kind="Опись";p.Code=null;p.Tom=null;}
            p.Pages=null;p.Readings.RemoveAll(reading=>reading.Field=="Pages");
            p.Book=BookText(p);p.Chief=Chief(p);
            var crc=Regex.Match(n.Replace('С','C').Replace('Р','R').Replace('В','B').Replace('А','A').Replace('Е','E'),@"CRC\s*32[^0-9A-F]{0,6}(?<sum>[0-9A-FОOIL]{8})(?![0-9A-FОOIL])");if(crc.Success)p.CRC=Digits(crc.Groups["sum"].Value);
            if(p.CRC==null){var marker=p.Words.FirstOrDefault(w=>Compact(w.text).Replace('С','C').Replace('Р','R').StartsWith("CRC32"));if(marker!=null){var candidate=p.Words.Where(w=>Math.Abs(w.x-marker.x)<120&&w.y>=marker.y-8&&w.y<marker.y+80).OrderBy(w=>w.y).Select(w=>Digits(w.text).Replace('С','C').Replace('В','B').Replace('А','A').Replace('Е','E').Trim(',',':',';','•')).FirstOrDefault(s=>Regex.IsMatch(s,@"^[0-9A-F]{8}$"));p.CRC=candidate;}}
            if(p.Kind=="ИУЛ"){
                p.Page=FooterNumber(p,"ЛИСТ");
                int revision;p.Revision=int.TryParse(AdvancedAudit.Revision(p.Ocr),out revision)?(int?)revision:null;
                if(!AdvancedAudit.RevisionBounds(p.Ocr).HasValue)p.Readings.RemoveAll(reading=>reading.Field=="Revision");
            }
            ApplyReadings(p);if(details)FormAnalysis.Inspect(p,null);return p;
        }
        static string InventorySuffix(string text){
            string c=CodeText(text);if(!Regex.IsMatch(c,@"^(?:И[.]?[ЛJI1Ј][ОO0]|ТКР)"))return null;
            string code=FindCode("000-00-0000-"+c);return code==null?null:code.Substring(12);
        }
        static string InventoryPrefix(Photo p,Word anchor,string unique){
            var prefixes=p.Words.Where(w=>Math.Abs(w.x-anchor.x)<p.Ocr.width*.1&&Math.Abs(w.y-anchor.y)<85).Select(w=>new {Word=w,Match=Regex.Match(CodeText(w.text),@"^\d{3}-\d{2}-\d{4}-?$" )}).Where(v=>v.Match.Success).OrderBy(v=>Math.Abs(v.Word.y-anchor.y)).ToList();
            return prefixes.Count>0?prefixes[0].Match.Value.TrimEnd('-'):unique;
        }
        static List<InventoryEntry> ParseInventory(Photo p){
            var result=new List<InventoryEntry>();
            var pagePrefixes=Regex.Matches(CodeText(p.Ocr.text),@"\d{3}-\d{2}-\d{4}").Cast<Match>().Select(m=>m.Value).Distinct().ToList();string uniquePrefix=pagePrefixes.Count==1?pagePrefixes[0]:null;
            var suffixes=p.Words.Where(w=>w.x>p.Ocr.width*.145&&w.x<p.Ocr.width*.4).Select(w=>new {Word=w,Suffix=InventorySuffix(w.text)}).Where(v=>v.Suffix!=null).OrderBy(v=>v.Word.y).ToList();
            foreach(var suffix in suffixes){
                string prefix=InventoryPrefix(p,suffix.Word,uniquePrefix);if(prefix==null)continue;
                string code=FindCode(prefix+"-"+suffix.Suffix);if(code==null||result.Any(row=>row.Code==code&&Math.Abs(row.AnchorY-suffix.Word.y)<80))continue;
                result.Add(InventoryRow(p,code,Tom(code),suffix.Word.y));
            }
            // A legible volume can recover a wrapped or missed code only when that code family is present on the page.
            bool ilo=suffixes.Any(v=>v.Suffix.StartsWith("ИЛО")),tkr=suffixes.Any(v=>v.Suffix.StartsWith("ТКР"));
            foreach(var word in p.Words.Where(w=>w.x<p.Ocr.width*.2).OrderBy(w=>w.y)){
                string volume=Digits(word.text).Replace(',','.');
                bool isTkr=tkr&&Regex.IsMatch(volume,@"^3\.[0-9]{1,2}(?:\.[0-9]{1,3}){0,3}$"),isIlo=ilo&&Regex.IsMatch(volume,@"^4\.[34]\.[0-9]{1,2}\.[0-9]{1,2}$");
                if(!isTkr&&!isIlo)continue;string prefix=InventoryPrefix(p,word,uniquePrefix);if(prefix==null)continue;
                string code=prefix+"-"+(isTkr?"ТКР":"ИЛО")+volume.Substring(2);var near=result.FirstOrDefault(row=>Math.Abs(row.AnchorY-word.y)<70);if(near!=null){if(code.StartsWith(near.Code+".",StringComparison.Ordinal)){near.Code=code;near.Tom=volume;}continue;}result.Add(InventoryRow(p,code,volume,word.y));
            }
            foreach(var row in DocumentIdentity.InventoryRows(p.Ocr,p.Image))if(!result.Any(r=>Math.Abs(r.AnchorY-row.Y)<Math.Max(70,p.Ocr.height*.028)))result.Add(InventoryRow(p,CodeValue(row.Code),row.Volume,row.Y));
            return result.OrderBy(row=>row.AnchorY).ToList();
        }
        static InventoryEntry InventoryRow(Photo p,string code,string volume,double anchor){
                var bookWord=p.Words.Where(w=>N(w.text).StartsWith("КНИГА")&&w.x>p.Ocr.width*.34&&Math.Abs(w.y-anchor)<85).OrderBy(w=>Math.Abs(w.y-anchor)).FirstOrDefault();
                string book=null;if(bookWord!=null){double limit=p.Ocr.height;var next=p.Words.Where(w=>N(w.text).StartsWith("КНИГА")&&w.x>p.Ocr.width*.34&&w.x<p.Ocr.width*.76&&w.y>bookWord.y+20).OrderBy(w=>w.y).FirstOrDefault();if(next!=null)limit=next.y-4;
                    var stop=p.Words.Where(w=>w.x>p.Ocr.width*.34&&w.x<p.Ocr.width*.75&&w.y>bookWord.y+15&&(N(w.text).StartsWith("ЧАСТЬ")||N(w.text).StartsWith("ПОДРАЗДЕЛ"))).OrderBy(w=>w.y).FirstOrDefault();if(stop!=null)limit=Math.Min(limit,stop.y-4);
                    var anchorLine=p.Ocr.lines.First(l=>l.words.Contains(bookWord));double start=anchorLine.words.Where(w=>w.x>=bookWord.x-2&&w.x<p.Ocr.width*.72).Average(w=>w.y+w.height/2);
                    string line=string.Join(" ",p.Ocr.lines.Select(l=>l.words.Where(w=>w.x>=bookWord.x-2&&w.x<p.Ocr.width*.72).ToList()).Where(ws=>ws.Count>0&&ws.Average(w=>w.y+w.height/2)>=start-8&&ws.Average(w=>w.y)<limit).OrderBy(ws=>ws.Average(w=>w.y+w.height/2)).Select(ws=>string.Join(" ",ws.OrderBy(w=>w.x).Select(w=>w.text))));book=Regex.Replace(line,@"^Книга\s*[0-9ЗзбБ]+[.,]?\s*","",RegexOptions.IgnoreCase).Trim();}
                var marker=p.Words.Where(w=>N(w.text).StartsWith("ИЗМ")&&w.x>p.Ocr.width*.72&&Math.Abs(w.y-anchor)<60).OrderBy(w=>Math.Abs(w.y-anchor)).FirstOrDefault();int? revision=null;
                if(marker!=null){var num=p.Words.Where(w=>w.x>marker.x&&w.x<marker.x+145&&Math.Abs(w.y-marker.y)<22&&Regex.IsMatch(w.text??"",@"^\d{1,2}$")).OrderBy(w=>w.x).FirstOrDefault();if(num!=null)revision=int.Parse(num.text);}
                return new InventoryEntry {Code=code,Tom=volume,Book=book,Revision=revision,Photo=p,AnchorY=anchor,Box=InventoryBox(p,anchor),DocumentationKind=InventoryDocumentationKind(p.Ocr)};
        }
        static int? InventoryBox(Photo p,double anchor){
            var header=p.Words.Where(w=>N(w.text).StartsWith("КОРОБ")&&w.x>p.Ocr.width*.6).OrderBy(w=>w.y).FirstOrDefault();if(header==null)return null;
            double centre=header.x+header.width/2;
            var values=p.Words.Where(w=>Math.Abs(w.x+w.width/2-centre)<p.Ocr.width*.06&&w.y>header.y+header.height&&Math.Abs(w.y-anchor)<Math.Max(65,p.Ocr.height*.024)&&Regex.IsMatch(w.text??"",@"^[0-9]{1,5}$")).OrderBy(w=>Math.Abs(w.y-anchor)).ToList();
            if(values.Count==0)return null;
            // Do not choose between two different readings within the same row band.
            var numbers=values.Select(w=>int.Parse(w.text)).Where(v=>v>0).Distinct().ToList();return numbers.Count==1?(int?)numbers[0]:null;
        }
        public static string InventoryDocumentationKind(OcrPage page){
            string text=N(page.text);int end=text.IndexOf("ШИФР ТОМА",StringComparison.Ordinal);if(end>=0)text=text.Substring(0,end);else text=text.Substring(0,Math.Min(text.Length,1200));
            int stage=text.IndexOf("СТАДИЯ",StringComparison.Ordinal);if(stage>=0)text=text.Substring(stage);
            var kinds=new List<string>();if(Regex.IsMatch(text,@"ПРОЕКТН(?:АЯ|ОЙ)\s+(?:И\s+СМЕТН(?:АЯ|ОЙ)\s+)?ДОКУМЕНТАЦИ"))kinds.Add("ПД");
            if(Regex.IsMatch(text,@"ИНЖЕНЕРН.{0,12}ИЗЫСКАН"))kinds.Add("ИИ");if(Regex.IsMatch(text,@"ДОКУМЕНТАЦИ.{0,12}ПО\s+ПЛАНИРОВКЕ\s+ТЕРРИТОРИИ"))kinds.Add("ДПТ");
            return kinds.Count==1?kinds[0]:null;
        }
        static int Distance(string a,string b){int[] prev=Enumerable.Range(0,b.Length+1).ToArray();for(int i=1;i<=a.Length;i++){int[] cur=new int[b.Length+1];cur[0]=i;for(int j=1;j<=b.Length;j++)cur[j]=Math.Min(Math.Min(cur[j-1]+1,prev[j]+1),prev[j-1]+(a[i-1]==b[j-1]?0:1));prev=cur;}return prev[b.Length];}
        static bool Different(string a,string b){a=BookClean(a);b=BookClean(b);if(a.Length<4||b.Length<4)return false;return (double)Distance(a,b)/Math.Max(a.Length,b.Length)>.28;}
        static void Add(AuditResult r,string level,string code,string topic,string detail,params Photo[] photos){var f=new Finding {Level=level,Code=code??"Без шифра",Topic=topic,Detail=detail};foreach(var p in photos.Where(p=>p!=null))f.Photos.Add(p.Id);r.Findings.Add(f);}
        public static AuditResult Analyze(List<Photo> photos,string registry,Action<int,string> progress=null,CancellationToken cancel=default(CancellationToken),int workers=1,ISet<int> inspectPages=null){
            var r=new AuditResult {Images=photos.Count,Photos=photos};
            ParallelWork.For(photos.Count,workers,cancel,i=>Parse(photos[i],false));
            foreach(var p in photos.Where(p=>p.Kind=="Опись"))r.Inventory.AddRange(ParseInventory(p));
            var known=r.Inventory.Select(i=>i.Code).Concat(photos.Where(p=>p.Code!=null).Select(p=>p.Code)).Distinct().ToList();
            var exactCodes=known.GroupBy(DocumentIdentity.Key).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.First());
            foreach(var p in photos.Where(p=>p.Kind!="Опись")){
                bool strong;string field=DocumentIdentity.FieldCode(p.Ocr,p.Image,out strong),match=null;
                if(field!=null&&exactCodes.TryGetValue(DocumentIdentity.Key(CodeValue(field)),out match)){p.CodeHint=match;Parse(p,false);p.Code=match;p.Tom=Tom(match);continue;}
                match=DocumentIdentity.MatchPage(p.Ocr,known);if(match!=null&&(p.Code==null||DocumentIdentity.Key(p.Code)==DocumentIdentity.Key(match))){p.CodeHint=match;Parse(p,false);p.Code=match;p.Tom=Tom(match);}
            }
            int processed=0;var inspected=new bool[photos.Count];
            ParallelWork.For(photos.Count,workers,cancel,i=>{var p=photos[i];if(inspectPages==null||inspectPages.Contains(p.Id)||p.Signatures==null||p.Seal==null||p.Kind=="Титул"&&p.Seal.Status=="Не применяется"){FormAnalysis.Inspect(p,null);inspected[i]=true;}int n=Interlocked.Increment(ref processed);if(progress!=null)progress(89+(int)(8.0*n/Math.Max(1,photos.Count)),"Правила и сохранённые данные: "+n+" / "+photos.Count);});
            if(inspectPages!=null)for(int i=0;i<photos.Count;i++)if(inspected[i])inspectPages.Add(photos[i].Id);
            foreach(var p in photos){string body=IulDocumentText(p);if(body!=null)p.Sections=FormAnalysis.Sections(body);}
            foreach(var entry in r.Inventory)entry.Sections=FormAnalysis.InventorySections(entry.Photo,entry);
            // Continuation pages may omit the stage; propagate it only within the same explicit box number.
            var kindByBox=r.Inventory.Where(i=>i.Box.HasValue&&i.DocumentationKind!=null).GroupBy(i=>i.Box.Value).ToDictionary(g=>g.Key,g=>g.Select(i=>i.DocumentationKind).Distinct().ToList());
            foreach(var entry in r.Inventory){List<string> kinds;if(entry.DocumentationKind==null&&entry.Box.HasValue&&kindByBox.TryGetValue(entry.Box.Value,out kinds)&&kinds.Count==1)entry.DocumentationKind=kinds[0];}
            foreach(var duplicates in r.Inventory.GroupBy(i=>i.Code)){
                var first=duplicates.First();var boxes=duplicates.Where(i=>i.Box.HasValue).Select(i=>i.Box.Value).Distinct().ToList();var kinds=duplicates.Where(i=>i.DocumentationKind!=null).Select(i=>i.DocumentationKind).Distinct().ToList();
                if(boxes.Count>1){first.Box=null;Add(r,"Проверить",first.Code,"Разные номера короба в описи",string.Join(" / ",boxes)+". Номер короба автоматически не переносится.",duplicates.Select(i=>i.Photo).ToArray());}else if(boxes.Count==1)first.Box=boxes[0];
                if(kinds.Count>1){first.DocumentationKind=null;Add(r,"Проверить",first.Code,"Разные виды документации в описи",string.Join(" / ",kinds)+". Вид документации автоматически не переносится.",duplicates.Select(i=>i.Photo).ToArray());}else if(kinds.Count==1)first.DocumentationKind=kinds[0];
            }
            var parsedInventoryPages=new HashSet<int>(r.Inventory.Select(entry=>entry.Photo.Id));
            r.Inventory=r.Inventory.GroupBy(i=>i.Code).Select(g=>g.First()).ToList();r.InventoryPages=photos.Count(p=>p.Kind=="Опись");r.MainTitles=photos.Count(p=>p.Kind=="Титул");r.Iul=photos.Count(p=>p.Kind=="ИУЛ");r.InventoryOnly=photos.Count>0&&photos.All(p=>p.Kind=="Опись");
            foreach(var photo in photos.Where(p=>p.Kind=="Опись"&&!parsedInventoryPages.Contains(p.Id)))Add(r,"Проверить",null,"Строки описи не распознаны","Страница определена как опись, но строки с шифрами томов не извлечены. Откройте фотографию и проверьте качество распознавания.",photo);
            var records=new Dictionary<string,List<SourceRow>>();var fileMatcher=new DocumentIdentity.Matcher(r.Inventory.Select(i=>i.Code).Concat(photos.Where(p=>p.Code!=null).Select(p=>p.Code)));
            if(!string.IsNullOrWhiteSpace(registry))foreach(var row in XlsxReader.Read(registry,"Все загруженные файлы").Rows){string code=fileMatcher.Match(XlsxReader.Text(row.Values[2]));if(code==null)continue;if(!records.ContainsKey(code))records[code]=new List<SourceRow>();records[code].Add(row);}
            var groups=photos.Where(p=>p.Code!=null&&p.Kind!="Опись").GroupBy(p=>p.Code).ToList();r.Volumes=r.InventoryOnly?r.Inventory.Count:groups.Count;
            if(!r.InventoryOnly)foreach(var inv in r.Inventory)if(!groups.Any(g=>g.Key==inv.Code))Add(r,"Проверить",inv.Code,"Нет фотографий тома","Том указан в распознанной описи, но его титулы и ИУЛ не найдены среди загруженных изображений.",inv.Photo);
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
                if(!title.Any(p=>p.Organization=="Росжелдорпроект"||p.Signatures.Any(v=>v.Approval&&v.Organization=="Росжелдорпроект"))&&iul.Any(p=>p.Organization=="Росжелдорпроект"))Add(r,"Проверить",group.Key,"Титул Росжелдорпроекта не найден","Не найден отдельный основной титул изготовителя. Возможна неполная загрузка или ошибка распознавания логотипа.",group.First());
                foreach(var t in title)foreach(var role in t.Signatures.Where(v=>v.Role=="Главный инженер"&&!v.Approval||v.Role=="Главный инженер проекта")) {
                    var matches=iul.Where(v=>v.Organization==role.Organization).SelectMany(v=>v.Signatures.Select(sig=>new {Photo=v,Signature=sig})).Where(v=>v.Signature.Role==role.Role).ToList();
                    if(matches.Count==0&&iul.Any(v=>v.Organization==role.Organization))Add(r,"Проверить",group.Key,"Состав подписантов ИУЛ","На титуле есть «"+role.Role+"», но эта роль не распознана в ИУЛ той же организации: "+role.Organization+".",t,iul.First(v=>v.Organization==role.Organization));
                    foreach(var match in matches.Where(v=>v.Signature.Name!=null&&role.Name!=null&&Different(v.Signature.Name,role.Name))) {
                        if(role.Role=="Главный инженер"&&t.Organization=="Ленгипротранс")continue;
                        Add(r,"Проверить",group.Key,role.Role+": титул / ИУЛ","Организация: "+role.Organization+". Титул: "+role.Name+"; ИУЛ: "+match.Signature.Name+". Проверьте распознанные фамилии одной и той же роли.",t,match.Photo);
                    }
                }
                foreach(var t in title)foreach(var p in iul.Where(p=>p.Book!=null))foreach(var section in t.Sections) {
                    string value;if(p.Sections.TryGetValue(section.Key,out value)&&Different(section.Value,value))Add(r,"Проверить",group.Key,"Название раздела / части","Поле: "+section.Key+". Титул: «"+section.Value+"»; ИУЛ: «"+value+"». Возможны обрыв строки или ошибка OCR.",t,p);
                }
                if(inv!=null&&inv.Sections!=null)foreach(var section in inv.Sections)foreach(var p in title.Concat(iul.Where(p=>p.Book!=null))) {
                    string value;if(p.Sections.TryGetValue(section.Key,out value)&&Different(section.Value,value))Add(r,"Проверить",group.Key,"Раздел / часть: опись и документ","Поле: "+section.Key+". Опись: «"+section.Value+"»; документ: «"+value+"». Проверьте полный заголовок.",inv.Photo,p);
                }
            }
            foreach(var p in photos){
                if(p.Kind=="Титул"&&p.Seal!=null&&p.Seal.Score<=.60)Add(r,"Проверить",p.Code,"Печать на титуле",p.Seal.Status+". Круговая форма проверяется отдельно от подписных строк; другие формы штампа требуют просмотра.",p);
                var unconfirmed=p.Signatures.Where(v=>!v.Status.StartsWith("Найдены")).ToList();
                if(p.Kind=="Титул фрагмента"&&p.Signatures.Count==0)Add(r,"Наблюдение",p.Code,"Титул фрагмента","Дополнительный титул фрагмента: состав подписантов и необходимость отдельного подписания определяются по правилам вашей документации.",p);
                if(unconfirmed.Count>0)Add(r,"Проверить",p.Code,"Подписи: проверка отдельных строк",string.Join("; ",unconfirmed.Select(v=>v.Role+" ("+(v.Name??"фамилия не прочитана")+"): "+v.Status))+". Показаны отдельные ячейки; наличие штрихов не подтверждает подлинность подписи.",p);
                if(p.Kind=="ИУЛ"&&p.Signatures.Count==0)Add(r,"Проверить",p.Code,"Подписная таблица не распознана","Не удалось выделить подписные строки. Отсутствие распознанной таблицы не означает отсутствие подписей.",p);
                foreach(var proposed in p.Readings.Where(v=>v.Status.StartsWith("Предложено по форме цифры")))Add(r,"Проверить",p.Code,"Проверьте чтение: "+FieldLabel(proposed.Field),"Windows OCR не прочитал отдельную цифру. По сходству с образцами шрифтов предложено значение «"+proposed.Value+"». Это предварительное чтение: подтвердите его по увеличенному фрагменту.",p);
                foreach(var reading in p.Readings.Where(v=>v.Value==null)){
                    // The total is commonly printed only on the last IUL sheet.
                    string label=FieldLabel(reading.Field);bool disputed=reading.Candidates.Count>0;Add(r,"Проверить",p.Code,disputed?"Разные результаты чтения: "+label:"Не удалось прочитать: "+label,disputed?"Повторные попытки дали разные значения: "+string.Join(" / ",reading.Candidates)+". Проверьте графу «"+label+"» на увеличенном фрагменте.":"Программа не получила значение графы «"+label+"». Поле может быть заполнено на фотографии, даже если OCR его пропустил. Откройте увеличенный фрагмент; это не подтверждённое расхождение.",p);
                }
                if(p.Kind=="Не определено"||p.Kind!="Опись"&&p.Code==null)Add(r,"Проверить",p.Code,"Документ не распознан уверенно","Не удалось определить вид листа или шифр. Снимок сохранён для просмотра.",p);
            }
            r.Findings=r.Findings.GroupBy(f=>f.Code+"|"+f.Topic+"|"+f.Detail).Select(g=>g.First()).ToList();
            return r;
        }
        static string Arg(string value){if(value.Contains("\""))throw new ArgumentException("Недопустимый символ в пути.");return "\""+value+"\"";}
        static bool InputExt(string path){return ImageExt(path)||Path.GetExtension(path).Equals(".pdf",StringComparison.OrdinalIgnoreCase);}
        static OcrPage ReadOcr(string folder,string file){return Json.Deserialize<OcrPage>(File.ReadAllText(Path.Combine(folder,Path.GetFileNameWithoutExtension(file)+".json"),Encoding.UTF8));}
        static void ApplyReadings(Photo p) {
            foreach(var reading in p.Readings){int n;
                if(reading.Field=="Page"&&reading.Value==null&&reading.Candidates.Count==0&&!string.IsNullOrEmpty(p.Image)){
                    string cell=Path.Combine(Path.GetDirectoryName(p.Image),"p"+p.Id.ToString("D4")+"-PageCell-1.png");
                    if(File.Exists(cell))using(var image=new Bitmap(cell)){double similarity;string proposed=DigitShapes.Read(image,out similarity);if(proposed!=null){reading.Value=proposed;reading.Candidates.Add(proposed);reading.Sources.Add(Path.GetFileName(cell)+" (форма цифры; сходство "+similarity.ToString("0.00",Inv)+"): "+proposed);reading.Status="Предложено по форме цифры; проверьте фрагмент";reading.Agreement=1;reading.Attempts++;}}
                }
                if(reading.Field=="CRC")p.CRC=reading.Value;
                if(reading.Field=="Page")p.Page=int.TryParse(reading.Value,out n)?(int?)n:null;
                if(reading.Field=="Revision")p.Revision=int.TryParse(reading.Value,out n)?(int?)n:null;
            }
        }
        static string Field(Photo p,string name){return name=="CRC"?p.CRC:name=="Page"?Convert.ToString(p.Page,Inv):Convert.ToString(p.Revision,Inv);}
        static string FieldLabel(string name){return name=="Page"?"Лист":name=="Pages"?"Листов":name=="Revision"?"Номер изменения":name=="CRC"?"Контрольная сумма CRC32":name;}
        public static InventoryPreview PreviewInventory(IList<Photo> photos){
            foreach(var photo in photos)Parse(photo,false);
            var preview=new InventoryPreview{Items=photos.Select(p=>new InventoryPreviewPage{Id=p.Id,Original=p.Original,Image=p.Image,Automatic=p.Kind=="Опись"&&!p.ManualInventory,Manual=p.ManualInventory,Selected=p.Kind=="Опись"}).ToList()};preview.Refresh();return preview;
        }
        public static AuditResult Run(IList<string> inputs,string registry,string destination,Action<int,string> progress,CancellationToken cancel,int requestedWorkers=0,IList<string> inventoryInputs=null,Func<InventoryPreview,bool> beforeAudit=null,bool adaptiveNumeric=true){
            var explicitInputs=new HashSet<string>((inventoryInputs??new List<string>()).Select(Path.GetFullPath),StringComparer.OrdinalIgnoreCase);
            inputs=inputs.Concat(explicitInputs).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var explicitFiles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var timing=new RunTiming();timing.Mark("Входные файлы и PDF");
            int imageWorkers=ParallelWork.Workers(requestedWorkers,512),ocrWorkers=ParallelWork.Workers(requestedWorkers,128),visualWorkers=ParallelWork.Workers(requestedWorkers,96);
            var publish=progress;var progressGate=new object();int lastProgress=-1;var lastMessage=DateTime.MinValue;
            progress=(percent,message)=>{lock(progressGate){percent=Math.Max(lastProgress,percent);if(percent!=lastProgress||percent==100||(DateTime.UtcNow-lastMessage).TotalMilliseconds>=150){lastProgress=percent;lastMessage=DateTime.UtcNow;publish(percent,message);}}};
            progress(0,"Параллельная обработка: изображения — "+imageWorkers+", OCR — "+ocrWorkers+", подписи и печати — "+visualWorkers+". Ограничения учитывают доступную память.");
            string root=Path.GetFullPath(destination);ParallelWork.CheckDiskSpace(root);Directory.CreateDirectory(root);string work=Path.Combine(root,".ocr-work");Directory.CreateDirectory(work);
            string candidates=Path.Combine(work,"images"),jsons=Path.Combine(work,"json"),large=Path.Combine(work,"large"),largeJson=Path.Combine(work,"large-json"),crops=Path.Combine(work,"crops"),cropJson=Path.Combine(work,"crop-json");
            foreach(var dir in new[]{candidates,jsons,large,largeJson,crops,cropJson})Directory.CreateDirectory(dir);
            string marker=Path.Combine(work,"input-fingerprint.txt"),fingerprint=Fingerprint(inputs);bool useCache=File.Exists(marker)&&File.ReadAllText(marker)==fingerprint;File.WriteAllText(marker,fingerprint,Encoding.UTF8);
            var files=new List<Tuple<string,string>>();long extracted=0;int entries=0;
            foreach(string input in inputs){cancel.ThrowIfCancellationRequested();
                bool manual=explicitInputs.Contains(Path.GetFullPath(input));
                if(Directory.Exists(input)){foreach(var file in Directory.EnumerateFiles(input,"*",SearchOption.AllDirectories).Where(InputExt).OrderBy(f=>f,StringComparer.OrdinalIgnoreCase)){files.Add(Tuple.Create(file,file));if(manual)explicitFiles.Add(file);}}
                else if(Path.GetExtension(input).Equals(".zip",StringComparison.OrdinalIgnoreCase))using(var fs=File.OpenRead(input))using(var zip=new ZipArchive(fs,ZipArchiveMode.Read))foreach(var e in zip.Entries.Where(e=>InputExt(e.FullName)).OrderBy(e=>e.FullName,StringComparer.OrdinalIgnoreCase)){
                    entries++;extracted+=e.Length;if(entries>1000||e.Length>150000000||extracted>2000000000)throw new InvalidDataException("Архив превышает допустимый размер или число файлов.");string path=Path.Combine(work,"original"+files.Count+Path.GetExtension(e.FullName));using(var es=e.Open())using(var os=File.Create(path))es.CopyTo(os);files.Add(Tuple.Create(path,Path.GetFileName(input)+" / "+e.FullName));if(manual)explicitFiles.Add(path);
                }else if(InputExt(input)){files.Add(Tuple.Create(Path.GetFullPath(input),Path.GetFileName(input)));if(manual)explicitFiles.Add(Path.GetFullPath(input));}
            }
            files=files.GroupBy(file=>Path.GetFullPath(file.Item1),StringComparer.OrdinalIgnoreCase).Select(group=>group.First()).ToList();
            // An explicitly chosen inventory may already be inside the selected folder or archive.
            var duplicates=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var lengthGroup in files.GroupBy(file=>new FileInfo(file.Item1).Length).Where(group=>group.Count()>1&&group.Any(file=>explicitFiles.Contains(file.Item1)))){
                var hashes=new Dictionary<string,Tuple<string,string>>();using(var hash=SHA256.Create())foreach(var file in lengthGroup){string digest;using(var stream=File.OpenRead(file.Item1))digest=BitConverter.ToString(hash.ComputeHash(stream));Tuple<string,string> first;
                    if(hashes.TryGetValue(digest,out first)){duplicates.Add(file.Item1);if(explicitFiles.Contains(file.Item1))explicitFiles.Add(first.Item1);}else hashes.Add(digest,file);
                }
            }
            files=files.Where(file=>!duplicates.Contains(file.Item1)).ToList();
            if(files.Count==0)throw new InvalidOperationException("Не найдены изображения JPG, PNG, BMP или PDF.");if(files.Count>1000)throw new InvalidOperationException("Одна проверка поддерживает до 1000 входных файлов.");
            var sources=new List<Tuple<string,string>>();
            foreach(var file in files){cancel.ThrowIfCancellationRequested();if(Path.GetExtension(file.Item1).Equals(".pdf",StringComparison.OrdinalIgnoreCase)){
                string pages=Path.Combine(work,"pdf-"+sources.Count);Directory.CreateDirectory(pages);int remaining=1000-sources.Count;if(remaining<=0)throw new InvalidOperationException("Одна проверка поддерживает до 1000 страниц / изображений.");
                NativeWindows.RenderPdf(file.Item1,pages,remaining,imageWorkers,s=>progress(1,"PDF "+file.Item2+": "+s),cancel);
                int page=0;foreach(var image in Directory.EnumerateFiles(pages,"*.png").OrderBy(f=>f,StringComparer.Ordinal)){page++;sources.Add(Tuple.Create(image,file.Item2+" / стр. "+page));if(explicitFiles.Contains(file.Item1))explicitFiles.Add(image);}
            }else sources.Add(file);if(sources.Count>1000)throw new InvalidOperationException("Одна проверка поддерживает до 1000 страниц / изображений.");}
            if(sources.Count==0)throw new InvalidOperationException("В PDF отсутствуют страницы.");
            ParallelWork.CheckDiskSpace(root,sources.Count*24L*1024*1024);
            int imageBudget=128;foreach(var source in sources){using(var header=Image.FromFile(source.Item1)){if((long)header.Width*header.Height>45000000)throw new InvalidDataException("Изображение превышает 45 мегапикселей: "+source.Item2);imageBudget=Math.Max(imageBudget,ParallelWork.ImageBudget(header.Width,header.Height));}}
            imageWorkers=ParallelWork.Workers(requestedWorkers,imageBudget);progress(2,"С учётом размера страниц: изображения — "+imageWorkers+", OCR — "+ocrWorkers+", подписи и печати — "+visualWorkers);
            timing.Mark("Подготовка изображений");int prepared=0;ParallelWork.For(sources.Count,imageWorkers,cancel,i=>{if(!useCache||!File.Exists(Path.Combine(candidates,"p"+(i+1).ToString("D4")+"a.jpg")))Prepare(sources[i].Item1,candidates,i+1);int n=Interlocked.Increment(ref prepared);progress(2+(int)(8.0*n/sources.Count),"Подготовлено "+n+" / "+sources.Count);});
            timing.Mark("Первичное OCR");int done=0;NativeWindows.Recognize(candidates,jsons,ocrWorkers,useCache,s=>progress(10+(int)(23.0*Interlocked.Increment(ref done)/(sources.Count*2)),"Первичное OCR: "+s),cancel);
            var identified=new List<Photo>();for(int i=0;i<sources.Count;i++){string stem="p"+(i+1).ToString("D4");var a=ReadOcr(jsons,stem+"a.jpg");var b=ReadOcr(jsons,stem+"b.jpg");var best=Score(a)>=Score(b)?a:b;identified.Add(new Photo{Id=i+1,Original=sources[i].Item2,Image=Path.Combine(candidates,best.file),Ocr=best,ManualInventory=explicitFiles.Contains(sources[i].Item1)});}
            var preview=PreviewInventory(identified);File.WriteAllText(Path.Combine(root,"Поиск_описи.json"),Json.Serialize(preview),new UTF8Encoding(false));
            progress(33,"До основной проверки найдено страниц описи: "+preview.Pages+" (автоматически: "+preview.AutomaticPages+", указано вручную: "+preview.ManualPages+").");
            if(beforeAudit!=null){timing.Mark("Выбор страниц описи");if(!beforeAudit(preview))throw new OperationCanceledException();cancel.ThrowIfCancellationRequested();preview.Apply(identified);File.WriteAllText(Path.Combine(root,"Поиск_описи.json"),Json.Serialize(preview),new UTF8Encoding(false));}cancel.ThrowIfCancellationRequested();
            timing.Mark("Геометрия страниц");var photoArray=new Photo[sources.Count];var baselineArray=new Dictionary<string,string>[sources.Count];int geometryDone=0;string assets=Path.Combine(root,"assets");Directory.CreateDirectory(assets);
            ParallelWork.For(sources.Count,imageWorkers,cancel,i=>{
                string stem="p"+(i+1).ToString("D4");var pa=ReadOcr(jsons,stem+"a.jpg");var pb=ReadOcr(jsons,stem+"b.jpg");var best=Score(pa)>=Score(pb)?pa:pb;
                string image=Path.Combine(assets,stem+".jpg");File.Copy(Path.Combine(candidates,best.file),image,true);
                var p=new Photo{Id=i+1,Original=sources[i].Item2,Image=image,Ocr=best,ManualInventory=identified[i].ManualInventory,InventoryOverride=identified[i].InventoryOverride};Parse(p,false);p.KindHint=p.Kind;p.CodeHint=p.Code;p.OrganizationHint=p.Organization;baselineArray[i]=new Dictionary<string,string>();foreach(var f in new[]{"CRC","Page","Revision"})baselineArray[i][f]=Field(p,f);
                p.FullImage=Path.Combine(work,stem+"-full.jpg");
                if(useCache&&File.Exists(p.FullImage)&&File.Exists(Path.Combine(large,stem+".jpg"))&&File.Exists(Path.Combine(large,stem+"-scan.png"))){p.ProcessingNote="Использована подготовленная геометрия того же входного файла";photoArray[i]=p;int cached=Interlocked.Increment(ref geometryDone);progress(33+(int)(7.0*cached/sources.Count),"Геометрия из кэша: "+cached+" / "+sources.Count);return;}
                using(var original=AdvancedAudit.Upright(sources[i].Item1,best==pb)) {
                    string note;
                    using(var corrected=AdvancedAudit.Rectify(original,best,out note)){
                        p.FullImage=Path.Combine(work,stem+"-full.jpg");FullJpeg(corrected,p.FullImage);p.ProcessingNote=note;
                        using(var overview=AdvancedAudit.Resize(corrected,3000)){Jpeg(overview,Path.Combine(large,stem+".jpg"));using(var scan=DocumentScan.Enhance(overview,true,cancel)){string scanPath=Path.Combine(large,stem+"-scan.png");ParallelWork.SaveImage(scanPath,()=>scan.Save(scanPath,ImageFormat.Png));}}
                        p.ProcessingNote+="; очищен фон, выровнено освещение и усилен контраст текста";
                    }
                }photoArray[i]=p;int n=Interlocked.Increment(ref geometryDone);progress(33+(int)(7.0*n/sources.Count),"Геометрия и исходное разрешение: "+n+" / "+sources.Count);
            ParallelWork.RelieveMemory();});var photos=photoArray.ToList();var baseline=photos.ToDictionary(p=>p.Id,p=>baselineArray[p.Id-1]);
            timing.Mark("OCR высокого разрешения");done=0;NativeWindows.Recognize(large,largeJson,ocrWorkers,useCache,s=>progress(40+(int)(15.0*Interlocked.Increment(ref done)/(sources.Count*2)),"OCR высокого разрешения: "+s),cancel);
            timing.Mark("Увеличение фрагментов");var cropArray=new List<CropRequest>[photos.Count];var uncleanedReadings=new Dictionary<string,string>[photos.Count];var skippedNumeric=new HashSet<string>[photos.Count];int cropDone=0;
            ParallelWork.For(photos.Count,imageWorkers,cancel,i=>{var p=photos[i];string stem="p"+p.Id.ToString("D4");var improved=ReadOcr(largeJson,stem+".jpg");var uncleaned=new Photo{Id=p.Id,Ocr=improved,Image=p.Image};Parse(uncleaned,false);uncleanedReadings[i]=new Dictionary<string,string>();foreach(var field in new[]{"CRC","Page","Revision"})uncleanedReadings[i][field]=Field(uncleaned,field);
                p.ScanOcr=ReadOcr(largeJson,stem+"-scan.png");p.ScanImage=Path.Combine(assets,stem+"-scan.png");File.Copy(Path.Combine(large,stem+"-scan.png"),p.ScanImage,true);File.Copy(Path.Combine(largeJson,stem+"-scan.json"),Path.Combine(assets,stem+"-scan.json"),true);
                if(Score(p.ScanOcr)>Score(improved)*1.15&&(FindCode(improved.text)==null||FindCode(improved.text)==FindCode(p.ScanOcr.text))){improved=p.ScanOcr;p.ProcessingNote+="; основное OCR по очищенной странице";}
                if(Score(improved)>=Score(p.Ocr)*.80){p.Ocr=improved;p.Image=Path.Combine(assets,stem+"-processed.jpg");File.Copy(Path.Combine(large,stem+".jpg"),p.Image,true);}else{using(var original=AdvancedAudit.Upright(sources[p.Id-1].Item1,p.Ocr.file.EndsWith("b.jpg")))FullJpeg(original,p.FullImage);p.ProcessingNote+="; сохранено первичное OCR";}
                Parse(p,false);var scanFields=new Photo{Ocr=p.ScanOcr,ManualInventory=p.ManualInventory,InventoryOverride=p.InventoryOverride};Parse(scanFields,false);skippedNumeric[i]=new HashSet<string>();
                if(adaptiveNumeric&&p.Kind=="ИУЛ")foreach(string field in new[]{"CRC","Page","Revision"})if(AdvancedAudit.StableWholeReading(new[]{baseline[p.Id][field],uncleanedReadings[i][field],Field(scanFields,field)}))skippedNumeric[i].Add(field);
                cropArray[i]=AdvancedAudit.Crops(p,crops,skippedNumeric[i]);int n=Interlocked.Increment(ref cropDone);progress(55+(int)(5.0*n/photos.Count),"Увеличенные фрагменты: "+n+" / "+photos.Count);
            ParallelWork.RelieveMemory();});var requests=cropArray.SelectMany(c=>c).ToList();
            timing.Mark("Повторное OCR фрагментов");done=0;NativeWindows.Recognize(crops,cropJson,ocrWorkers,false,s=>progress(60+(int)(27.0*Interlocked.Increment(ref done)/Math.Max(1,requests.Count)),"Повторное OCR фрагментов: "+s),cancel);
            timing.Mark("Номера листов");string numbers=Path.Combine(work,"numbers"),numberJson=Path.Combine(work,"numbers-json");Directory.CreateDirectory(numbers);Directory.CreateDirectory(numberJson);
            ParallelWork.For(photos.Count,imageWorkers,cancel,i=>{var numeric=Pagination.Cells(photos[i],cropArray[i],crops,r=>ReadOcr(cropJson,r.File)).Concat(Pagination.RevisionCells(photos[i],cropArray[i],crops,r=>ReadOcr(cropJson,r.File))).ToList();foreach(var req in numeric){File.Move(Path.Combine(crops,req.File),Path.Combine(numbers,req.File));cropArray[i].Add(req);}});
            done=0;NativeWindows.Recognize(numbers,numberJson,ocrWorkers,false,s=>progress(87,"Цифровые ячейки: "+s),cancel);
            foreach(var file in Directory.EnumerateFiles(numbers)){File.Move(file,Path.Combine(crops,Path.GetFileName(file)));}foreach(var file in Directory.EnumerateFiles(numberJson)){File.Move(file,Path.Combine(cropJson,Path.GetFileName(file)));}
            timing.Mark("Объединение чтений");ParallelWork.For(photos.Count,imageWorkers,cancel,i=>{var p=photos[i];var local=cropArray[i];var scanned=new Photo{Id=p.Id,Ocr=p.ScanOcr,Image=p.Image};Parse(scanned,false);
                foreach(var field in new[]{"CRC","Page","Revision"}) {
                    if(field=="Revision"&&!AdvancedAudit.RevisionBounds(p.Ocr).HasValue)continue;
                    var reads=new List<Tuple<string,string>>{Tuple.Create("whole-2200",baseline[p.Id][field]),Tuple.Create("whole-3000",uncleanedReadings[i][field]),Tuple.Create("whole-scan-3000",Field(scanned,field))};
                    foreach(var req in local.Where(c=>c.Field==(field=="Page"?"Footer":field)||c.Field==(field=="Page"?"PageCell":field=="Revision"?"RevisionCell":""))) {
                        var ocr=ReadOcr(cropJson,req.File);string value=req.Field=="PageCell"||req.Field=="RevisionCell"?Pagination.Number(ocr.text):field=="CRC"?AdvancedAudit.Crc(ocr.text):field=="Revision"?AdvancedAudit.Revision(ocr):AdvancedAudit.Footer(ocr,"ЛИСТ");reads.Add(Tuple.Create(req.File,value));
                    }
                    if(local.Any(c=>c.Field==(field=="Page"?"Footer":field))||skippedNumeric[i].Contains(field)){
                        var reading=AdvancedAudit.Consensus(field,reads);
                        if(field=="CRC"){
                            var english=local.Where(c=>c.Field=="CRC").Select(c=>new {Request=c,Ocr=ReadOcr(cropJson,c.File)}).Where(c=>(c.Ocr.language??"").StartsWith("en",StringComparison.OrdinalIgnoreCase)).Select(c=>Tuple.Create(c.Request.File+" (en)",AdvancedAudit.Crc(c.Ocr.text))).ToList();
                            reading=AdvancedAudit.CrcConsensus(reads,english);
                        }
                        p.Readings.Add(reading);
                    }
                }
                foreach(var kind in new[]{"Roles","Approval","Sections","Inventory"}) {
                    var alternatives=local.Where(c=>c.Field==kind).Select(c=>Tuple.Create(c,ReadOcr(cropJson,c.File))).OrderByDescending(c=>Score(c.Item2)).ToList();if(alternatives.Count==0)continue;
                    var selected=alternatives[0];if(kind=="Roles"||kind=="Approval"){
                        var a=selected.Item1.Bounds;p.DetailWords=(p.DetailWords??p.Words).Where(w=>!a.Contains((float)(w.x+w.width/2),(float)(w.y+w.height/2))).Concat(AdvancedAudit.Map(selected.Item2,selected.Item1)).ToList();
                    }else if(kind=="Sections")p.SectionText=selected.Item2.text;
                    else {
                        var req=selected.Item1;var ocr=selected.Item2;var mapped=ocr.lines.Select(l=>new Line{text=l.text,words=l.words.Select(w=>new Word{text=w.text,x=req.Bounds.X+w.x/req.Scale,y=req.Bounds.Y+w.y/req.Scale,width=w.width/req.Scale,height=w.height/req.Scale}).ToList()}).ToList();
                        var retained=p.Ocr.lines.Where(l=>l.words.Count>0&&!req.Bounds.Contains((float)l.words.Average(w=>w.x),(float)l.words.Average(w=>w.y))).ToList();p.Ocr.lines=retained.Concat(mapped).OrderBy(l=>l.words.Average(w=>w.y)).ToList();p.Ocr.text=string.Join("\n",p.Ocr.lines.Select(l=>l.text));
                    }
                }
                foreach(var c in local){File.Copy(Path.Combine(crops,c.File),Path.Combine(assets,c.File),true);File.Delete(Path.Combine(crops,c.File));File.Copy(Path.Combine(cropJson,Path.GetFileNameWithoutExtension(c.File)+".json"),Path.Combine(assets,Path.GetFileNameWithoutExtension(c.File)+".json"),true);}
                ApplyReadings(p);File.WriteAllText(Path.Combine(assets,"p"+p.Id.ToString("D4")+".json"),Json.Serialize(p.Ocr),new UTF8Encoding(false));
            });
            timing.Mark("Подписи, печати и правила");progress(89,"Подписные ячейки, печати и правила документов");var result=Analyze(photos,registry,progress,cancel,visualWorkers);
            timing.Mark("Сохранение отчёта");ParallelWork.For(photos.Count,visualWorkers,cancel,i=>FormAnalysis.SaveSignatureCrops(photos[i],assets));
            SaveResult(result,root,registry);if(File.Exists(Path.Combine(root,"Ошибка.txt")))File.Delete(Path.Combine(root,"Ошибка.txt"));cancel.ThrowIfCancellationRequested();
            if(Path.GetFullPath(work).StartsWith(root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(work)==".ocr-work")Directory.Delete(work,true);
            var stages=timing.Finish();File.WriteAllText(Path.Combine(root,"Время_проверки.json"),Json.Serialize(new {Version="1.8.0",Seconds=timing.Seconds,RequestedWorkers=requestedWorkers,ImageWorkers=imageWorkers,OcrWorkers=ocrWorkers,VisualWorkers=visualWorkers,AdaptiveNumeric=adaptiveNumeric,StableFieldsWithoutExtraCrops=skippedNumeric.Sum(fields=>fields.Count),Stages=stages}),new UTF8Encoding(false));
            progress(100,"Готово за "+TimeSpan.FromSeconds(timing.Seconds).ToString(@"mm\:ss")+": "+result.Images+" страниц / изображений, "+result.Findings.Count+" пунктов для просмотра");return result;
        }
        public static AuditResult Reanalyze(string root,string registry){
            var photos=Recheck.Load(root);
            var result=Analyze(photos,registry);foreach(var photo in photos)FormAnalysis.SaveSignatureCrops(photo,Path.Combine(root,"assets"));SaveResult(result,root,registry);return result;
        }
        internal static void SaveResult(AuditResult result,string root,string registry){
            result.Directory=root;result.Report=Path.Combine(root,"Отчет.html");WriteReport(result,registry);
            File.WriteAllText(Path.Combine(root,"Результаты.json"),Json.Serialize(new {result.Images,result.Volumes,result.Iul,result.MainTitles,result.InventoryPages,result.InventoryOnly,result.RecheckMode,result.RecheckPages,result.CrcMatches,result.CrcCompared,result.RevisionMatches,result.RevisionCompared,Inventory=result.Inventory.Select(i=>new{i.Code,i.Tom,i.Book,i.Revision,i.Box,i.DocumentationKind,Photo=i.Photo.Id}),Findings=result.Findings,Photos=result.Photos.Select(p=>new{p.Id,p.Original,ImageFile=Path.GetFileName(p.Image),p.Kind,p.Code,p.Tom,p.Book,p.Chief,p.Organization,p.ManualInventory,p.InventoryOverride,p.CRC,p.Revision,p.Page,p.Pages,p.Ink,p.SignatureRows,p.RowsWithoutBlue,p.ProcessingNote,p.KindHint,p.CodeHint,p.OrganizationHint,p.Readings,p.Signatures,p.Sections,p.Seal,p.DetailWords,p.SectionText})}),new UTF8Encoding(false));
        }
        static string H(object s){return HttpUtility.HtmlEncode(s==null?"":Convert.ToString(s,Inv));}
        static string ImageHtml(Photo p){return "<figure><a href='assets/p"+p.Id.ToString("D4")+".jpg' target='_blank'><img loading='lazy' src='assets/p"+p.Id.ToString("D4")+".jpg'></a><figcaption>"+H(p.Original)+"<br>"+H(p.Kind)+(p.ManualInventory?" (указана вручную)":"")+"; "+H(p.Code)+"</figcaption></figure>";}
        static string EvidenceHtml(Photo p) {
            var b=new StringBuilder("<details><summary>"+p.Id+". "+H(p.Kind)+" — "+H(p.Code)+"</summary><p>"+H(p.ProcessingNote)+"</p>");
            string scanPath=Path.Combine(Path.GetDirectoryName(p.Image),"p"+p.Id.ToString("D4")+"-scan.png");if(File.Exists(scanPath))b.Append("<p><a target='_blank' href='assets/p"+p.Id.ToString("D4")+".jpg'>Исходная фотография</a> · <a target='_blank' href='assets/p"+p.Id.ToString("D4")+"-scan.png'>Очищенная страница</a></p><details><summary>Страница после очистки фона</summary><figure><a target='_blank' href='assets/p"+p.Id.ToString("D4")+"-scan.png'><img loading='lazy' src='assets/p"+p.Id.ToString("D4")+"-scan.png'></a><figcaption>Освещение и фон выровнены; исходные цветные области используются для проверки подписей и печатей.</figcaption></figure></details>");
            if(p.Readings.Count>0){b.Append("<table><tr><th>Поле</th><th>Результат</th><th>Согласие чтений</th><th>Полученные чтения</th></tr>");foreach(var v in p.Readings)b.Append("<tr><td>"+H(FieldLabel(v.Field))+"</td><td>"+H(v.Value??"Значение не получено")+" — "+H(v.Status)+"</td><td>"+v.Agreement+" / "+v.Attempts+"</td><td>"+H(v.Sources.Count==0?"Нет прочитанных значений":string.Join("; ",v.Sources))+"</td></tr>");b.Append("</table><details><summary>Увеличенные фрагменты OCR</summary><div class='proof'>");foreach(var file in Directory.GetFiles(Path.GetDirectoryName(p.Image),"p"+p.Id.ToString("D4")+"-*").Where(f=>(f.EndsWith(".jpg")||f.EndsWith(".png"))&&!f.Contains("signature")&&!f.Contains("processed")&&!f.Contains("-scan")))b.Append("<figure><a target='_blank' href='assets/"+H(Path.GetFileName(file))+"'><img loading='lazy' src='assets/"+H(Path.GetFileName(file))+"'></a><figcaption>"+H(Path.GetFileName(file))+"</figcaption></figure>");b.Append("</div></details>");}
            if(p.Seal!=null&&p.Kind.StartsWith("Титул"))b.Append("<p>Печать: "+H(p.Seal.Status)+"; доля обнаруженных участков кругового контура: "+p.Seal.Score.ToString("0.00",Inv)+".</p>");
            b.Append("<table><tr><th>Роль</th><th>Фамилия</th><th>Признак подписи</th><th>Область</th></tr>");int n=0;foreach(var sig in p.Signatures){string image="p"+p.Id.ToString("D4")+"-signature-"+(n++)+".jpg";b.Append("<tr><td>"+H(sig.Role)+"</td><td>"+H(sig.Name)+"</td><td>"+H(sig.Status)+"; цветных пикселей: "+sig.BluePixels+", чёрных: "+sig.BlackPixels+"</td><td><a href='assets/"+image+"' target='_blank'>Открыть "+(sig.TableCell?"ячейку":"область строки")+"</a></td></tr>");}b.Append("</table><p>Заголовки: "+H(string.Join("; ",p.Sections.Select(v=>v.Key+": "+v.Value)))+"</p></details>");return b.ToString();
        }
        static void WriteReport(AuditResult r,string registry){
            var b=new StringBuilder("<!doctype html><html lang='ru'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>Автопроверка документов</title><style>body{font:16px/1.5 Segoe UI,Arial;margin:0;background:#f3f5f7;color:#1c2e41}main{max-width:1260px;margin:32px auto;padding:0 24px}h1{font-size:30px}h2{font-size:22px}section,article{background:white;border:1px solid #dce3ea;border-radius:8px;padding:20px;margin:16px 0}small,.muted{color:#5a6b7c}.metrics{display:flex;gap:20px;flex-wrap:wrap}.metrics span{background:#eaf0f5;padding:12px 18px;border-radius:6px}table{border-collapse:collapse;width:100%;font-size:14px}th,td{padding:10px;text-align:left;border-bottom:1px solid #dce3ea;vertical-align:top}th{background:#234564;color:white}.proof{display:flex;gap:14px;flex-wrap:wrap}figure{margin:10px 0;width:280px}img{width:100%;border:1px solid #dce3ea}figcaption{font-size:12px;word-break:break-word}.level{font-weight:bold;color:#a03d22}button,select{font:inherit;padding:8px 12px}details summary{cursor:pointer;color:#234564}pre{white-space:pre-wrap;font-size:13px}a{color:#235e7a}@media print{body{background:white}button,select{display:none}}</style><main>");
            b.Append("<h1>Автопроверка документов</h1><p>Результат распознавания и предварительной сверки. Каждый пункт сопровождается исходными снимками.</p><div class='metrics'><span>Изображений: <b>"+r.Images+"</b></span><span>Томов распознано: <b>"+r.Volumes+"</b></span><span>ИУЛ: <b>"+r.Iul+"</b></span><span><a href='#inventory-photos'>Фотографий описи: <b>"+r.InventoryPages+"</b></a></span><span>Пунктов для просмотра: <b>"+r.Findings.Count+"</b></span></div>");
            if(r.RecheckMode!=null)b.Append("<section><h2>Повторная проверка</h2><p>"+H(r.RecheckMode)+". Повторное OCR: "+(r.RecheckPages.Count==0?"не выполнялось":H(string.Join(", ",r.RecheckPages)))+". Остальные страницы — по сохранённому OCR. Исходные PDF и фотографии не открывались.</p></section>");
            if(r.InventoryOnly)b.Append("<section><h2>Режим: только опись</h2><p>Опись распознана для заполнения таблицы Excel. Титулы, ИУЛ, подписи, печати и CRC32 по документам не проверялись.</p><p>Файл проверки: "+H(registry)+"</p></section>");
            else b.Append("<section><h2>Результат сверки</h2><p>CRC32: совпало "+r.CrcMatches+" из "+r.CrcCompared+" распознанных значений на листах ИУЛ. Изменения: совпало "+r.RevisionMatches+" из "+r.RevisionCompared+" сравнений с описью.</p><p class='muted'>Контрольная сумма сравнивается по напечатанному тексту и реестру, без пересчёта исходных PDF. Графа «Листов» не используется для замечаний или вывода о комплектности ИУЛ. Подписные строки анализируются отдельно: цветные и чёрные штрихи, границы ячеек и удаление печатного текста. Круглая печать определяется по контуру. Результат является визуальным признаком, не подтверждением подлинности; пересечение печати и подписи требует просмотра.</p><p>Реестр: "+H(registry)+"</p></section>");
            b.Append("<section id='inventory-photos'><h2>Фотографии описи</h2><p>Распознано страниц описи: "+r.InventoryPages+". Строк в сводной описи: "+r.Inventory.Count+". Продолжения таблицы распознаются по заголовкам граф.</p><div class='proof'>");
            foreach(var photo in r.Photos.Where(p=>p.Kind=="Опись")){b.Append("<div id='inventory-photo-"+photo.Id+"'>"+ImageHtml(photo)+"<p>Распознанных строк: "+r.Inventory.Count(i=>i.Photo.Id==photo.Id)+".</p></div>");}
            if(r.InventoryPages==0)b.Append("<p>Фотографии описи не определены среди загруженных страниц.</p>");b.Append("</div></section>");
            b.Append("<h2>Возможные ошибки и неполные данные</h2><label>Показать: <select id='filter'><option value='Все'>Все пункты</option><option>Расхождение</option><option>Проверить</option><option>Наблюдение</option></select></label>");
            int n=0;foreach(var f in r.Findings.OrderBy(f=>f.Level=="Расхождение"?0:f.Level=="Проверить"?1:2)){n++;b.Append("<article data-level='"+H(f.Level)+"'><span class='level'>"+H(f.Level)+"</span><h2>"+n+". "+H(f.Code)+" — "+H(f.Topic)+"</h2><p>"+H(f.Detail)+"</p><details><summary>Открыть фотографии</summary><div class='proof'>");foreach(int id in f.Photos.Distinct())b.Append(ImageHtml(r.Photos.First(p=>p.Id==id)));b.Append("</div></details></article>");}
            b.Append("<section><h2>Распознанная опись</h2><table><tr><th>Том</th><th>Шифр</th><th>Название книги</th><th>Изм.</th><th>Вид</th><th>Короб</th><th>Фотография описи</th></tr>");foreach(var i in r.Inventory)b.Append("<tr><td>"+H(i.Tom)+"</td><td>"+H(i.Code)+"</td><td>"+H(i.Book)+"</td><td>"+H(i.Revision)+"</td><td>"+H(i.DocumentationKind)+"</td><td>"+H(i.Box)+"</td><td><a href='#inventory-photo-"+i.Photo.Id+"'>Фото "+i.Photo.Id+"</a></td></tr>");b.Append("</table></section>");
            b.Append("<section><h2>Повторные чтения и визуальные проверки</h2>");foreach(var photo in r.Photos)b.Append(EvidenceHtml(photo));b.Append("</section>");b.Append("<section><h2>Все изображения и распознанные поля</h2>");foreach(var p in r.Photos){b.Append("<details><summary>"+p.Id+". "+H(p.Original)+" — "+H(p.Kind)+" — "+H(p.Code)+"</summary><p>Книга: "+H(p.Book)+". Главный инженер: "+H(p.Chief)+". CRC32: "+H(p.CRC)+". Изм.: "+H(p.Revision)+". Лист: "+H(p.Page)+". Строк подписей: "+p.SignatureRows+"; неподтверждённых подписей: "+p.RowsWithoutBlue+".</p><div class='proof'>"+ImageHtml(p)+"</div><details><summary>Текст OCR</summary><pre>"+H(p.Ocr.text)+"</pre></details></details>");}b.Append("</section><script>document.getElementById('filter').onchange=function(){document.querySelectorAll('article[data-level]').forEach(a=>a.hidden=this.value!=='Все'&&a.dataset.level!==this.value)}</script></main></html>");File.WriteAllText(r.Report,b.ToString(),new UTF8Encoding(false));
        }
    }
}
