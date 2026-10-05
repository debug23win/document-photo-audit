using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml.Linq;
using PhotoAudit;
using ReviewMerge;

class SummaryTest {
    static readonly XNamespace N="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    const string Code="123-45-6789-ИЛО4.1.1",Other="123-45-6789-ИЛО4.1.2";
    static void Assert(bool value,string message){if(!value)throw new Exception(message);}
    static void Put(ZipArchive z,string path,string text){using(var s=new StreamWriter(z.CreateEntry(path).Open()))s.Write(text);}
    static XElement C(string address,string value){return new XElement(N+"c",new XAttribute("r",address),new XAttribute("t","inlineStr"),new XElement(N+"is",new XElement(N+"t",value)));}
    public static void Fixture(string path,bool multiple=false,bool date1904=false,bool formula=false){
        var header=new XElement(N+"row",new XAttribute("r",5),C("C5","Наименование файла"),C("H5","Контрольная сумма"),C("I5","Проверил"),C("J5","Дата проверки"),C("L5","Короб"),C("S5","Ошибки в описи"),C("T5","Примечание"));
        var first=new XElement(N+"row",new XAttribute("r",7),C("C7",Code+".pdf"),C("D7","pdf"),C("H7","A1B2C3D4"),C("I7","Петров"),C("M7","1"),C("S7","Прежняя ошибка описи"),C("T7","Прежнее замечание"));
        if(formula)first.Add(new XElement(N+"c",new XAttribute("r","J7"),new XElement(N+"f","TODAY()"),new XElement(N+"v",46000)));
        var second=new XElement(N+"row",new XAttribute("r",8),C("C8",(multiple?Code:Other)+".pdf"),C("D8","pdf"),C("H8","FFEEDDCC"));
        var sheet=new XDocument(new XElement(N+"worksheet",new XElement(N+"dimension",new XAttribute("ref","A1:T8")),new XElement(N+"sheetData",header,first,second)));
        using(var z=ZipFile.Open(path,ZipArchiveMode.Create)){
            Put(z,"xl/workbook.xml","<workbook xmlns=\""+N+"\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><workbookPr date1904=\""+(date1904?"1":"0")+"\"/><sheets><sheet name=\"Все загруженные файлы\" sheetId=\"1\" r:id=\"rId1\"/><sheet name=\"СВОД\" sheetId=\"2\" r:id=\"rId2\"/></sheets><calcPr calcMode=\"auto\"/></workbook>");
            Put(z,"xl/_rels/workbook.xml.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/><Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet2.xml\"/></Relationships>");
            Put(z,"xl/styles.xml","<styleSheet xmlns=\""+N+"\"><fonts count=\"1\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"2\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/></cellXfs></styleSheet>");
            Put(z,"xl/worksheets/sheet1.xml",sheet.ToString());
            Put(z,"xl/worksheets/sheet2.xml","<worksheet xmlns=\""+N+"\"><sheetData><row r=\"1\"><c r=\"A1\"><f>COUNTIFS('Все загруженные файлы'!I7:I8,\"&lt;&gt;\")</f><v>1</v></c></row></sheetData></worksheet>");
            Put(z,"_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            Put(z,"[Content_Types].xml","<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/><Override PartName=\"/xl/worksheets/sheet2.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
            Put(z,"custom/preserved.txt","unchanged attachment");
        }
    }
    static string Entry(string path,string name){using(var z=ZipFile.OpenRead(path))using(var r=new StreamReader(z.GetEntry(name).Open()))return r.ReadToEnd();}
    [STAThread]static int Main(){
        string root=Path.Combine(Path.GetTempPath(),"summary-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string source=Path.Combine(root,"source.xlsx"),dest=Path.Combine(root,"filled.xlsx");Fixture(source);
        var result=new AuditResult{Photos=new List<Photo>{new Photo{Id=1,Code=Code,Kind="Титул"},new Photo{Id=2,Code=Code,Kind="ИУЛ",CRC="A1B2C3D4"}},Directory=root};
        var plan=SummaryWriter.Preview(result,source);Assert(plan.Rows.Count==1&&plan.Documents==1,"Only loaded document must be filled");byte[] original=File.ReadAllBytes(source);var date=new DateTime(2026,2,17,23,50,0);
        SummaryWriter.Save(plan,dest,"Иванов",date,new HashSet<int>());var filled=XlsxReader.Read(dest,"Все загруженные файлы");var row=filled.Rows.First();
        Assert(XlsxReader.Text(row.Values[8])=="Петров, Иванов"&&XlsxReader.DateSerial(row.Values[9])==date.Date.ToOADate(),"Surname and typed date");
        Assert(XlsxReader.Text(row.Values[12])=="1"&&XlsxReader.Text(row.Values[18])=="Прежняя ошибка описи"&&XlsxReader.Text(row.Values[19])=="Прежнее замечание","Clean check preserves existing remarks");
        Assert(filled.Rows[1].Values.Skip(8).All(v=>v==null),"Unprocessed row must be untouched");
        Assert(original.SequenceEqual(File.ReadAllBytes(source)),"Source is unchanged");
        Assert(Entry(source,"xl/worksheets/sheet2.xml")==Entry(dest,"xl/worksheets/sheet2.xml")&&Entry(dest,"custom/preserved.txt")=="unchanged attachment","Other sheets, formulas and attachments preserved");
        Assert(Entry(dest,"xl/workbook.xml").Contains("fullCalcOnLoad=\"1\""),"Excel recalculates formula statistics");
        result.Photos.Add(new Photo{Id=3,Code=Other,Kind="Титул"});
        string both=Path.Combine(root,"both-checked.xlsx");SummaryWriter.Save(SummaryWriter.Preview(result,source),both,"Иванов",date,new HashSet<int>());
        Assert(XlsxReader.Text(XlsxReader.Read(both,"Все загруженные файлы").Rows[1].Values[8])=="Иванов","Second clean document is checked");result.Photos.RemoveAt(2);
        result.Findings.Add(new Finding{Code=Code,Level="Расхождение",Topic="Номер изменения",Detail="Опись: 2; ИУЛ: 1."});
        result.Findings.Add(new Finding{Code=Code,Level="Проверить",Topic="CRC32 не совпадает",Detail="Проверьте чтение."});
        var signatures=new Finding{Code=Code,Level="Проверить",Topic="Подписи: проверка отдельных строк",Detail="Строка требует просмотра."};signatures.Photos.Add(2);result.Findings.Add(signatures);
        SummaryWriter.Save(SummaryWriter.Preview(result,source),dest,"Иванов",date,new HashSet<int>{0});row=XlsxReader.Read(dest,"Все загруженные файлы").Rows[0];
        Assert(XlsxReader.Text(row.Values[18]).Contains("Опись: 2")&&XlsxReader.Text(row.Values[19]).Contains("Проверить: CRC32")&&row.Values[16]==null&&row.Values[17]==null,"Unconfirmed OCR must not set flags; revision uses S");
        SummaryWriter.Save(SummaryWriter.Preview(result,source),dest,"Иванов",date,new HashSet<int>{0,1,2});row=XlsxReader.Read(dest,"Все загруженные файлы").Rows[0];Assert(XlsxReader.Text(row.Values[16])=="1"&&XlsxReader.Text(row.Values[17])=="1"&&row.Values[15]==null,"Confirmed CRC/signature use Q/R, not P");
        var again=SummaryWriter.Preview(result,dest);string dest2=Path.Combine(root,"filled-again.xlsx");SummaryWriter.Save(again,dest2,"Иванов",date,new HashSet<int>{0,1,2});Assert(XlsxReader.Text(XlsxReader.Read(dest2,"Все загруженные файлы").Rows[0].Values[8])=="Петров, Иванов","Surname is not duplicated");
        string multi=Path.Combine(root,"versions.xlsx");Fixture(multi,true);plan=SummaryWriter.Preview(result,multi);Assert(plan.Rows.Count==1&&plan.Rows[0].Source.Row==7,"CRC selects one version");result.Photos[1].CRC=null;plan=SummaryWriter.Preview(result,multi);Assert(plan.Rows.Count==0&&plan.Skipped.Count==1,"Ambiguous versions skipped");result.Photos[1].CRC="A1B2C3D4";
        string oldDate=Path.Combine(root,"1904.xlsx");Fixture(oldDate,false,true);SummaryWriter.Save(SummaryWriter.Preview(result,oldDate),dest,"Иванов",date,new HashSet<int>());Assert(XlsxReader.DateSerial(XlsxReader.Read(dest,"Все загруженные файлы").Rows[0].Values[9])==date.Date.ToOADate(),"1904 date system preserved");
        string formula=Path.Combine(root,"formula.xlsx");Fixture(formula,false,false,true);byte[] before=File.ReadAllBytes(dest);bool refused=false;try{SummaryWriter.Save(SummaryWriter.Preview(result,formula),dest,"Иванов",date,new HashSet<int>());}catch(InvalidDataException){refused=true;}Assert(refused&&before.SequenceEqual(File.ReadAllBytes(dest)),"Formula conflict cannot partially overwrite output");
        refused=false;try{SummaryWriter.Save(SummaryWriter.Preview(result,source),source,"Иванов",date,new HashSet<int>());}catch(InvalidOperationException){refused=true;}Assert(refused,"Source overwrite refused");Assert(!SummaryWriter.ValidSurname("=1+1")&&SummaryWriter.ValidSurname("Иванова-Петрова"),"Surname validation");
        var only=new AuditResult{InventoryOnly=true,Directory=root,Inventory=new List<InventoryEntry>{new InventoryEntry{Code=Code,DocumentationKind="ПД",Box=22},new InventoryEntry{Code=Other,DocumentationKind="ИИ",Box=23}}};
        plan=SummaryWriter.Preview(only,source);Assert(plan.Rows.Count==2&&plan.Documents==2&&plan.MetadataNotes.Count==0,"Inventory-only matches inventory without photos or findings");
        string inventoryDest=Path.Combine(root,"inventory-only.xlsx");SummaryWriter.Save(plan,inventoryDest,"Иванов",date,new HashSet<int>());filled=XlsxReader.Read(inventoryDest,"Все загруженные файлы");
        Assert(XlsxReader.Text(filled.Rows[0].Values[10])=="ПД"&&XlsxReader.Text(filled.Rows[0].Values[11])=="22"&&XlsxReader.Text(filled.Rows[1].Values[10])=="ИИ"&&XlsxReader.Text(filled.Rows[1].Values[11])=="23","Kind and per-row box transferred");
        Assert(filled.Rows.All(item=>XlsxReader.Text(item.Values[19]).Contains("Сверено только по описи"))&&filled.Rows[1].Values.Skip(12).Take(7).All(v=>v==null),"Partial scope visible and no invented issue marks");
        var xml=XDocument.Parse(Entry(inventoryDest,"xl/worksheets/sheet1.xml"));Assert(xml.Descendants(N+"c").First(c=>(string)c.Attribute("r")=="L7").Attribute("t")==null,"Box is numeric");
        only.Inventory[0].DocumentationKind="ИИ";only.Inventory[0].Box=99;plan=SummaryWriter.Preview(only,inventoryDest);Assert(plan.MetadataNotes.Count==2,"Existing metadata conflicts shown in preview");SummaryWriter.Save(plan,dest,"Иванов",date,new HashSet<int>());row=XlsxReader.Read(dest,"Все загруженные файлы").Rows[0];
        Assert(XlsxReader.Text(row.Values[10])=="ПД"&&XlsxReader.Text(row.Values[11])=="22"&&XlsxReader.Text(row.Values[19]).Contains("Прежнее значение сохранено"),"Existing metadata preserved with conflict notes");
        SummaryWriter.Save(SummaryWriter.Preview(only,source,false),dest,"Иванов",date,new HashSet<int>());row=XlsxReader.Read(dest,"Все загруженные файлы").Rows[0];Assert(row.Values[10]==null&&row.Values[11]==null&&XlsxReader.Text(row.Values[8]).Contains("Иванов"),"Optional metadata transfer can be disabled");
        only.Inventory[0].DocumentationKind=null;only.Inventory[0].Box=null;plan=SummaryWriter.Preview(only,source);Assert(plan.MetadataNotes.Count==2,"Missing metadata shown");SummaryWriter.Save(plan,dest,"Иванов",date,new HashSet<int>());row=XlsxReader.Read(dest,"Все загруженные файлы").Rows[0];Assert(row.Values[10]==null&&row.Values[11]==null,"Unknown values must remain blank");
        plan=SummaryWriter.Preview(only,multi);Assert(plan.Rows.Count==0&&plan.Skipped.Count==2,"Inventory alone cannot choose a CRC version; absent rows also skipped");
        Assert(original.SequenceEqual(File.ReadAllBytes(source))&&Entry(source,"xl/worksheets/sheet2.xml")==Entry(dest,"xl/worksheets/sheet2.xml"),"Inventory mode preserves source and formula statistics");
        string attachments=Path.Combine(root,"attachments.xlsx");Fixture(attachments,true);
        using(var z=ZipFile.Open(attachments,ZipArchiveMode.Update)){string part="xl/worksheets/sheet1.xml";XDocument doc;using(var reader=new StreamReader(z.GetEntry(part).Open()))doc=XDocument.Load(reader);doc.Descendants(N+"c").First(c=>(string)c.Attribute("r")=="C8").Descendants(N+"t").First().Value=Code+"-Л1_Состав тома.pdf";z.GetEntry(part).Delete();Put(z,part,doc.ToString());}
        plan=SummaryWriter.Preview(only,attachments);Assert(plan.Rows.Count==1&&plan.Rows[0].Source.Row==7,"Attached documents must not block selection of the base volume");
        var empty=SummaryWriter.Preview(new AuditResult(),source);Assert(empty.Rows.Count==0&&SummaryWriter.MatchMessage(empty).Contains("Сохранение недоступно")&&!SummaryWriter.MatchMessage(empty).Contains("Все распознанные"),"Zero recognition must explain disabled save, not claim success");
        string fragments=Path.Combine(root,"fragments.xlsx");Fixture(fragments,true);const string tkr="123-45-6789-ТКР12.1";
        using(var z=ZipFile.Open(fragments,ZipArchiveMode.Update)){string part="xl/worksheets/sheet1.xml";XDocument doc;using(var reader=new StreamReader(z.GetEntry(part).Open()))doc=XDocument.Load(reader);foreach(int n in new[]{7,8})doc.Descendants(N+"c").First(c=>(string)c.Attribute("r")=="C"+n).Descendants(N+"t").First().Value=tkr+" Фрагмент "+(n-6);z.GetEntry(part).Delete();Put(z,part,doc.ToString());}
        var parts=new AuditResult{InventoryOnly=true,Inventory=new List<InventoryEntry>{new InventoryEntry{Code=tkr,DocumentationKind="ПД",Box=13}}};plan=SummaryWriter.Preview(parts,fragments);Assert(plan.Rows.Count==2&&plan.Skipped.Count==0&&plan.Documents==1,"Distinct fragments with different CRCs are separate files, not conflicting versions");SummaryWriter.Save(plan,dest,"Иванов",date,new HashSet<int>());Assert(XlsxReader.Read(dest,"Все загруженные файлы").Rows.All(item=>XlsxReader.Text(item.Values[11])=="13"),"All fragments receive inventory metadata");
        int arbitraryIndex=0;foreach(string arbitrary in new[]{"АЛЬФА", "007", "СП / Договор №7", "PREFIX-123-45-6789-ИЛО4.1.1-SUFFIX", "Ω/PLAN-07"}){
            string genericFile=Path.Combine(root,"arbitrary-"+(arbitraryIndex++)+".xlsx");Fixture(genericFile);
            using(var z=ZipFile.Open(genericFile,ZipArchiveMode.Update)){string part="xl/worksheets/sheet1.xml";XDocument doc;using(var reader=new StreamReader(z.GetEntry(part).Open()))doc=XDocument.Load(reader);doc.Descendants(N+"c").First(c=>(string)c.Attribute("r")=="C7").Descendants(N+"t").First().Value="Альбом_"+arbitrary+".pdf";z.GetEntry(part).Delete();Put(z,part,doc.ToString());}
            var generic=new AuditResult{InventoryOnly=true,Inventory=new List<InventoryEntry>{new InventoryEntry{Code=DocumentIdentity.Value(arbitrary),DocumentationKind="ПД",Box=7}}};
            plan=SummaryWriter.Preview(generic,genericFile);Assert(plan.Rows.Count==1&&plan.Skipped.Count==0,"Arbitrary code must match Excel: "+arbitrary);SummaryWriter.Save(plan,dest,"Иванов",date,new HashSet<int>());var check=XlsxReader.Read(dest,"Все загруженные файлы");Assert(XlsxReader.Text(check.Rows[0].Values[8]).Contains("Иванов")&&XlsxReader.Text(check.Rows[0].Values[11])=="7"&&check.Rows[1].Values[8]==null,"Arbitrary code fills only matched row");
        }
        Console.WriteLine("Summary tests passed: clean/remarks, typed dates (1900/1904), version matching, confirmed flags, preservation, atomic failure, surname validation.");return 0;
    }
}
