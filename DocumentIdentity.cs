using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace PhotoAudit {
    // Codes are field values. No prefix, alphabet or numbering scheme is required.
    public static class DocumentIdentity {
        public sealed class Row {public string Code,Volume;public double Y;}
        public static string Value(string text){
            string s=(text??"").Normalize(NormalizationForm.FormKC).Replace('–','-').Replace('—','-').Trim();
            s=Regex.Replace(s,@"\s+"," ").Trim(' ','"','«','»');
            if(s.Length==0||s.Length>4096||Regex.IsMatch(s,@"^(?:Без\s*шифра|Не\s*определено|Не\s*распознано)$",RegexOptions.IgnoreCase))return null;
            return s.ToUpperInvariant();
        }
        public static string Key(string code){return Regex.Replace(Value(code)??"",@"\s+","");}
        static List<Word> Words(OcrPage p){return (p.lines??new List<Line>()).SelectMany(l=>l.words??new List<Word>()).ToList();}
        static string Cell(List<Word> words){
            var lines=new List<List<Word>>();foreach(var w in words.OrderBy(w=>w.y).ThenBy(w=>w.x)){
                var line=lines.FirstOrDefault(l=>Math.Abs(l.Average(v=>v.y+v.height/2)-(w.y+w.height/2))<Math.Max(w.height,l.Average(v=>v.height))*.65);
                if(line==null){line=new List<Word>();lines.Add(line);}line.Add(w);
            }
            string result="";foreach(var line in lines.OrderBy(l=>l.Average(w=>w.y))){string part=string.Join(" ",line.OrderBy(w=>w.x).Select(w=>w.text));result+=(result.Length==0||result.EndsWith("-")||result.EndsWith("/")?"":" ")+part;}return Value(result);
        }
        static double? Rule(Bitmap image,OcrPage page,double from,double to,double y,double height,bool last){
            double scale=image.Width/(double)page.width;int min=Math.Max(0,(int)(from*scale)),max=Math.Min(image.Width-1,(int)(to*scale));var positions=new List<int>();
            for(int x=min;x<=max;x++){int dark=0,total=0;for(int i=0;i<30;i++){int yy=(int)((y+height*i/29)*scale);if(yy<0||yy>=image.Height)continue;var c=image.GetPixel(x,yy);if((c.R+c.G+c.B)/3<175)dark++;total++;}if(total>=20&&dark>=total*.8)positions.Add(x);}
            return positions.Count==0?(double?)null:(last?positions.Last():positions.First())/scale;
        }
        public static List<Row> InventoryRows(OcrPage page,string image=null){
            var ws=Words(page);var marker=ws.Where(w=>Regex.IsMatch(w.text??"",@"^(?:ШИФР|ОБОЗНАЧЕНИЕ)",RegexOptions.IgnoreCase)).OrderBy(w=>w.y).FirstOrDefault();if(marker==null)return new List<Row>();
            var name=ws.Where(w=>w.x>marker.x&&Math.Abs(w.y-marker.y)<page.height*.04&&Regex.IsMatch(w.text??"",@"^НАИМЕНОВАНИЕ",RegexOptions.IgnoreCase)).OrderBy(w=>w.x).FirstOrDefault();if(name==null)return new List<Row>();
            double left=Math.Max(0,marker.x-page.width*.055),right=name.x-page.width*.015,bottom=marker.y+marker.height*2;
            if(image!=null&&File.Exists(image))using(var bitmap=new Bitmap(image)){
                var l=Rule(bitmap,page,marker.x-page.width*.14,marker.x-3,marker.y-marker.height,marker.height*7,true);
                var r=Rule(bitmap,page,marker.x+marker.width+3,name.x-3,marker.y-marker.height,marker.height*7,false);
                if(l.HasValue)left=l.Value+2;if(r.HasValue)right=r.Value-2;
            }
            else {var body=ws.Where(w=>w.x>marker.x+marker.width&&w.y>bottom&&Regex.IsMatch(w.text??"",@"^(?:Книга|Часть)$",RegexOptions.IgnoreCase)).OrderBy(w=>w.x).FirstOrDefault();if(body!=null)right=Math.Min(right,body.x-page.width*.01);}
            if(right<=left)return new List<Row>();
            var volume=ws.Where(w=>w.x<left&&w.x>page.width*.08&&w.y>bottom&&Regex.IsMatch(w.text??"",@"^\d+(?:[.]\d+){1,6}$")).OrderBy(w=>w.y).ToList();
            var numbers=ws.Where(w=>w.x<page.width*.10&&w.y>bottom&&Regex.IsMatch(w.text??"",@"^\d{1,4}[.]?$" )).OrderBy(w=>w.y).ToList();
            var anchors=numbers.Count>0?numbers:volume;var result=new List<Row>();
            for(int i=0;i<anchors.Count;i++){
                var a=anchors[i];double top=numbers.Count>0?a.y-a.height: i==0?bottom:(anchors[i-1].y+a.y)/2;
                double end=i+1<anchors.Count?(numbers.Count>0?anchors[i+1].y-anchors[i+1].height:(a.y+anchors[i+1].y)/2):page.height*.95;
                var cell=ws.Where(w=>w.x+w.width/2>=left&&w.x+w.width/2<right&&w.y>=top&&w.y<end).ToList();string code=Cell(cell);
                if(code==null||Regex.IsMatch(code,@"^(?:ШИФР|ТОМА|ДОКУМЕНТ|\d{1,2})$",RegexOptions.IgnoreCase)&&cell.All(w=>w.y<bottom+marker.height*2))continue;
                var v=volume.FirstOrDefault(w=>w.y>=top&&w.y<end);result.Add(new Row{Code=code,Volume=v==null?null:v.text,Y=cell.Min(w=>w.y)});
            }
            return result;
        }
        public static string FieldCode(OcrPage page,string image,out bool strong){
            strong=false;
            var ws=Words(page);
            foreach(var line in page.lines??new List<Line>()){
                string t=line.text??"";var inline=Regex.Match(t,@"(?:Обозначение\s+документа|Шифр(?:\s+(?:документа|тома))?)\s*[:=]\s*(?<v>.+)$",RegexOptions.IgnoreCase);
                if(inline.Success){strong=true;return Value(inline.Groups["v"].Value);}
                var label=(line.words??new List<Word>()).FirstOrDefault(w=>Regex.IsMatch(w.text??"",@"^(?:Обозначение(?:документа)?|Шифр)$",RegexOptions.IgnoreCase));if(label==null)continue;
                if((label.text??"").StartsWith("Шифр",StringComparison.OrdinalIgnoreCase)&&ws.Any(w=>w.x<label.x&&Math.Abs(w.y-label.y)<page.height*.025&&Regex.IsMatch(w.text??"",@"^(?:Наименование|Название)$",RegexOptions.IgnoreCase)))continue;
                // A table header has its value underneath, bounded by the next column header.
                double end=(line.words??new List<Word>()).Max(w=>w.y+w.height);
                var next=ws.Where(w=>w.x>label.x+page.width*.10&&Math.Abs(w.y-label.y)<page.height*.03&&Regex.IsMatch(w.text??"",@"^(?:Наименование|Номер|Вид)$",RegexOptions.IgnoreCase)).OrderBy(w=>w.x).FirstOrDefault();
                double right=next==null?Math.Min(page.width,label.x+page.width*.35):next.x-page.width*.01;
                if(next!=null&&image!=null&&File.Exists(image))using(var bitmap=new Bitmap(image)){var rule=Rule(bitmap,page,label.x+label.width+3,next.x-3,label.y-label.height,label.height*7,false);if(rule.HasValue){right=rule.Value-2;strong=true;}}
                var candidates=ws.Where(w=>w.x>=Math.Max(0,label.x-page.width*.025)&&w.x+w.width/2<right&&w.y>end+2&&w.y<end+page.height*.12&&!Regex.IsMatch(w.text??"",@"^(?:документа|тома)$",RegexOptions.IgnoreCase)).OrderBy(w=>w.y).ToList();
                if(candidates.Count>0){
                    double first=candidates[0].y;double lineHeight=Math.Max(6,candidates[0].height);
                    var firstLine=candidates.Where(w=>w.y<first+lineHeight*.75).OrderBy(w=>w.x).ToList();
                    for(int i=1;i<firstLine.Count;i++)if(firstLine[i].x-(firstLine[i-1].x+firstLine[i-1].width)>Math.Max(page.width*.025,lineHeight*2)){firstLine=firstLine.Take(i).ToList();break;}
                    string value=Cell(firstLine);
                    int wraps=0;double endLine=firstLine.Max(w=>w.y+w.height);
                    while(value!=null&&(value.EndsWith("-")||value.EndsWith("/"))&&wraps++<3){var rest=candidates.Where(w=>w.y>=endLine).ToList();if(rest.Count==0)break;double y=rest[0].y;var nextLine=rest.Where(w=>w.y<y+Math.Max(6,rest[0].height)*.75).ToList();value=Value(value+Cell(nextLine));endLine=nextLine.Max(w=>w.y+w.height);}
                    return value;
                }
            }
            return null;
        }
        public static string PageCode(OcrPage page,string image=null){bool strong;return FieldCode(page,image,out strong)??TitleCode(page);}
        public static string TitleCode(OcrPage page){
            var ws=(page.lines??new List<Line>()).Where(l=>l.words!=null&&l.words.Count==1).SelectMany(l=>l.words).ToList();
            // An unlabelled title code needs stronger typography and placement evidence.
            var tokens=ws.Where(w=>w.y>page.height*.45&&w.y<page.height*.83&&w.x>page.width*.20&&w.x<page.width*.75&&Regex.IsMatch(w.text??"",@"^(?=.*\p{L})(?=.*\d)[\p{L}\p{N}][\p{L}\p{N}._/\-]{3,}$")&&!Regex.IsMatch(w.text,@"^(?:CRC|ISBN)",RegexOptions.IgnoreCase)).Select(w=>Value(w.text)).Distinct().ToList();
            return tokens.Count==1?tokens[0]:null;
        }
        public sealed class Matcher {
            sealed class Entry {public string Code,Key;public Regex Pattern;}
            readonly List<Entry> entries;
            public Matcher(IEnumerable<string> codes){entries=codes.Where(c=>Value(c)!=null).Distinct().Select(c=>new Entry{Code=c,Key=Key(c),Pattern=new Regex(@"(?<![\p{L}\p{N}])"+string.Join(@"\s*",Key(c).Select(ch=>Regex.Escape(ch.ToString())))+@"(?![\p{L}\p{N}.])",RegexOptions.CultureInvariant)}).OrderByDescending(e=>e.Key.Length).ToList();}
            public string Match(string file){
                string value=Value(file);if(value==null)return null;value=Regex.Replace(value,@"\.(?:PDF|DOCX?|XLSX?|DWG|ZIP)$","");string compact=Key(value);var found=new List<Entry>();
                foreach(var entry in entries){if(found.Count>0&&entry.Key.Length<found[0].Key.Length)break;if(compact.Contains(entry.Key)&&entry.Pattern.IsMatch(value))found.Add(entry);}
                return found.Count==1?found[0].Code:null;
            }
        }
        public static string MatchFile(string file,IEnumerable<string> codes){return new Matcher(codes).Match(file);}
        public static string MatchPage(OcrPage page,IEnumerable<string> codes){
            var matches=new HashSet<string>();var matcher=new Matcher(codes.Where(c=>Key(c).Length>=4));foreach(var line in page.lines??new List<Line>()){
                var ws=line.words??new List<Word>();if(ws.Count==0||ws.All(w=>w.y<page.height*.12))continue;
                string text=string.Join(" ",ws.OrderBy(w=>w.x).Select(w=>w.text));string match=matcher.Match(text);if(match!=null){string key=Key(text),code=Key(match);if(!Regex.IsMatch(code,@"^\d+$")&&(key==code||key==code+"-УЛ"||Regex.IsMatch(key,"^"+Regex.Escape(code)+@"\(?ИЗМ[.0-9)]+$")))matches.Add(match);}
            }
            return matches.Count==1?matches.First():null;
        }
    }
}
