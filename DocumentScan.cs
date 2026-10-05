using System;
using System.Drawing;
using System.Threading;

namespace PhotoAudit {
    public static class DocumentScan {
        // Estimate paper per colour channel; interpolate illumination, never the ink itself.
        // Soft suppression removes paper noise without a hard black/white threshold.
        public static Bitmap Enhance(Bitmap source,bool colour=true,CancellationToken cancel=default(CancellationToken)) {
            var pixels=new Pixels(source);int tile=96,nx=(pixels.Width+tile-1)/tile,ny=(pixels.Height+tile-1)/tile;
            var paper=new double[nx*ny*3];var histogram=new int[3*256];
            for(int yy=0;yy<ny;yy++){
                cancel.ThrowIfCancellationRequested();
                for(int xx=0;xx<nx;xx++){
                    Array.Clear(histogram,0,histogram.Length);int samples=0;
                    for(int y=yy*tile;y<Math.Min(pixels.Height,(yy+1)*tile);y+=3)
                        for(int x=xx*tile;x<Math.Min(pixels.Width,(xx+1)*tile);x+=3){
                            int at=(y*pixels.Width+x)*3;samples++;
                            for(int channel=0;channel<3;channel++)histogram[channel*256+pixels.Data[at+channel]]++;
                        }
                    for(int channel=0;channel<3;channel++){
                        int cumulative=0,value=0,target=(int)Math.Ceiling(samples*.90);
                        for(;value<255;value++){cumulative+=histogram[channel*256+value];if(cumulative>=target)break;}
                        paper[(yy*nx+xx)*3+channel]=Math.Max(60,value);
                    }
                }
            }
            var left=new int[pixels.Width];var right=new int[pixels.Width];var fractions=new double[pixels.Width];
            for(int x=0;x<pixels.Width;x++){
                double position=Math.Max(0,Math.Min(nx-1,x/(double)tile-.5));left[x]=(int)position;right[x]=Math.Min(nx-1,left[x]+1);fractions[x]=position-left[x];
            }
            var rowPaper=new double[nx*3];
            for(int y=0;y<pixels.Height;y++){
                if(y%32==0)cancel.ThrowIfCancellationRequested();
                double position=Math.Max(0,Math.Min(ny-1,y/(double)tile-.5));int top=(int)position,bottom=Math.Min(ny-1,top+1);double fy=position-top;
                for(int i=0;i<rowPaper.Length;i++)rowPaper[i]=(1-fy)*paper[top*nx*3+i]+fy*paper[bottom*nx*3+i];
                for(int x=0;x<pixels.Width;x++){
                    int at=(y*pixels.Width+x)*3;double fx=fractions[x];
                    for(int channel=0;channel<3;channel++){
                        double a=rowPaper[left[x]*3+channel],b=rowPaper[right[x]*3+channel];
                        double background=a+(b-a)*fx;
                        double deficit=Math.Max(0,255*(background-pixels.Data[at+channel])/background-3);
                        double ink=deficit<=10?deficit*deficit/10:10+(deficit-10)*2.1;
                        pixels.Data[at+channel]=(byte)Math.Max(0,Math.Min(255,Math.Round(255-ink)));
                    }
                    if(!colour){byte grey=(byte)((pixels.Data[at]*29+pixels.Data[at+1]*150+pixels.Data[at+2]*77)>>8);pixels.Data[at]=pixels.Data[at+1]=pixels.Data[at+2]=grey;}
                }
            }
            cancel.ThrowIfCancellationRequested();return pixels.Bitmap();
        }
    }
}
