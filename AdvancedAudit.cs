using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PhotoAudit {
    public sealed class Reading {
        public string Field,Value,Status;
        public List<string> Candidates=new List<string>(),Sources=new List<string>();
        public int Agreement,Attempts;
    }
    public sealed class CropRequest {
        public int Photo; public string Field,Variant,File; public RectangleF Bounds; public double Scale;
    }
    public sealed class SignatureCheck {
        public string Role,Name,Organization,Status; public RectangleF Bounds;
        public int BluePixels,BlackPixels; public bool TableCell,Approval;
    }
    public sealed class SealCheck { public string Status; public double Score,X,Y,Radius; }
    public sealed class Pixels {
        public readonly int Width,Height; public readonly byte[] Data;
        public Pixels(int width,int height){Width=width;Height=height;Data=new byte[checked(width*height*3)];}
        public Pixels(Bitmap bitmap) {
            Width=bitmap.Width;Height=bitmap.Height;Data=new byte[Width*Height*3];
            using(var b=new Bitmap(Width,Height,PixelFormat.Format24bppRgb)) {
                using(var g=Graphics.FromImage(b))g.DrawImage(bitmap,0,0,Width,Height);
                var d=b.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.ReadOnly,PixelFormat.Format24bppRgb);
                try { for(int y=0;y<Height;y++)Marshal.Copy(IntPtr.Add(d.Scan0,y*d.Stride),Data,y*Width*3,Width*3); } finally { b.UnlockBits(d); }
            }
        }
        public int Grey(int x,int y) { int i=(y*Width+x)*3;return (Data[i]*29+Data[i+1]*150+Data[i+2]*77)>>8; }
        public bool Blue(int x,int y) { int i=(y*Width+x)*3;return Data[i]>Data[i+2]+12&&Data[i]>Data[i+1]+6&&Data[i]>55; }
        public Bitmap Bitmap() {
            var b=new Bitmap(Width,Height,PixelFormat.Format24bppRgb);var d=b.LockBits(new Rectangle(0,0,Width,Height),ImageLockMode.WriteOnly,PixelFormat.Format24bppRgb);
            try {for(int y=0;y<Height;y++)Marshal.Copy(Data,y*Width*3,IntPtr.Add(d.Scan0,y*d.Stride),Width*3);}finally{b.UnlockBits(d);}return b;
        }
    }
    public static class AdvancedAudit {
        static string N(string s){return Regex.Replace((s??"").ToUpperInvariant().Replace('Ё','Е'),@"\s+"," ").Trim();}
        public static Bitmap Resize(Bitmap src,int max) {
            double k=Math.Min(1, max/(double)Math.Max(src.Width,src.Height));var b=new Bitmap(Math.Max(1,(int)(src.Width*k)),Math.Max(1,(int)(src.Height*k)));
            using(var g=Graphics.FromImage(b)){g.Clear(Color.White);g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.DrawImage(src,0,0,b.Width,b.Height);}return b;
        }
        public static Bitmap Upright(string file,bool reverse) {
            using(var im=Image.FromFile(file)) {
                if((long)im.Width*im.Height>45000000)throw new InvalidDataException("Изображение превышает 45 мегапикселей: "+Path.GetFileName(file));
                var b=new Bitmap(im);
                if(im.PropertyIdList.Contains(274)) {int o=BitConverter.ToUInt16(im.GetPropertyItem(274).Value,0);if(o==3)b.RotateFlip(RotateFlipType.Rotate180FlipNone);if(o==6)b.RotateFlip(RotateFlipType.Rotate90FlipNone);if(o==8)b.RotateFlip(RotateFlipType.Rotate270FlipNone);}
                if(b.Width>b.Height)b.RotateFlip(RotateFlipType.Rotate90FlipNone);if(reverse)b.RotateFlip(RotateFlipType.Rotate180FlipNone);return b;
            }
        }
        public static Bitmap Rectify(Bitmap source,OcrPage page,out string note) {
            note="Геометрия сохранена";PointF[] q=PaperQuad(source);
            if(q!=null){note="Выровнена перспектива границ листа";return Warp(source,q);}
            var slopes=new List<double>();
            foreach(var l in page.lines??new List<Line>()) {
                var ws=l.words.Where(w=>w.text.Length>2).OrderBy(w=>w.x).ToList();if(ws.Count<3||ws.Last().x-ws[0].x<page.width*.25)continue;
                double a=(ws.Last().y-ws[0].y)/(ws.Last().x-ws[0].x);if(Math.Abs(a)<.10)slopes.Add(a);
            }
            if(slopes.Count<4)return new Bitmap(source);slopes.Sort();double angle=Math.Atan(slopes[slopes.Count/2])*180/Math.PI;
            if(Math.Abs(angle)<.35||Math.Abs(angle)>5)return new Bitmap(source);
            var b=new Bitmap(source.Width,source.Height);using(var g=Graphics.FromImage(b)){g.Clear(Color.White);g.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;g.TranslateTransform(b.Width/2f,b.Height/2f);g.RotateTransform((float)-angle);g.DrawImage(source,-source.Width/2f,-source.Height/2f,source.Width,source.Height);}
            note="Выровнен наклон "+angle.ToString("0.0",System.Globalization.CultureInfo.InvariantCulture)+"°";return b;
        }
        static PointF[] PaperQuad(Bitmap source) {
            using(var small=Resize(source,800)) {
                var p=new Pixels(small);var rows=new List<Tuple<int,int,int>>();
                for(int y=0;y<p.Height;y+=4) {
                    int start=-1,last=-1,bestL=0,bestR=0;
                    for(int x=0;x<p.Width;x++) {if(p.Grey(x,y)>145){if(start<0)start=x;last=x;}else if(start>=0&&x-last>18){if(last-start>bestR-bestL){bestL=start;bestR=last;}start=-1;}}
                    if(start>=0&&last-start>bestR-bestL){bestL=start;bestR=last;}if(bestR-bestL>p.Width*.70)rows.Add(Tuple.Create(y,bestL,bestR));
                }
                if(rows.Count<30)return null;int top=rows[0].Item1,bottom=rows.Last().Item1;if(bottom-top<p.Height*.65)return null;
                var middle=rows.Where(r=>r.Item1>top+(bottom-top)*.08&&r.Item1<bottom-(bottom-top)*.08).ToList();
                double[] left=Fit(middle.Select(r=>(double)r.Item1).ToArray(),middle.Select(r=>(double)r.Item2).ToArray());
                double[] right=Fit(middle.Select(r=>(double)r.Item1).ToArray(),middle.Select(r=>(double)r.Item3).ToArray());
                if(middle.Average(r=>Math.Abs(r.Item2-(left[0]+left[1]*r.Item1)))>p.Width*.025||middle.Average(r=>Math.Abs(r.Item3-(right[0]+right[1]*r.Item1)))>p.Width*.025)return null;
                double lt=Math.Max(0,left[0]+left[1]*top-3),lb=Math.Max(0,left[0]+left[1]*bottom-3),rt=Math.Min(p.Width-1,right[0]+right[1]*top+3),rb=Math.Min(p.Width-1,right[0]+right[1]*bottom+3);
                if(Math.Abs(lt-lb)+Math.Abs(rt-rb)<p.Width*.025)return null;
                // Cropping is conservative: if either edge leaves the image, preserve the source.
                if(lt<2&&lb<2||rt>p.Width-3&&rb>p.Width-3)return null;
                float k=source.Width/(float)p.Width;return new[]{new PointF((float)lt*k,top*k),new PointF((float)rt*k,top*k),new PointF((float)rb*k,bottom*k),new PointF((float)lb*k,bottom*k)};
            }
        }
        static double[] Fit(double[] x,double[] y){double xm=x.Average(),ym=y.Average(),den=x.Sum(a=>(a-xm)*(a-xm));double slope=den==0?0:x.Select((a,i)=>(a-xm)*(y[i]-ym)).Sum()/den;return new[]{ym-slope*xm,slope};}
        public static Bitmap Warp(Bitmap src,PointF[] q) {
            var p=new Pixels(src);int w=(int)Math.Max(Distance(q[0],q[1]),Distance(q[3],q[2])),h=(int)Math.Max(Distance(q[0],q[3]),Distance(q[1],q[2]));
            w=Math.Max(1,w);h=Math.Max(1,h);var output=new Pixels(w,h);
            double dx1=q[1].X-q[2].X,dx2=q[3].X-q[2].X,dx3=q[0].X-q[1].X+q[2].X-q[3].X,dy1=q[1].Y-q[2].Y,dy2=q[3].Y-q[2].Y,dy3=q[0].Y-q[1].Y+q[2].Y-q[3].Y;
            double den=dx1*dy2-dx2*dy1,g=0,j=0;if(Math.Abs(den)>.0001){g=(dx3*dy2-dx2*dy3)/den;j=(dx1*dy3-dx3*dy1)/den;}
            double a=q[1].X-q[0].X+g*q[1].X,c=q[3].X-q[0].X+j*q[3].X,d=q[1].Y-q[0].Y+g*q[1].Y,e=q[3].Y-q[0].Y+j*q[3].Y;
            for(int y=0;y<h;y++)for(int x=0;x<w;x++){double u=x/(double)Math.Max(1,w-1),v=y/(double)Math.Max(1,h-1),z=g*u+j*v+1;double sx=(a*u+c*v+q[0].X)/z,sy=(d*u+e*v+q[0].Y)/z;int ix=Math.Max(0,Math.Min(p.Width-2,(int)sx)),iy=Math.Max(0,Math.Min(p.Height-2,(int)sy));double fx=Math.Max(0,Math.Min(1,sx-ix)),fy=Math.Max(0,Math.Min(1,sy-iy));int i=(y*w+x)*3;
                for(int k=0;k<3;k++)output.Data[i+k]=(byte)((1-fy)*((1-fx)*p.Data[(iy*p.Width+ix)*3+k]+fx*p.Data[(iy*p.Width+ix+1)*3+k])+fy*((1-fx)*p.Data[((iy+1)*p.Width+ix)*3+k]+fx*p.Data[((iy+1)*p.Width+ix+1)*3+k]));}
            return output.Bitmap();
        }
        static double Distance(PointF a,PointF b){return Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Y-b.Y)*(a.Y-b.Y));}
        public static List<CropRequest> Crops(Photo p,string folder) {
            var requests=new List<CropRequest>();float w=p.Ocr.width,h=p.Ocr.height;var boxes=new Dictionary<string,RectangleF>();
            if(p.Kind=="ИУЛ"){
                var marker=p.Words.FirstOrDefault(t=>N(t.text).Replace('С','C').Replace('Р','R').StartsWith("CRC32"));
                if(marker!=null)boxes["CRC"]=new RectangleF((float)marker.x-20,(float)marker.y-12,Math.Max((float)marker.width*3.5f,w*.19f),Math.Max((float)marker.height*6,h*.07f));
                else if(p.CRC!=null||p.Book!=null)boxes["CRC"]=new RectangleF(w*.30f,h*.42f,w*.42f,h*.21f);
                boxes["Footer"]=new RectangleF(w*.65f,h*.82f,w*.35f,h*.18f);
                if(p.Book!=null||p.CRC!=null)boxes["Revision"]=new RectangleF(w*.76f,h*.145f,w*.24f,h*.185f);
                boxes["Roles"]=new RectangleF(0,p.Book!=null?h*.56f:h*.07f,w,p.Book!=null?h*.28f:h*.65f);
                if(p.Book!=null)boxes["Sections"]=new RectangleF(w*.25f,h*.28f,w*.54f,h*.22f);
            } else if(p.Kind.StartsWith("Титул")) {
                boxes["Roles"]=new RectangleF(0,h*.65f,w,h*.30f);boxes["Sections"]=new RectangleF(0,h*.40f,w,h*.28f);
                if(N(p.Ocr.text).Contains("СОГЛАСОВАНО"))boxes["Approval"]=new RectangleF(0,h*.12f,w*.78f,h*.18f);
            } else if(p.Kind=="Опись")boxes["Inventory"]=new RectangleF(0,h*.12f,w,h*.82f);
            using(var full=new Bitmap(p.FullImage))foreach(var box in boxes) {
                var area=RectangleF.Intersect(box.Value,new RectangleF(0,0,w,h));float ratio=full.Width/w;var actual=Rectangle.Round(new RectangleF(area.X*ratio,area.Y*ratio,area.Width*ratio,area.Height*ratio));actual=Rectangle.Intersect(actual,new Rectangle(0,0,full.Width,full.Height));
                if(actual.Width<8||actual.Height<8)continue;
                using(var crop=full.Clone(actual,PixelFormat.Format24bppRgb))for(int variant=0;variant<(box.Key=="CRC"?4:2);variant++) {
                    double enlargement=Math.Min(box.Key=="CRC"||box.Key=="Footer"||box.Key=="Revision"?2:1.25,3500.0/Math.Max(crop.Width,crop.Height));
                    using(var enlarged=new Bitmap(Math.Max(1,(int)(crop.Width*enlargement)),Math.Max(1,(int)(crop.Height*enlargement)))) {
                        using(var gr=Graphics.FromImage(enlarged)){gr.Clear(Color.White);gr.InterpolationMode=System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;gr.DrawImage(crop,0,0,enlarged.Width,enlarged.Height);}
                        string name="p"+p.Id.ToString("D4")+"-"+box.Key+"-"+variant+(box.Key=="CRC"?".png":".jpg");if(variant%2==0)SaveCrop(enlarged,Path.Combine(folder,name));
                        else using(var normalized=Normalize(enlarged))SaveCrop(normalized,Path.Combine(folder,name));
                        requests.Add(new CropRequest{Photo=p.Id,Field=box.Key,Variant=variant==0?"original":"contrast",File=name,Bounds=area,Scale=enlargement*ratio});
                    }
                }
            }return requests;
        }
        static void SaveCrop(Bitmap bitmap,string path){if(path.EndsWith(".png")){bitmap.Save(path,ImageFormat.Png);return;}var codec=ImageCodecInfo.GetImageEncoders().First(c=>c.MimeType=="image/jpeg");using(var options=new EncoderParameters(1)){options.Param[0]=new EncoderParameter(Encoder.Quality,99L);bitmap.Save(path,codec,options);}}
        public static Bitmap Normalize(Bitmap source) {
            var p=new Pixels(source);int tile=64;int nx=(p.Width+tile-1)/tile,ny=(p.Height+tile-1)/tile;var backgrounds=new int[nx*ny];
            for(int yy=0;yy<ny;yy++)for(int xx=0;xx<nx;xx++){var values=new List<int>();for(int y=yy*tile;y<Math.Min(p.Height,(yy+1)*tile);y+=3)for(int x=xx*tile;x<Math.Min(p.Width,(xx+1)*tile);x+=3)values.Add(p.Grey(x,y));values.Sort();backgrounds[yy*nx+xx]=Math.Max(80,values[(int)(values.Count*.85)]);}
            for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++){int grey=p.Grey(x,y),bg=backgrounds[(y/tile)*nx+x/tile],v=Math.Max(0,Math.Min(255,255-(bg-grey)*3));int i=(y*p.Width+x)*3;p.Data[i]=p.Data[i+1]=p.Data[i+2]=(byte)v;}return p.Bitmap();
        }
        public static Reading Consensus(string field,IEnumerable<Tuple<string,string>> reads) {
            var valid=reads.Where(r=>!string.IsNullOrWhiteSpace(r.Item2)).ToList();var result=new Reading{Field=field,Attempts=reads.Count(),Status="Не распознано"};
            var groups=valid.GroupBy(r=>r.Item2).OrderByDescending(g=>g.Count()).ToList();result.Candidates=groups.Select(g=>g.Key).ToList();result.Sources=valid.Select(r=>r.Item1+": "+r.Item2).ToList();
            if(groups.Count==0)return result;result.Agreement=groups[0].Count();
            if(groups.Count==1){result.Value=groups[0].Key;result.Status=result.Agreement>=2?"Подтверждено повторным чтением":"Одно чтение";}
            else if(groups[0].Count()>=2&&groups[0].Count()>=groups[1].Count()*2){result.Value=groups[0].Key;result.Status="Большинство чтений; были альтернативы";}
            else result.Status="Спорное чтение";return result;
        }
        public static Reading CrcConsensus(List<Tuple<string,string>> reads,List<Tuple<string,string>> latin) {
            var reading=Consensus("CRC",reads);
            // A blank specialized pass is not evidence against two readable whole pages.
            if(latin.Count(r=>!string.IsNullOrWhiteSpace(r.Item2))<2)return reading;
            var specialized=Consensus("CRC",latin);
            specialized.Sources=reads.Where(v=>!string.IsNullOrWhiteSpace(v.Item2)).Select(v=>v.Item1+": "+v.Item2).ToList();
            specialized.Candidates=reads.Where(v=>!string.IsNullOrWhiteSpace(v.Item2)).Select(v=>v.Item2).Distinct().ToList();
            specialized.Status+="; чтение латинского CRC по фрагменту";return specialized;
        }
        public static string Crc(string text) {
            string t=N(text).Replace('С','C').Replace('В','B').Replace('А','A').Replace('Е','E').Replace('О','0').Replace('O','0').Replace('I','1').Replace('L','1').Replace('З','3');
            var m=Regex.Match(t,@"(?<![A-Z0-9])([0-9A-F]{8})(?![A-Z0-9])");return m.Success?m.Groups[1].Value:null;
        }
        public static string Footer(OcrPage page,string label) {
            var ws=page.lines.SelectMany(l=>l.words).ToList();var labels=ws.Where(w=>N(w.text).Trim('.',':').StartsWith("ЛИСТ")).OrderBy(w=>w.x).ToList();
            var anchor=label=="ЛИСТ"?labels.FirstOrDefault(w=>N(w.text).Trim('.',':')=="ЛИСТ"):labels.FirstOrDefault(w=>N(w.text).Trim('.',':')!="ЛИСТ");if(label=="ЛИСТОВ"&&anchor==null&&labels.Count>=2)anchor=labels.Last();
            if(anchor==null)return null;double cx=anchor.x+anchor.width/2;
            var values=ws.Where(w=>w.y>anchor.y+anchor.height*.5&&w.y<anchor.y+Math.Max(220,anchor.height*6)&&Math.Abs(w.x+w.width/2-cx)<Math.Max(anchor.width*.8,70)&&Regex.IsMatch(N(w.text).Replace('О','0').Replace('O','0').Replace('I','1').Replace('Л','1'),@"^[0-9]{1,2}$")).OrderBy(w=>w.y).ToList();
            return values.Count==0?null:N(values[0].text).Replace('О','0').Replace('O','0').Replace('I','1').Replace('Л','1');
        }
        public static string Revision(OcrPage page) {
            var ws=page.lines.SelectMany(l=>l.words).ToList();var m=ws.Where(w=>Regex.IsMatch(N(w.text),@"^\d{1,2}$")&&w.y>page.height*.30&&w.y<page.height*.85).OrderBy(w=>w.y).FirstOrDefault();return m==null?null:m.text;
        }
        public static List<Word> Map(OcrPage page,CropRequest crop) {
            return page.lines.SelectMany(l=>l.words).Select(w=>new Word{text=w.text,x=crop.Bounds.X+w.x/crop.Scale,y=crop.Bounds.Y+w.y/crop.Scale,width=w.width/crop.Scale,height=w.height/crop.Scale}).ToList();
        }
    }
}
