using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace PhotoAudit {
    // Conservative fallback for one printed digit. No document order or expected value is used.
    public static class DigitShapes {
        sealed class Sample {public string Digit;public bool[] Mask;}
        static readonly Lazy<List<Sample>> Samples=new Lazy<List<Sample>>(Build);
        static bool[] Mask(Bitmap image,out int width,out int height){
            var p=new Pixels(image);int left=p.Width,top=p.Height,right=-1,bottom=-1;
            for(int y=0;y<p.Height;y++)for(int x=0;x<p.Width;x++)if(p.Grey(x,y)<140){left=Math.Min(left,x);top=Math.Min(top,y);right=Math.Max(right,x);bottom=Math.Max(bottom,y);}
            width=right-left+1;height=bottom-top+1;if(width<3||height<12)return null;
            var result=new bool[32*48];
            for(int y=0;y<48;y++)for(int x=0;x<32;x++){
                double xx=left+(x+.5)*width/32,yy=top+(y+.5)*height/48;
                result[y*32+x]=p.Grey(Math.Min(right,(int)xx),Math.Min(bottom,(int)yy))<140;
            }return result;
        }
        static List<Sample> Build(){
            var result=new List<Sample>();
            foreach(string family in new[]{"Times New Roman","Cambria","Arial","Segoe UI"})foreach(FontStyle style in new[]{FontStyle.Regular,FontStyle.Bold,FontStyle.Italic})for(int n=0;n<=9;n++){
                using(var image=new Bitmap(200,160))using(var g=Graphics.FromImage(image))using(var font=new Font(family,100,style,GraphicsUnit.Pixel)){
                    g.Clear(Color.White);g.DrawString(n.ToString(),font,Brushes.Black,20,10);int width,height;var mask=Mask(image,out width,out height);if(mask!=null)result.Add(new Sample{Digit=n.ToString(),Mask=mask});
                }
            }return result;
        }
        public static string Read(Bitmap contextual,out double score){
            score=0;int scale=contextual.Width/500;if(scale<1)return null;
            var area=Rectangle.Intersect(new Rectangle(125*scale,45*scale,225*scale,55*scale),new Rectangle(0,0,contextual.Width,contextual.Height));
            using(var digit=contextual.Clone(area,contextual.PixelFormat)){
                int width,height;var mask=Mask(digit,out width,out height);if(mask==null||width>height*1.2||height<20*scale)return null;
                var ranked=Samples.Value.GroupBy(s=>s.Digit).Select(group=>new{Digit=group.Key,Score=group.Max(s=>Similarity(mask,s.Mask))}).OrderByDescending(s=>s.Score).ToList();
                score=ranked[0].Score;return score>=.65&&score-ranked[1].Score>=.12&&ranked[0].Digit!="0"?ranked[0].Digit:null;
            }
        }
        static double Similarity(bool[] a,bool[] b){int intersection=0,union=0;for(int i=0;i<a.Length;i++){if(a[i]&&b[i])intersection++;if(a[i]||b[i])union++;}return union==0?0:intersection/(double)union;}
    }
}
