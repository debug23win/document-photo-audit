using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using PhotoAudit;
class AuditTest {
    static OcrPage Page(string text){return new OcrPage{width=1200,height=2200,text=text,lines=text.Split('\n').Select((s,i)=>new Line{text=s,words=s.Split(' ').Select((w,j)=>new Word{text=w,x=40+j*90,y=400+i*35,width=80,height=22}).ToList()}).ToList()};}
    static int Main(){
        string root=Path.Combine(Path.GetTempPath(),"photo-audit-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string file=Path.Combine(root,"page.jpg");using(var bmp=new Bitmap(1200,2200)){using(var g=Graphics.FromImage(bmp))g.Clear(Color.White);bmp.Save(file);}
        var a=new Photo{Id=1,Original="synthetic-title.jpg",Image=file,Ocr=Page("Росжелдорпроект\nКнига 1. Шумозащитные экраны\n123-45-6789-ИЛО3.7.1\nТом 4.3.7.1")};
        var b=new Photo{Id=2,Original="synthetic-iul.jpg",Image=file,Ocr=Page("Росжелдорпроект\nИнформационно-удостоверяющий лист\nКнига 1. Ограждения\nТом 4.3.7.1\n123-45-6789-ИЛОЗ.7.1\nCRC32: B7СЕ8DС4")};
        var r=AuditEngine.Analyze(new List<Photo>{a,b},null);
        if(r.Volumes!=1||r.MainTitles!=1||r.Iul!=1)throw new Exception("Document classification failed");
        if(!r.Findings.Any(f=>f.Topic=="Название книги в ИУЛ"))throw new Exception("Name mismatch missed");
        if(b.Code!="123-45-6789-ИЛО3.7.1"||b.CRC!="B7CE8DC4")throw new Exception("OCR character normalization failed");
        b.Ocr=Page("Информационно-удостоверяющий лист\nКнига 1. Шумозащитные экраны\nТом 4.3.7.1\n123-45-6789-ИЛО3.7.1");r=AuditEngine.Analyze(new List<Photo>{a,b},null);
        if(r.Findings.Any(f=>f.Topic=="Название книги в ИУЛ"))throw new Exception("False name mismatch");
        Console.WriteLine("Audit tests passed: classification, name mismatch, equal names, code and CRC normalization");return 0;
    }
}
