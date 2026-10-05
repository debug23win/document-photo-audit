using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace PhotoAudit {
    public static class Pagination {
        public static string Number(string text){
            string value=(text??"").ToUpperInvariant().Replace('З','3').Replace('О','0').Replace('O','0').Replace('I','1').Replace('|','1');
            value=Regex.Replace(value,@"Л[ИП]СТ(?:ОВ)?|PAGE","").Trim(' ','.',':',';','\n','\r');
            int number;return Regex.IsMatch(value,@"^[0-9]{1,4}$")&&int.TryParse(value,out number)&&number>=1&&number<=1000?number.ToString():null;
        }
        public static List<CropRequest> Cells(Photo photo,List<CropRequest> requests,string folder,Func<CropRequest,OcrPage> read){
            var result=new List<CropRequest>();var footer=requests.FirstOrDefault(r=>r.Field=="Footer"&&r.Variant=="original");if(footer==null)return result;
            var page=read(footer);var labels=page.lines.SelectMany(l=>l.words).Where(w=>Regex.IsMatch(w.text??"",@"^Ли(?:ст|с|п)",RegexOptions.IgnoreCase)).OrderBy(w=>w.x).ToList();
            if(labels.Count==0)return result;
            using(var source=new Bitmap(Path.Combine(folder,footer.File)))for(int index=0;index<Math.Min(2,labels.Count);index++){
                var label=labels[index];string field=index==0&&Regex.IsMatch(label.text,@"^Лист[.:]?$",RegexOptions.IgnoreCase)?"PageCell":"PagesCell";
                if(field!="PageCell")continue;
                int width=Math.Max(120,(int)(label.width*2.1)),height=Math.Max(140,(int)(label.height*4));
                var area=Rectangle.Intersect(new Rectangle((int)(label.x+label.width/2-width/2),(int)(label.y+label.height+8),width,height),new Rectangle(0,0,source.Width,source.Height));
                if(area.Width<30||area.Height<30)continue;
                using(var cell=source.Clone(area,PixelFormat.Format24bppRgb))for(int variant=0;variant<2;variant++)using(var contextual=Context(cell,variant==1)){
                    string name="p"+photo.Id.ToString("D4")+"-"+field+"-"+variant+".png";contextual.Save(Path.Combine(folder,name),ImageFormat.Png);
                    result.Add(new CropRequest{Photo=photo.Id,Field=field,Variant="numeric-cell",File=name,Bounds=footer.Bounds,Scale=footer.Scale});
                }
            }
            return result;
        }
        public static List<CropRequest> RevisionCells(Photo photo,List<CropRequest> requests,string folder,Func<CropRequest,OcrPage> read){
            var result=new List<CropRequest>();var revision=requests.FirstOrDefault(r=>r.Field=="Revision"&&r.Variant=="original");if(revision==null)return result;
            var label=read(revision).lines.SelectMany(l=>l.words).Where(w=>Regex.IsMatch(w.text??"",@"^изменени[яй]",RegexOptions.IgnoreCase)).OrderBy(w=>w.y).FirstOrDefault();if(label==null)return result;
            using(var source=new Bitmap(Path.Combine(folder,revision.File))){
                int width=Math.Max(120,(int)(label.width*1.5)),height=Math.Max(180,(int)(label.height*5));
                var area=Rectangle.Intersect(new Rectangle((int)(label.x+label.width/2-width/2),(int)(label.y+label.height+8),width,height),new Rectangle(0,0,source.Width,source.Height));
                if(area.Width<30||area.Height<30)return result;
                using(var cell=source.Clone(area,PixelFormat.Format24bppRgb))for(int variant=0;variant<2;variant++)using(var contextual=Context(cell,variant==1)){
                    string name="p"+photo.Id.ToString("D4")+"-RevisionCell-"+variant+".png";contextual.Save(Path.Combine(folder,name),ImageFormat.Png);result.Add(new CropRequest{Photo=photo.Id,Field="RevisionCell",Variant="numeric-cell",File=name,Bounds=revision.Bounds,Scale=revision.Scale});
                }
            }return result;
        }
        public static Bitmap Context(Bitmap source,bool clean){
            using(var image=clean?DocumentScan.Enhance(source,false):new Bitmap(source)){
                var p=new Pixels(image);var rows=new bool[p.Height];var cols=new bool[p.Width];
                for(int y=0;y<p.Height;y++){int dark=0;for(int x=0;x<p.Width;x++)if(p.Grey(x,y)<110)dark++;rows[y]=dark>p.Width*.70;}
                for(int x=0;x<p.Width;x++){int dark=0;for(int y=0;y<p.Height;y++)if(p.Grey(x,y)<110)dark++;cols[x]=dark>p.Height*.75;}
                for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++){
                    bool rule=rows[y]||cols[x]||y>0&&rows[y-1]||y+1<p.Height&&rows[y+1]||x>0&&cols[x-1]||x+1<p.Width&&cols[x+1];
                    if(rule){int at=(y*p.Width+x)*3;p.Data[at]=p.Data[at+1]=p.Data[at+2]=255;}
                }
                // Ignore the long edges of the cell, including pale or sloping rules.
                var seen=new bool[p.Width*p.Height];var components=new List<Rectangle>();
                for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++){
                    int start=y*p.Width+x;if(seen[start]||p.Grey(x,y)>=135)continue;
                    var pending=new Queue<int>();pending.Enqueue(start);seen[start]=true;int left=x,top=y,right=x,bottom=y,count=0;
                    while(pending.Count>0){int at=pending.Dequeue(),cx=at%p.Width,cy=at/p.Width;count++;left=Math.Min(left,cx);top=Math.Min(top,cy);right=Math.Max(right,cx);bottom=Math.Max(bottom,cy);
                        foreach(int next in new[]{cx>0?at-1:-1,cx+1<p.Width?at+1:-1,cy>0?at-p.Width:-1,cy+1<p.Height?at+p.Width:-1})if(next>=0&&!seen[next]&&p.Grey(next%p.Width,next/p.Width)<135){seen[next]=true;pending.Enqueue(next);}}
                    int cw=right-left+1,ch=bottom-top+1;
                    if(count>=15&&ch>=12&&cw< p.Width*.6&&ch<p.Height*.8&&cw/(double)ch<5&&ch/(double)cw<8&&left+cw/2>p.Width*.12&&left+cw/2<p.Width*.88)components.Add(new Rectangle(left,top,cw,ch));
                }
                Rectangle box=Rectangle.Empty;if(components.Count>0){var main=components.OrderByDescending(r=>r.Height).First();foreach(var r in components.Where(r=>Math.Abs(r.Bottom-main.Bottom)<main.Height*.5&&r.Height>=main.Height*.5))box=box.IsEmpty?r:Rectangle.Union(box,r);box=Rectangle.Intersect(new Rectangle(box.X-4,box.Y-4,box.Width+8,box.Height+8),new Rectangle(0,0,p.Width,p.Height));}
                var output=new Bitmap(clean?1000:500,clean?300:150);using(var g=Graphics.FromImage(output)){
                    g.Clear(Color.White);if(clean)g.ScaleTransform(2,2);using(var font=new Font("Segoe UI",38,FontStyle.Regular,GraphicsUnit.Pixel))g.DrawString("Лист",font,Brushes.Black,20,48);
                    // OCR needs line context for an isolated digit. The prefix contains no numbers.
                    if(!box.IsEmpty)using(var stripped=p.Bitmap())using(var ink=stripped.Clone(box,PixelFormat.Format24bppRgb)){
                        double scale=Math.Min(42.0/ink.Height,220.0/ink.Width);int w=Math.Max(1,(int)(ink.Width*scale)),h=Math.Max(1,(int)(ink.Height*scale));g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(ink,128,94-h,w,h);
                    }
                }return output;
            }
        }
    }
}
