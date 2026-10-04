using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using PhotoAudit;
class AdvancedTest {
    static void Assert(bool ok,string message){if(!ok)throw new Exception(message);}
    static Word W(string s,double x,double y,double width){return new Word{text=s,x=x,y=y,width=width,height=25};}
    static int Main() {
        var ambiguous=AdvancedAudit.Consensus("CRC",new[]{Tuple.Create("raw","1234ABCD"),Tuple.Create("contrast","1234ABCE")});Assert(ambiguous.Value==null,"Tied OCR readings must not be resolved from the registry");
        var majority=AdvancedAudit.Consensus("CRC",new[]{Tuple.Create("whole","1234ABCE"),Tuple.Create("raw","1234ABCD"),Tuple.Create("contrast","1234ABCD")});Assert(majority.Value=="1234ABCD"&&majority.Candidates.Count==2,"Independent repeat majority missing");
        Assert(AdvancedAudit.Crc("CRC32: АВСD1234")=="ABCD1234","Hex alphabet normalization failed");
        var crcReads=new List<Tuple<string,string>>{Tuple.Create("whole","1234ABCD"),Tuple.Create("repeat","1234ABCD")};
        Assert(AdvancedAudit.CrcConsensus(crcReads,new List<Tuple<string,string>>{Tuple.Create("english-raw",(string)null),Tuple.Create("english-contrast",(string)null)}).Value=="1234ABCD","Blank specialized crops must preserve readable page evidence");
        Assert(AdvancedAudit.CrcConsensus(crcReads,new List<Tuple<string,string>>{Tuple.Create("english-raw","1234ABCE"),Tuple.Create("english-contrast","1234ABCF")}).Value==null,"Conflicting readable specialized crops must remain ambiguous");
        var sections=FormAnalysis.Sections("Раздел 4. Здания\nПодраздел 4. Инженерное оборудование\nЧасть 1. Электроснабжение\nКнига 2. Тест");Assert(sections.Count==3&&sections["ЧАСТЬ"].StartsWith("1."),"Heading hierarchy missing");
        string folder=Path.Combine(Path.GetTempPath(),"audit-advanced-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        var words=new List<Word>{W("Главный",35,1320,80),W("инженер",125,1320,90),W("Александров",290,1320,120),W("Главный",35,1520,80),W("инженер",125,1520,90),W("проекта",35,1560,90),W("Иванов",290,1520,120)};
        string image=Path.Combine(folder,"black-signature.png");using(var b=new Bitmap(1000,2000)){using(var g=Graphics.FromImage(b)){g.Clear(Color.White);using(var pen=new Pen(Color.Black,4)){foreach(int y in new[]{1300,1500,1700})g.DrawLine(pen,10,y,990,y);foreach(int x in new[]{10,270,550,830,990})g.DrawLine(pen,x,1300,x,1700);g.DrawLines(pen,new[]{new Point(580,1450),new Point(610,1350),new Point(660,1430),new Point(700,1360),new Point(770,1410)});}}b.Save(image);}
        var p=new Photo{Id=1,Image=image,Kind="ИУЛ",Organization="Тест",Words=words,Ocr=new OcrPage{width=1000,height=2000,text="",lines=new List<Line>()}};
        FormAnalysis.Inspect(p,null);Assert(p.Signatures.Count==2,"Chief and GIP must be separate roles");Assert(p.Signatures[0].Status.Contains("чёрные"),"Black handwriting missed");Assert(!p.Signatures[1].Status.StartsWith("Найдены"),"Blank cell falsely accepted as signature");
        p.Words=new List<Word>{W("Главный",35,1320,80),W("инженер",125,1320,90),W("Белов",260,1326,100),W("Андрей",370,1320,90),W("Главный",35,1520,80),W("инженер",125,1520,90),W("троекта",35,1560,90),W("Орлов",260,1526,100),W("Павел",370,1520,90)};
        var roles=FormAnalysis.Roles(p);Assert(roles[0].Name=="БЕЛОВ"&&roles[1].Name=="ОРЛОВ","Surname must precede first name despite skewed OCR baselines");Assert(roles[1].Role=="Главный инженер проекта","OCR typo in project role must not merge GIP with chief");
        var footer=new OcrPage{width=350,height=300,lines=new List<Line>{new Line{words=new List<Word>{W("Лист",40,30,70),W("Листс",200,30,70),W("1",55,110,20),W("3",220,110,20)}}}};
        Assert(AdvancedAudit.Footer(footer,"ЛИСТ")=="1"&&AdvancedAudit.Footer(footer,"ЛИСТОВ")=="3","Truncated footer label must retain column identity");
        using(var b=new Bitmap(1000,2000)){using(var g=Graphics.FromImage(b)){g.Clear(Color.White);using(var pen=new Pen(Color.Black,5))g.DrawEllipse(pen,300,1600,200,200);}Assert(FormAnalysis.Seal(new Pixels(b)).Score>.6,"Black circular seal missed");}
        using(var b=new Bitmap(200,300)){using(var g=Graphics.FromImage(b)){g.Clear(Color.White);g.FillRectangle(Brushes.Black,40,50,100,200);}using(var transformed=AdvancedAudit.Warp(b,new[]{new PointF(40,50),new PointF(139,50),new PointF(139,249),new PointF(40,249)})){Assert(transformed.Width==99&&transformed.Height==199,"Perspective transform dimensions wrong");Assert(transformed.GetPixel(40,50).R<10,"Perspective mapping changed content");}}
        var report=new Dictionary<string,object>{{"Photos",new object[]{new Dictionary<string,object>{{"Id",1},{"CRC","ABCDEF00"}}}},{"Findings",new object[0]}};
        var reference=new Dictionary<string,object>{{"Fields",new object[]{new Dictionary<string,object>{{"Photo",1},{"Field","CRC"},{"Value","ABCDEF00"}}}}};var quality=QualityBenchmark.Evaluate(report,reference);Assert((int)quality["FieldsCorrect"]==1,"Reference field evaluation failed");
        Console.WriteLine("Advanced tests passed: consensus without expected answers, CRC alphabet, headings, separate roles, black signature, blank cell, black seal, projective transform, manual reference metrics");return 0;
    }
}
