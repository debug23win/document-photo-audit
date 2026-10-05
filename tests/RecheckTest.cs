using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using PhotoAudit;

class RecheckTest {
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static string Hash(string file){using(var s=File.OpenRead(file))using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(s));}
    static void Reject(Action action,string message){try{action();}catch(ArgumentException){return;}catch(InvalidDataException){return;}catch(InvalidOperationException){return;}throw new Exception(message);}
    static OcrPage Ocr(string file,string text){return new OcrPage{file=file,text=text,width=800,height=1100,lines=new List<Line>{new Line{text=text,words=new List<Word>{new Word{text=text,x=50,y=300,width=600,height=30}}}}};}
    static int Main(){
        string temp=Path.Combine(Path.GetTempPath(),"recheck-tests-"+Guid.NewGuid().ToString("N")),source=Path.Combine(temp,"source"),assets=Path.Combine(source,"assets");Directory.CreateDirectory(assets);var json=new JavaScriptSerializer{MaxJsonLength=50000000};
        for(int id=1;id<=2;id++){string stem="p"+id.ToString("D4");using(var image=new Bitmap(800,1100))using(var graphics=Graphics.FromImage(image)){graphics.Clear(Color.White);image.Save(Path.Combine(assets,stem+".jpg"),System.Drawing.Imaging.ImageFormat.Jpeg);}File.WriteAllText(Path.Combine(assets,stem+".json"),json.Serialize(Ocr(stem+".jpg","Текст без шифра "+id)),Encoding.UTF8);}
        File.WriteAllText(Path.Combine(source,"Результаты.json"),json.Serialize(new{Photos=new[]{new Photo{Id=1,Original="absent.pdf / page 1",Kind="Не определено"},new Photo{Id=2,Original="absent.pdf / page 2",Kind="Не определено"}}}),Encoding.UTF8);
        var hashes=Directory.GetFiles(source,"*",SearchOption.AllDirectories).ToDictionary(f=>f,Hash);
        var loaded=Recheck.Load(Path.Combine(source,"Результаты.json"));Assert(loaded.Count==2,"Load cached pages without original PDF");
        var cached=Recheck.Run(source,null,Path.Combine(temp,"cached"),new List<int>(),false,null,CancellationToken.None,1,(a,b,c,d,e,f)=>{throw new Exception("Cached mode invoked OCR");});
        Assert(cached.Images==2&&cached.RecheckPages.Count==0&&File.Exists(cached.Report),"Cached analysis exported separate report");
        int calls=0;var selected=Recheck.Run(source,null,Path.Combine(temp,"selected"),new[]{1,1},true,null,CancellationToken.None,1,(input,output,workers,cache,progress,cancel)=>{
            calls++;var files=Directory.GetFiles(input);Assert(files.Length==1&&Path.GetFileName(files[0])=="p0001.jpg","Only selected page reaches OCR");Assert(!cache,"Force fresh OCR for selected pages");
            File.WriteAllText(Path.Combine(output,"p0001.json"),json.Serialize(Ocr("p0001.jpg","Книга 1. Том 3.12.1 123-45-6789-ТКР12.1")),Encoding.UTF8);progress("1/1");
        });
        Assert(calls==1&&selected.RecheckPages.SequenceEqual(new[]{1}),"Deduplicate selected pages");Assert(selected.Photos[0].Code=="123-45-6789-ТКР12.1","Fresh OCR replaces selected text");
        Assert(Hash(Path.Combine(assets,"p0002.json"))==Hash(Path.Combine(selected.Directory,"assets","p0002.json")),"Unselected OCR copied byte for byte");
        foreach(var pair in hashes)Assert(File.Exists(pair.Key)&&Hash(pair.Key)==pair.Value,"Source report unchanged");
        Assert(Recheck.Load(selected.Directory)[0].Code=="123-45-6789-ТКР12.1","A repeated report can be loaded again");
        Reject(()=>Recheck.Run(source,null,Path.Combine(temp,"none"),new int[0],true,null,CancellationToken.None),"Empty selection must fail");
        Reject(()=>Recheck.Run(source,null,Path.Combine(temp,"bad"),new[]{3},true,null,CancellationToken.None),"Missing page must fail");
        Reject(()=>Recheck.Run(source,null,source,new[]{1},true,null,CancellationToken.None),"Cannot overwrite source");
        Reject(()=>Recheck.Run(source,null,Path.Combine(source,"nested"),new[]{1},true,null,CancellationToken.None),"Cannot export into source");
        using(var cancel=new CancellationTokenSource()){cancel.Cancel();bool stopped=false;try{Recheck.Run(source,null,Path.Combine(temp,"cancel"),new[]{1},true,null,cancel.Token);}catch(OperationCanceledException){stopped=true;}Assert(stopped&&!Directory.Exists(Path.Combine(temp,"cancel")),"Cancellation before writes");}
        File.Delete(Path.Combine(assets,"p0001.json"));Reject(()=>Recheck.Load(source),"Missing OCR must have actionable error");
        Console.WriteLine("PASS: selective OCR, cached analysis, original preservation, missing PDF, selection validation and cancellation");return 0;
    }
}
