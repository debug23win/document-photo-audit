using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using PhotoAudit;
class AuditTest {
    static OcrPage Page(string text){return new OcrPage{width=1200,height=2200,text=text,lines=text.Split('\n').Select((s,i)=>new Line{text=s,words=s.Split(' ').Select((w,j)=>new Word{text=w,x=40+j*90,y=400+i*35,width=80,height=22}).ToList()}).ToList()};}
    static Word W(string text,double x,double y){return new Word{text=text,x=x,y=y,width=90,height=25};}
    static OcrPage InventoryPage(string heading,string suffix){
        var words=new[]{W("Шифр",260,300),W("тома",360,300),W("Наименование",600,300),W("документа",800,300),W("123-45-6789-",260,600),W(suffix,260,640),W("Книга",600,600),W("10.",705,600),W("Тестовое",760,600),W("здание",600,640),W("Изм.",970,600),W("2",1070,600)};
        return new OcrPage{width=1200,height=2200,text=heading+"\nШифр тома\nНаименование документа\n123-45-6789-\n"+suffix,lines=new List<Line>{new Line{text=heading,words=new List<Word>()},new Line{words=words.ToList()}}};
    }
    static int Main(){
        string root=Path.Combine(Path.GetTempPath(),"photo-audit-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string file=Path.Combine(root,"page.jpg");using(var bmp=new Bitmap(1200,2200)){using(var g=Graphics.FromImage(bmp))g.Clear(Color.White);bmp.Save(file);}
        var a=new Photo{Id=1,Original="synthetic-title.jpg",Image=file,Ocr=Page("Росжелдорпроект\nКнига 1. Шумозащитные экраны\n123-45-6789-ИЛО3.7.1\nТом 4.3.7.1")};
        var b=new Photo{Id=2,Original="synthetic-iul.jpg",Image=file,Ocr=Page("Росжелдорпроект\nИнформационно-удостоверяющий лист\nКнига 1. Ограждения\nТом 4.3.7.1\n123-45-6789-ИЛОЗ.7.1\nCRC32: АВ12СD34")};
        var r=AuditEngine.Analyze(new List<Photo>{a,b},null);
        if(r.Volumes!=1||r.MainTitles!=1||r.Iul!=1)throw new Exception("Document classification failed");
        if(!r.Findings.Any(f=>f.Topic=="Название книги в ИУЛ"))throw new Exception("Name mismatch missed");
        if(b.Code!="123-45-6789-ИЛО3.7.1"||b.CRC!="AB12CD34")throw new Exception("OCR character normalization failed");
        b.Ocr=Page("Информационно-удостоверяющий лист\nКнига 1. Шумозащитные экраны\nТом 4.3.7.1\n123-45-6789-ИЛО3.7.1");r=AuditEngine.Analyze(new List<Photo>{a,b},null);
        if(r.Findings.Any(f=>f.Topic=="Название книги в ИУЛ"))throw new Exception("False name mismatch");
        var inventory=new Photo{Id=3,Original="single-inventory.jpg",Image=file,Ocr=InventoryPage("Опись","ИЛО3.7.1"),CodeHint="123-45-6789-ИЛО4.1.1"};
        r=AuditEngine.Analyze(new List<Photo>{inventory},null);
        if(inventory.Kind!="Опись"||inventory.Code!=null||r.InventoryPages!=1||r.Inventory.Count!=1||r.Inventory[0].Tom!="4.3.7.1"||r.Inventory[0].Revision!=2)throw new Exception("Single-row inventory with unread volume cell was missed or assigned a document hint");
        inventory.Ocr=InventoryPage("","ИЛО4Л.1О");inventory.Ocr.lines[1].words.Add(W("4.4.1.",150,600));inventory.Ocr.lines[1].words.Add(W("О",150,640));
        r=AuditEngine.Analyze(new List<Photo>{inventory},null);
        if(inventory.Kind!="Опись"||r.Inventory.Count!=1||r.Inventory[0].Tom!="4.4.1.10")throw new Exception("Continuation inventory with a wrapped volume was missed");
        inventory.Ocr=InventoryPage("","ИЛО3.7.1");inventory.Ocr.text="123-45-6789-ИЛО3.7.1";inventory.ManualInventory=true;inventory.KindHint=null;
        var preview=AuditEngine.PreviewInventory(new List<Photo>{a,inventory});if(preview.Pages!=1||preview.ManualPages!=1||preview.AutomaticPages!=0||preview.Images!=2)throw new Exception("Manual inventory designation must override missing headings and be counted before audit");
        r=AuditEngine.Analyze(new List<Photo>{inventory},null);if(inventory.Kind!="Опись"||r.Inventory.Count!=1)throw new Exception("Manual designation was lost during analysis");
        b.Ocr=Page("Информационно-удостоверяющий лист\nОпись исходных документов\n123-45-6789-ИЛО3.7.1\nCRC32: АВ12СD34");b.Pages=100;
        b.Readings=new List<Reading>{new Reading{Field="Pages",Status="Спорное чтение",Candidates=new List<string>{"3","100"}},new Reading{Field="Page",Status="Не распознано"}};
        r=AuditEngine.Analyze(new List<Photo>{b},null);
        if(b.Kind!="ИУЛ"||b.Pages!=null||b.Readings.Any(v=>v.Field=="Pages")||r.Findings.Any(f=>f.Topic.Contains("Листов")||f.Topic.Contains("комплектност")||f.Topic.Contains("пропуск")))throw new Exception("Ignored total count caused warnings or inventory misclassification");
        if(!r.Findings.Any(f=>f.Topic=="Не удалось прочитать: Лист"))throw new Exception("Ignoring total count must preserve sheet-number review");
        Console.WriteLine("Audit tests passed: classification, names, CRC, single-row/continuation inventory, wrapped volume, ignored total count with sheet-number review");return 0;
    }
}
