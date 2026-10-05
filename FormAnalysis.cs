using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhotoAudit {
    public static class FormAnalysis {
        static string N(string s){return Regex.Replace((s??"").ToUpperInvariant().Replace('Ё','Е'),@"\s+"," ").Trim();}
        public static string Organization(string text){string value=Regex.Replace(N(text),@"[^А-Я]","");return value.Contains("ЛЕНГИПРОТР")?"Ленгипротранс":value.Contains("РОСЖЕЛДОР")||value.Contains("ГИПР")&&value.Contains("ТРАНСПУТ")?"Росжелдорпроект":value.Contains("ЖЕЛДОРПРОЕКТ")?"Желдорпроект":"Не определено";}
        public static string DocumentOrganization(OcrPage page){var words=(page.lines??new List<Line>()).SelectMany(l=>l.words??new List<Word>()).Where(w=>w.y<page.height*.17).OrderBy(w=>w.y).ThenBy(w=>w.x);return Organization(string.Join(" ",words.Select(w=>w.text)));}
        public static Dictionary<string,string> Sections(string text) {
            var result=new Dictionary<string,string>();string t=Regex.Replace(text??"",@"-\s*\r?\n(?=[а-я])","");
            var matches=Regex.Matches(t,@"(?<![А-Яа-я])(?<type>Подраздел|Раздел|Часть)\s*(?<number>\d+)\s*[.,:]?\s*(?<name>[\s\S]*?)(?=(?:Подраздел|Раздел|[Ч\[:]асть|[К\[:]нига)\b|[Т•]ом\s*\d|\b\d{3}-\d{2}-\d{4}\b|CRC32|Алгоритм|Наименование файла|Главный|Заместитель|$)",RegexOptions.IgnoreCase);
            foreach(Match m in matches)result[N(m.Groups["type"].Value)]=m.Groups["number"].Value+". "+Regex.Replace(m.Groups["name"].Value,@"\s+"," ").Trim(' ','.');return result;
        }
        public static Dictionary<string,string> InventorySections(Photo p,InventoryEntry item) {
            double row=item.AnchorY;
            var lines=p.Ocr.lines.Where(l=>l.words!=null&&l.words.Count>0&&l.words.Average(w=>w.y)<=row+5).Select(l=>l.text);
            return Sections(string.Join("\n",lines));
        }
        public static List<SignatureCheck> Roles(Photo p) {
            var ws=p.DetailWords!=null&&p.DetailWords.Count>0?p.DetailWords:p.Words;double w=p.Ocr.width,h=p.Ocr.height;bool iul=p.Kind=="ИУЛ";
            bool approval=!iul&&N(p.Ocr.text).Contains("СОГЛАСОВАНО");
            var anchors=ws.Where(t=>t.x<w*(iul?.28:.48)&&(t.y>h*(iul?.05:.65)||approval&&t.y>h*.12&&t.y<h*.30)&&t.y<h*.92&&Regex.IsMatch(N(t.text),@"^(?:Г?ЛАВНЫЙ|ВЕДУЩИЙ|ЗАМЕСТИТЕЛЬ|НАЧАЛЬНИК|НОРМОКОНТРОЛЬ|РУКОВОДИТЕЛЬ|ИНЖЕНЕР)$")).OrderBy(t=>t.y).ToList();
            var starts=new List<Word>();foreach(var a in anchors)if(starts.Count==0||a.y-starts.Last().y>h*.025)starts.Add(a);
            var roles=new List<SignatureCheck>();
            for(int k=0;k<starts.Count;k++) {
                var a=starts[k];double end=Math.Min(k+1<starts.Count?starts[k+1].y-h*.008:h*.94,a.y+h*.13);
                var roleWords=ws.Where(t=>t.x<w*(iul?.30:.45)&&t.y>=a.y-h*.006&&t.y<Math.Min(end,a.y+h*.07)).OrderBy(t=>t.y).ThenBy(t=>t.x).ToList();
                string label=N(string.Join(" ",roleWords.Select(t=>t.text)));
                bool project=Regex.IsMatch(label,@"(?:^|\s)[ПТ]?РОЕ(?:КТ[АВ])?(?:\s|$)");
                string role=label.Contains("ИНЖЕНЕ")&&project?"Главный инженер проекта":label.Contains("ЛАВН")&&label.Contains("ИНЖЕНЕ")?"Главный инженер":label.StartsWith("НОРМОКОНТРОЛЬ")?"Нормоконтроль":label.Contains("ЗАМЕСТИТЕЛЬ")?"Заместитель":label.Contains("НАЧАЛЬНИК")?"Начальник":label.Contains("ВЕДУЩИЙ")?"Ведущий инженер":label.Contains("РУКОВОДИТЕЛЬ")?"Руководитель":"Инженер";
                bool isApproval=approval&&a.y<h*.30;if(isApproval){end=Math.Min(end,a.y+h*.11);var heading=ws.Where(t=>t.y>a.y&&t.y<h*.45&&Regex.IsMatch(N(t.text),@"^(?:СОЗДАНИЕ|ПРОЕКТНАЯ|РАЗДЕЛ|ВЫСОКОСКОРОСТНОЙ)$")).OrderBy(t=>t.y).FirstOrDefault();if(heading!=null)end=Math.Min(end,heading.y-h*.005);}double xmin=iul?w*.23:isApproval?w*.22:w*.64,xmax=iul?w*.55:isApproval?w*.75:w;
                var names=ws.Where(t=>t.x>=xmin&&t.x<xmax&&t.y>=a.y+(isApproval?h*.025:-h*.012)&&t.y<end&&Regex.IsMatch(t.text??"",@"^(?:[А-Яа-яЁё][.,]){0,2}[А-Яа-яЁё]{4,}[,.;]?$" )).OrderBy(t=>isApproval?-t.y:t.y).ThenBy(t=>t.x).ToList();
                if(names.Count>0&&!isApproval){double firstY=names.Min(t=>t.y),height=names.Average(t=>t.height);names=names.Where(t=>t.y<=firstY+height*.75).OrderBy(t=>t.x).ToList();}
                if(!iul){var anchored=names.Where(t=>Regex.IsMatch(t.text,@"^[А-Яа-яЁё][.,]")||ws.Any(v=>v.x<t.x&&t.x-v.x<w*.15&&Math.Abs(v.y-t.y)<Math.Max(v.height,t.height)*.8&&Regex.IsMatch(v.text??"",@"^(?:[А-Яа-яЁё][.,]){1,2}$"))).ToList();if(anchored.Count>0)names=anchored;else if(isApproval)names=names.Where(t=>ws.Any(v=>v.x<t.x&&t.x-v.x<w*.15&&Math.Abs(v.y-t.y)<Math.Max(v.height,t.height)&&Regex.IsMatch(v.text??"",@"^\.?(?:[А-Яа-яЁё][.,]){1,2}$"))).ToList();}
                roles.Add(new SignatureCheck{Approval=isApproval,Role=role,Name=names.Count==0?null:N(Regex.Match(names[0].text,@"[А-Яа-яЁё]{4,}").Value),Organization=isApproval?Organization(string.Join(" ",ws.Where(t=>t.y>=a.y-h*.01&&t.y<end).Select(t=>t.text))):p.Organization,Bounds=new RectangleF((float)(iul?w*.55:isApproval?w*.07:w*.30),(float)(a.y-h*.010),(float)(iul?w*.28:isApproval?w*.24:w*.34),(float)(end-a.y+h*.008)),Status="Не определено"});
            }
            return roles;
        }
        public static void Inspect(Photo p,string assets) {
            p.Signatures=Roles(p);p.Sections=Sections(p.Ocr.text);foreach(var section in Sections(p.SectionText)){string prior;if(!p.Sections.TryGetValue(section.Key,out prior)||section.Value.Length>=prior.Length*.85)p.Sections[section.Key]=section.Value;}p.Seal=new SealCheck{Status="Не применяется"};
            using(var source=new Bitmap(p.FullImage!=null&&File.Exists(p.FullImage)?p.FullImage:p.Image))using(var im=AdvancedAudit.Resize(source,1400)) {
                var pixels=new Pixels(im);double k=im.Width/(double)p.Ocr.width;
                if(p.Kind.StartsWith("Титул"))p.Seal=Seal(pixels);
                foreach(var role in p.Signatures) {
                    var rect=Rectangle.Round(new RectangleF((float)(role.Bounds.X*k),(float)(role.Bounds.Y*k),(float)(role.Bounds.Width*k),(float)(role.Bounds.Height*k)));
                    rect=Rectangle.Intersect(rect,new Rectangle(0,0,im.Width,im.Height));if(rect.Width<8||rect.Height<8){role.Status="Область обрезана";continue;}
                    if(p.Kind=="ИУЛ")rect=Cell(pixels,rect,out role.TableCell);
                    var mask=new byte[rect.Width*rect.Height];int bg=Background(pixels,rect);
                    var printed=(p.DetailWords??p.Words).Where(t=>t.text.Length>=2&&Regex.IsMatch(t.text,@"[А-Яа-я0-9]{2}" )).Select(t=>Rectangle.Round(new RectangleF((float)(t.x*k-2),(float)(t.y*k-2),(float)(t.width*k+4),(float)(t.height*k+4)))).ToList();
                    var textMask=new bool[mask.Length];foreach(var word in printed){var wordArea=Rectangle.Intersect(word,rect);for(int y=wordArea.Top;y<wordArea.Bottom;y++)for(int x=wordArea.Left;x<wordArea.Right;x++)textMask[(y-rect.Top)*rect.Width+x-rect.Left]=true;}
                    var rowRule=new bool[rect.Height];var columnRule=new bool[rect.Width];
                    for(int y=0;y<rect.Height;y++){int count=0;for(int x=0;x<rect.Width;x+=3)if(pixels.Grey(rect.Left+x,rect.Top+y)<115&&!pixels.Blue(rect.Left+x,rect.Top+y))count++;rowRule[y]=count>rect.Width/3*.70;}
                    for(int x=0;x<rect.Width;x++){int count=0;for(int y=0;y<rect.Height;y+=3)if(pixels.Grey(rect.Left+x,rect.Top+y)<115&&!pixels.Blue(rect.Left+x,rect.Top+y))count++;columnRule[x]=count>rect.Height/3*.75;}
                    for(int y=1;y<rect.Height-1;y++)for(int x=1;x<rect.Width-1;x++) {
                        int xx=x+rect.X,yy=y+rect.Y;bool blue=pixels.Blue(xx,yy),dark=pixels.Grey(xx,yy)<Math.Min(150,bg-45);if(!blue&&!dark)continue;
                        // OCR text and straight table rules are removed only from the black mask.
                        if(!blue&&textMask[y*rect.Width+x])continue;
                        if(!blue&&(rowRule[y]||columnRule[x]))continue;
                        if(p.Kind.StartsWith("Титул")&&p.Seal.Score>.52) {double dx=xx-p.Seal.X,dy=yy-p.Seal.Y;if(Math.Sqrt(dx*dx+dy*dy)<p.Seal.Radius*1.07)continue;}
                        mask[y*rect.Width+x]=(byte)(blue?2:1);
                    }
                    int blueCount=0,blackCount=0;bool handwriting=false;var visited=new bool[mask.Length];
                    for(int i=0;i<mask.Length;i++)if(mask[i]>0&&!visited[i]) {
                        var pending=new Queue<int>();pending.Enqueue(i);visited[i]=true;int count=0,blue=0,minx=rect.Width,miny=rect.Height,maxx=0,maxy=0;
                        while(pending.Count>0){int at=pending.Dequeue(),x=at%rect.Width,y=at/rect.Width;count++;if(mask[at]==2)blue++;minx=Math.Min(minx,x);maxx=Math.Max(maxx,x);miny=Math.Min(miny,y);maxy=Math.Max(maxy,y);
                            foreach(int next in new[]{x>0?at-1:-1,x<rect.Width-1?at+1:-1,y>0?at-rect.Width:-1,y<rect.Height-1?at+rect.Width:-1})if(next>=0&&mask[next]>0&&!visited[next]){visited[next]=true;pending.Enqueue(next);}}
                        int cw=maxx-minx+1,ch=maxy-miny+1;if(count<6||cw<4||ch<3||cw/(double)ch>30)continue;
                        blueCount+=blue;blackCount+=count-blue;
                        if(count-blue>=30&&cw>rect.Width*.10&&ch>Math.Max(6,rect.Height*.12)&&cw/(double)ch<18)handwriting=true;
                    }
                    role.BluePixels=blueCount;role.BlackPixels=blackCount;
                    role.Status=blueCount>=20?"Найдены цветные рукописные штрихи":handwriting?"Найдены чёрные рукописные штрихи":blueCount+blackCount<8?"Ячейка выглядит пустой":"Недостаточно признаков подписи";
                    role.Bounds=new RectangleF((float)(rect.X/k),(float)(rect.Y/k),(float)(rect.Width/k),(float)(rect.Height/k));
                    if(assets!=null)using(var crop=im.Clone(rect,PixelFormat.Format24bppRgb))crop.Save(Path.Combine(assets,"p"+p.Id.ToString("D4")+"-signature-"+p.Signatures.IndexOf(role)+".jpg"),ImageFormat.Jpeg);
                }
                p.SignatureRows=p.Signatures.Count;p.RowsWithoutBlue=p.Signatures.Count(r=>!r.Status.StartsWith("Найдены"));
            }
            // Same-role comparison is separate from the visual signature evidence.
            var chief=p.Signatures.FirstOrDefault(r=>r.Role=="Главный инженер"&&!r.Approval&&r.Name!=null);if(chief!=null)p.Chief=chief.Name;
        }
        static int Background(Pixels p,Rectangle rect){var samples=new List<int>();for(int y=rect.Top;y<rect.Bottom;y+=5)for(int x=rect.Left;x<rect.Right;x+=5)samples.Add(p.Grey(x,y));samples.Sort();return samples[(int)(samples.Count*.85)];}
        public static void SaveSignatureCrops(Photo p,string folder) {
            using(var source=new Bitmap(p.FullImage!=null&&File.Exists(p.FullImage)?p.FullImage:p.Image))using(var im=AdvancedAudit.Resize(source,1400)){
                double k=im.Width/(double)p.Ocr.width;int n=0;
                foreach(var s in p.Signatures){var r=Rectangle.Intersect(Rectangle.Round(new RectangleF((float)(s.Bounds.X*k),(float)(s.Bounds.Y*k),(float)(s.Bounds.Width*k),(float)(s.Bounds.Height*k))),new Rectangle(0,0,im.Width,im.Height));if(r.Width>0&&r.Height>0)using(var crop=im.Clone(r,PixelFormat.Format24bppRgb))crop.Save(Path.Combine(folder,"p"+p.Id.ToString("D4")+"-signature-"+n+".jpg"),ImageFormat.Jpeg);n++;}
            }
        }
        static bool LongRun(Pixels p,int x,int y,Rectangle rect,bool horizontal) {
            if(horizontal){int count=0;for(int a=rect.Left;a<rect.Right;a+=3)if(p.Grey(a,y)<115&&!p.Blue(a,y))count++;return count>rect.Width/3*.70;}
            int n=0;for(int a=rect.Top;a<rect.Bottom;a+=3)if(p.Grey(x,a)<115&&!p.Blue(x,a))n++;return n>rect.Height/3*.75;
        }
        static Rectangle Cell(Pixels p,Rectangle fallback,out bool located) {
            int y0=fallback.Top,y1=fallback.Bottom;var horizontal=new List<int>();
            for(int y=Math.Max(0,y0-30);y<Math.Min(p.Height,y1+40);y++){int n=0;for(int x=5;x<p.Width-5;x+=3)if(p.Grey(x,y)<120&&!p.Blue(x,y))n++;if(n>p.Width/3*.50)horizontal.Add(y);}
            int top=horizontal.Where(y=>y<=y0+15).DefaultIfEmpty(y0).Max(),bottom=horizontal.Where(y=>y>top+25&&y>=y1-25).DefaultIfEmpty(y1).Min();
            var vertical=new List<int>();for(int x=(int)(p.Width*.43);x<(int)(p.Width*.93);x++){int n=0;for(int y=Math.Max(0,top+3);y<Math.Min(p.Height,bottom-3);y+=2)if(p.Grey(x,y)<120&&!p.Blue(x,y))n++;if(n>(bottom-top)/2*.55)vertical.Add(x);}
            int left=vertical.Where(x=>x<p.Width*.68).DefaultIfEmpty(fallback.Left).Max(),right=vertical.Where(x=>x>left+p.Width*.12).DefaultIfEmpty(fallback.Right).Min();
            located=horizontal.Count>0&&vertical.Count>0;return Rectangle.Intersect(Rectangle.FromLTRB(left+3,top+3,right-3,bottom-3),new Rectangle(0,0,p.Width,p.Height));
        }
        static readonly double[] CircleCos=Enumerable.Range(0,48).Select(i=>Math.Cos(i*Math.PI/24)).ToArray();
        static readonly double[] CircleSin=Enumerable.Range(0,48).Select(i=>Math.Sin(i*Math.PI/24)).ToArray();
        static double Ring(byte[] mask,int w,int h,int cx,int cy,int radius,int tolerance) {
            int matched=0;var quadrants=new int[4];
            for(int n=0;n<48;n++){bool hit=false;for(int dr=-tolerance;dr<=tolerance&&!hit;dr++){int x=(int)(cx+CircleCos[n]*(radius+dr)),y=(int)(cy+CircleSin[n]*(radius+dr));hit=x>=0&&y>=0&&x<w&&y<h&&mask[y*w+x]>0;}if(hit){matched++;quadrants[n/12]++;}if(n==11&&matched<3)return 0;}
            return quadrants.Min()<3?0:matched/48.0;
        }
        public static SealCheck Seal(Pixels p) {
            var result=new SealCheck{Status="Круглая печать не подтверждена"};int step=Math.Max(10,p.Width/80),minR=Math.Max(20,p.Width/25),maxR=p.Width/5;var mask=new byte[p.Width*p.Height];
            for(int y=(int)(p.Height*.58);y<p.Height;y++)for(int x=0;x<p.Width;x++)if(p.Blue(x,y)||p.Grey(x,y)<95)mask[y*p.Width+x]=1;
            var candidates=new List<SealCheck>();
            for(int cy=(int)(p.Height*.68);cy<p.Height-minR;cy+=step)for(int cx=minR;cx<p.Width-minR;cx+=step)for(int r=minR;r<=maxR;r+=Math.Max(6,step/2)) {
                if(cx-r<0||cx+r>=p.Width||cy-r<0||cy+r>=p.Height)continue;double score=Ring(mask,p.Width,p.Height,cx,cy,r,step/2+2);if(score<.35)continue;
                if(candidates.Count<6||score>candidates.Min(c=>c.Score)){candidates.Add(new SealCheck{Score=score,X=cx,Y=cy,Radius=r});candidates=candidates.OrderByDescending(c=>c.Score).Take(6).ToList();}
            }
            foreach(var c in candidates)for(int dy=-step;dy<=step;dy+=2)for(int dx=-step;dx<=step;dx+=2)for(int dr=-step/2;dr<=step/2;dr+=2) {
                int cx=(int)c.X+dx,cy=(int)c.Y+dy,r=(int)c.Radius+dr;double score=Ring(mask,p.Width,p.Height,cx,cy,r,3);if(score>result.Score){result.Score=score;result.X=cx;result.Y=cy;result.Radius=r;}
            }
            if(result.Score>.60)result.Status="Найден круговой контур печати";else if(result.Score>.45)result.Status="Возможна круглая печать; требуется просмотр";return result;
        }
    }
}
