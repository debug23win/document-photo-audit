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
        if(!r.InventoryOnly||r.Volumes!=1||r.Findings.Any(f=>f.Topic=="Нет фотографий тома"))throw new Exception("Inventory-only must not invent missing document findings");
        inventory.Ocr=InventoryPage("Проектная документация","ИЛО3.7.1");inventory.Ocr.lines[1].words.Add(W("Короба",1150,300));inventory.Ocr.lines[1].words.Add(W("22",1150,600));
        var continuation=new Photo{Id=4,Original="continuation.jpg",Image=file,Ocr=InventoryPage("","ИЛО4.1.2")};continuation.Ocr.lines[1].words.Add(W("Короба",1150,300));continuation.Ocr.lines[1].words.Add(W("22",1150,600));
        r=AuditEngine.Analyze(new List<Photo>{inventory,continuation},null);
        if(r.Inventory.Count!=2||r.Inventory.Any(i=>i.Box!=22||i.DocumentationKind!="ПД")||r.Inventory.Any(i=>i.Revision!=2))throw new Exception("Box column/stage propagation failed or confused with revision");
        continuation.Ocr.lines[1].words.Last().text="23";r=AuditEngine.Analyze(new List<Photo>{inventory,continuation},null);
        if(r.Inventory.First(i=>i.Code.EndsWith("4.1.2")).DocumentationKind!=null)throw new Exception("Stage must not propagate across unrelated boxes");
        continuation.Ocr=InventoryPage("Проектная документация","ИЛО3.7.1");continuation.Ocr.lines[1].words.Add(W("Короба",1150,300));continuation.Ocr.lines[1].words.Add(W("23",1150,600));r=AuditEngine.Analyze(new List<Photo>{inventory,continuation},null);
        if(r.Inventory.Count!=1||r.Inventory[0].Box!=null||!r.Findings.Any(f=>f.Topic=="Разные номера короба в описи"))throw new Exception("Conflicting box assignments must remain visible and unfilled");
        r=AuditEngine.Analyze(new List<Photo>{inventory,b},null);if(r.InventoryOnly)throw new Exception("Mixed inputs must retain full audit mode");
        if(AuditEngine.InventoryDocumentationKind(Page("Инженерные изыскания\nШифр тома"))!="ИИ"||AuditEngine.InventoryDocumentationKind(Page("Документация по планировке территории\nШифр тома"))!="ДПТ"||AuditEngine.InventoryDocumentationKind(Page("Проектная документация\nИнженерные изыскания\nШифр тома"))!=null)throw new Exception("Documentation stage recognition must refuse ambiguous headings");
        if(AuditEngine.InventoryDocumentationKind(Page("Опись проектной и сметной документации\nСтадия: Инженерные изыскания\nШифр тома"))!="ИИ")throw new Exception("Explicit stage must take precedence over a generic inventory heading");
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
        if(AuditEngine.DocumentCode("Раздел_123-45-6789-TkP12.l.9.pdf")!="123-45-6789-ТКР12.1.9"||AuditEngine.DocumentCode("123-45-6789-ТКР12Л.11")!="123-45-6789-ТКР12.1.11")throw new Exception("TKR hierarchy and mixed OCR alphabets are not normalized");
        var tkr=new Photo{Id=7,Original="tkr-inventory.jpg",Image=file,Ocr=InventoryPage("Опись. Стадия: Проектная документация","TkP12.l.9")};tkr.Ocr.lines[1].words.Add(W("Короба",1150,300));tkr.Ocr.lines[1].words.Add(W("13",1150,600));r=AuditEngine.Analyze(new List<Photo>{tkr},null);
        if(!r.InventoryOnly||r.Inventory.Count!=1||r.Inventory[0].Code!="123-45-6789-ТКР12.1.9"||r.Inventory[0].Tom!="3.12.1.9"||r.Inventory[0].Box!=13)throw new Exception("TKR inventory row with unread volume cell was lost");
        tkr.Ocr.text="Шифр тома\nНаименование локумент\n123-45-6789-TkP12.l.9";r=AuditEngine.Analyze(new List<Photo>{tkr},null);if(!r.InventoryOnly||r.Inventory.Count!=1)throw new Exception("Damaged document-header OCR must not turn inventory continuation into a title");
        tkr.Ocr=Page("Ленгипротранс\nКнига 1. Тестовое здание\n123-45-6789-ТКР12.1.9\nТом 3.12.1.9");r=AuditEngine.Analyze(new List<Photo>{tkr},null);if(r.MainTitles!=1||tkr.Code!="123-45-6789-ТКР12.1.9"||tkr.Tom!="3.12.1.9")throw new Exception("TKR title classification failed");
        if(AuditEngine.DocumentCode("123-45-6789-ТКР12.1.10\nНаименование")!="123-45-6789-ТКР12.1.10")throw new Exception("Next-line label leaked into code digits");
        foreach(string arbitrary in new[]{"ЭОМ-ABC/17.02", "007", "АЛЬФА", "Я", "СП / Договор №7", "PREFIX-123-45-6789-ИЛО4.1.1-SUFFIX", "Ω/PLAN-07"}){
            var page=InventoryPage("Опись",arbitrary);page.lines[1].words.RemoveAll(w=>w.text=="123-45-6789-");page.lines[1].words.Add(W("1.",30,600));
            var photo=new Photo{Id=20,Image=file,Ocr=page};var generic=AuditEngine.Analyze(new List<Photo>{photo},null);
            if(!generic.InventoryOnly||generic.Inventory.Count!=1||generic.Inventory[0].Code!=DocumentIdentity.Value(arbitrary))throw new Exception("Arbitrary inventory code rejected: "+arbitrary);
            var field=new OcrPage{width=1200,height=2200,text="Обозначение документа: "+arbitrary+"\nИнформационно-удостоверяющий лист",lines=new List<Line>{new Line{text="Обозначение документа: "+arbitrary,words=new List<Word>{W(arbitrary,50,500)}}}};
            var iul=new Photo{Id=21,Image=file,Ocr=field};generic=AuditEngine.Analyze(new List<Photo>{iul},null);
            if(iul.Code!=DocumentIdentity.Value(arbitrary)||iul.Kind!="ИУЛ")throw new Exception("Arbitrary field code rejected: "+arbitrary);
            if(DocumentIdentity.MatchFile("Альбом_"+arbitrary+".pdf",new[]{DocumentIdentity.Value(arbitrary)})!=DocumentIdentity.Value(arbitrary))throw new Exception("Arbitrary filename matching failed: "+arbitrary);
        }
        var projectHeader=new OcrPage{width=1200,height=2200,text="Наименование и Шифр объекта\nТестовое название",lines=new List<Line>{new Line{text="Наименование и Шифр объекта",words=new List<Word>{W("Наименование",450,300),W("Шифр",650,300),W("объекта",750,300)}},new Line{text="Тестовое название",words=new List<Word>{W("Тестовое",450,400),W("название",600,400)}}}};
        bool strongField;if(DocumentIdentity.FieldCode(projectHeader,file,out strongField)!=null)throw new Exception("Object name and code header is not the document designation");
        var arbitraryInventory=InventoryPage("Опись","АЛЬФА");arbitraryInventory.lines[1].words.RemoveAll(w=>w.text=="123-45-6789-");arbitraryInventory.lines[1].words.Add(W("1.",30,600));
        var arbitraryDocument=new OcrPage{width=1200,height=2200,text="Обозначение документа\nАЛЬФА\nКнига 1\nСсылка 123-45-6789-ИЛО4.1.1",lines=new List<Line>{new Line{text="Обозначение документа",words=new List<Word>{W("Обозначение",40,300)}},new Line{text="АЛЬФА",words=new List<Word>{W("АЛЬФА",40,400)}},new Line{text="Ссылка 123-45-6789-ИЛО4.1.1",words=new List<Word>{W("Ссылка",450,800),W("123-45-6789-ИЛО4.1.1",600,800)}}}};
        var arbitraryPhoto=new Photo{Id=31,Image=file,Ocr=arbitraryDocument};AuditEngine.Analyze(new List<Photo>{new Photo{Id=30,Image=file,Ocr=arbitraryInventory},arbitraryPhoto},null);if(arbitraryPhoto.Code!="АЛЬФА")throw new Exception("Exact arbitrary designation must take precedence over an unrelated code reference");
        if(DocumentIdentity.MatchFile("ABC-120.pdf",new[]{"ABC-12"})!=null||DocumentIdentity.MatchFile("ABC-12.3.pdf",new[]{"ABC-12"})!=null)throw new Exception("Partial code must not match another document");
        var signaturePage=new Photo{Id=40,Image=file,Ocr=Page("Информационно-удостоверяющий лист\nОбозначение документа: ABC-12"),Revision=3,Readings=new List<Reading>{new Reading{Field="Revision",Value="3",Status="Подтверждено повторным чтением"}}};signaturePage.Ocr.lines.Add(new Line{words=new List<Word>{W("3",1100,480),W("Лист",1050,2000),W("3",1050,2050)}});
        AuditEngine.Analyze(new List<Photo>{signaturePage},null);if(signaturePage.Revision!=null||signaturePage.Readings.Any(v=>v.Field=="Revision"))throw new Exception("Signature-only IUL must not treat stray digits, footer or old cached reading as a revision");
        var revisionPage=new Photo{Id=41,Image=file,Ocr=Page("Информационно-удостоверяющий лист\nОбозначение документа: ABC-12")};revisionPage.Ocr.lines.Add(new Line{words=new List<Word>{W("Номер",1000,330),W("последнего",1000,365),W("изменения",1000,400),W("2",1020,470),W("37-26",1000,850),W("Лист",1050,2000),W("3",1050,2050)}});
        AuditEngine.Analyze(new List<Photo>{revisionPage},null);if(revisionPage.Revision!=2)throw new Exception("Revision must be read from the labeled cell separately from permit number and footer");
        var orderPage=Page("Информационно-удостоверяющий лист\nОбозначение документа: ABC-12\nКнига 8. Тяговая подстанция.\nинформационно-измерительная\nАвтоматизированная\nсистема учета электроэнергии\nТом 3.23.8");orderPage.lines=new List<Line>{new Line{words=new List<Word>{W("Книга",300,800),W("8.",410,800),W("Тяговая",480,800),W("подстанция.",580,800),W("информационно-измерительная",650,842),W("Автоматизированная",300,847),W("система",300,900),W("учета",410,900),W("электроэнергии",520,900),W("Том",300,950)}}};
        var ordered=new Photo{Id=42,Image=file,Ocr=orderPage};AuditEngine.Analyze(new List<Photo>{ordered},null);if(ordered.Book!="Тяговая подстанция. Автоматизированная информационно-измерительная система учета электроэнергии")throw new Exception("Words split across native OCR lines must retain left-to-right order on the same physical row");
        Console.WriteLine("Audit tests passed: classification, names, CRC, single-row/continuation inventory, wrapped volume, ignored total count with sheet-number review");return 0;
    }
}
