using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using ReviewMerge;

namespace PhotoAudit {
    public sealed class SummaryRow {
        public SourceRow Source;
        public string Code;
        public InventoryEntry Inventory;
    }
    public sealed class SummaryPlan {
        public string Source;
        public AuditResult Result;
        public bool IncludeInventoryMetadata;
        public int Candidates;
        public readonly List<SummaryRow> Rows=new List<SummaryRow>();
        public readonly List<string> Skipped=new List<string>();
        public readonly List<string> MetadataNotes=new List<string>();
        public int Documents {get{return Rows.Select(r=>r.Code).Distinct().Count();}}
    }
    public static class SummaryWriter {
        const string MainSheet="Все загруженные файлы";
        static readonly XNamespace N="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        static readonly XNamespace R="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        static readonly CultureInfo Inv=CultureInfo.InvariantCulture;
        public static bool ValidSurname(string name){return Regex.IsMatch((name??"").Trim(),@"^[\p{L}][\p{L} '\-]{0,79}$");}
        public static SummaryPlan Preview(AuditResult result,string source,bool includeInventoryMetadata=true){
            if(result==null)throw new ArgumentNullException("result");
            var plan=new SummaryPlan{Source=Path.GetFullPath(source),Result=result,IncludeInventoryMetadata=result.InventoryOnly&&includeInventoryMetadata};
            var book=XlsxReader.Read(plan.Source,MainSheet);
            var codes=(result.InventoryOnly?result.Inventory.Select(i=>i.Code):result.Photos.Where(p=>p.Kind!="Опись"&&p.Code!=null).Select(p=>p.Code).Concat(result.Findings.Select(f=>AuditEngine.DocumentCode(f.Code)))).Where(c=>c!=null).Distinct().OrderBy(c=>c,StringComparer.Ordinal).ToList();
            var matcher=new DocumentIdentity.Matcher(codes);var groups=book.Rows.Select(row=>new SummaryRow{Source=row,Code=matcher.Match(XlsxReader.Text(row.Values[2]))}).Where(row=>row.Code!=null&&BaseVolumeFile(row)).GroupBy(row=>row.Code).ToDictionary(g=>g.Key,g=>g.ToList());
            plan.Candidates=codes.Count;
            foreach(var code in codes){
                List<SummaryRow> candidates;
                if(!groups.TryGetValue(code,out candidates)){plan.Skipped.Add(code+": строка в Excel не найдена.");continue;}
                foreach(var part in candidates.GroupBy(row=>Fragment(row.Source.Values[2]))){
                var selected=part.ToList();var versions=selected.Select(row=>XlsxReader.Normal(row.Source.Values[7])).Distinct().ToList();
                if(versions.Count>1){
                    var read=result.Photos.Where(p=>p.Code==code&&p.Kind=="ИУЛ"&&!string.IsNullOrWhiteSpace(p.CRC)).Select(p=>XlsxReader.Normal(p.CRC)).Distinct().ToList();
                    var matches=versions.Where(v=>v!=""&&read.Contains(v)).ToList();
                    if(matches.Count!=1){plan.Skipped.Add(code+(part.Key==""?"":" / фрагмент "+part.Key)+(result.InventoryOnly?": в Excel несколько версий с разными CRC32; по одной описи выбрать версию невозможно.":": несколько версий с разными контрольными суммами; однозначное соответствие не найдено."));continue;}
                    selected=selected.Where(row=>XlsxReader.Normal(row.Source.Values[7])==matches[0]).ToList();
                }
                foreach(var item in selected){item.Inventory=result.Inventory.FirstOrDefault(i=>i.Code==code);plan.Rows.Add(item);if(plan.IncludeInventoryMetadata)plan.MetadataNotes.AddRange(MetadataNotes(item).Select(note=>code+", строка "+item.Source.Row+": "+note));}
                }
            }
            return plan;
        }
        static string Fragment(object name){var m=Regex.Match(XlsxReader.Normal(name),@"ФРАГМЕНТ\s*(?<n>[0-9]{1,3})(?![0-9])");return m.Success?int.Parse(m.Groups["n"].Value).ToString(Inv):"";}
        public static string MatchMessage(SummaryPlan plan){
            if(plan.Candidates==0)return "Шифры документов и строки описи не распознаны. Сохранение недоступно: неизвестно, какие строки Excel заполнять. Укажите файл описи или повторите распознавание в актуальной версии программы.";
            if(plan.Rows.Count==0)return "Нет однозначно сопоставленных строк Excel. Сохранение недоступно. Причины перечислены ниже.";
            return plan.Skipped.Count==0?"Все распознанные документы сопоставлены с таблицей.":"Часть строк пропущена; причины перечислены ниже.";
        }
        static bool BaseVolumeFile(SummaryRow item){
            string name=Regex.Replace(XlsxReader.Normal(item.Source.Values[2]),@"\s+","");name=Regex.Replace(name,@"\.(PDF|DOCX?|XLSX?|DWG|ZIP)$","");
            string normalizedCode=DocumentIdentity.Key(item.Code);int start=name.IndexOf(normalizedCode,StringComparison.Ordinal);if(start<0)return true;
            string tail=name.Substring(start+normalizedCode.Length);
            // Codes ending in -УЛ, -Л1, -О or .1 describe attached documents, not another CRC version of the volume.
            return !Regex.IsMatch(tail,@"^(?:\.[А-ЯA-Z0-9]|-[А-ЯA-Z0-9])")||Regex.IsMatch(tail,@"^-ИЗМ[.0-9(]");
        }
        static IEnumerable<string> MetadataNotes(SummaryRow item){
            var entry=item.Inventory;string kind=entry==null?null:entry.DocumentationKind;int? box=entry==null?null:entry.Box;
            string oldKind=XlsxReader.Text(item.Source.Values[10]),oldBox=XlsxReader.Text(item.Source.Values[11]);double number;
            if(string.IsNullOrWhiteSpace(kind))yield return "Вид документации по описи не распознан; графа K сохранена.";
            else if(!string.IsNullOrWhiteSpace(oldKind)&&XlsxReader.Normal(oldKind)!=XlsxReader.Normal(kind))yield return "Вид документации: в Excel «"+oldKind+"», в описи «"+kind+"». Прежнее значение сохранено.";
            if(!box.HasValue)yield return "Номер короба по описи не распознан; графа L сохранена.";
            else if(!string.IsNullOrWhiteSpace(oldBox)&&(!double.TryParse(oldBox,NumberStyles.Float,Inv,out number)||number!=box.Value))yield return "Короб: в Excel «"+oldBox+"», в описи «"+box.Value+"». Прежнее значение сохранено.";
        }
        // Only confirmed findings set numeric flags. Every finding remains visible in notes.
        public static string Columns(AuditResult result,Finding f){
            string topic=f.Topic??"";
            if(topic.IndexOf("CRC32",StringComparison.OrdinalIgnoreCase)>=0||topic.IndexOf("контрольн",StringComparison.OrdinalIgnoreCase)>=0)return "Q";
            if(topic=="Номер изменения")return "S";
            if(topic=="Название книги в ИУЛ"||topic=="Название на титуле"||topic=="Название раздела / части")return "M";
            if(topic=="Раздел / часть: опись и документ")return "MS";
            if(topic=="Печать на титуле")return "P";
            if(topic=="Подписная таблица не распознана")return "R";
            if(topic=="Подписи: проверка отдельных строк"){
                bool title=result.Photos.Any(p=>f.Photos.Contains(p.Id)&&(p.Kind=="Титул"||p.Kind=="Титул фрагмента"));
                bool iul=result.Photos.Any(p=>f.Photos.Contains(p.Id)&&p.Kind=="ИУЛ");
                return (title?"P":"")+(iul?"R":"");
            }
            return "";
        }
        public static string ColumnLabel(AuditResult result,Finding f){
            if(AuditEngine.DocumentCode(f.Code)==null)return "T — общее примечание";
            string columns=Columns(result,f);
            if(columns=="")return "T — примечание";
            return string.Join(", ",columns.Select(c=>c== 'S'?"S — ошибки в описи":c+" — отметка 1"));
        }
        static XDocument Load(ZipArchive zip,string name){
            var entry=zip.GetEntry(name);if(entry==null)throw new InvalidDataException("В Excel отсутствует "+name);
            using(var stream=entry.Open())using(var reader=XmlReader.Create(stream,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=150000000}))return XDocument.Load(reader,LoadOptions.PreserveWhitespace);
        }
        static string SheetPath(XDocument book,XDocument rels){
            var sheet=book.Descendants(N+"sheet").First(s=>string.Equals(((string)s.Attribute("name")??"").Trim(),MainSheet,StringComparison.OrdinalIgnoreCase));
            string id=(string)sheet.Attribute(R+"id");var rel=rels.Root.Elements().First(e=>(string)e.Attribute("Id")==id);
            string target=((string)rel.Attribute("Target")).Replace('\\','/');target=target.StartsWith("/")?target.TrimStart('/'):"xl/"+target;
            if(target.Contains("../"))throw new InvalidDataException("Неподдерживаемая ссылка на основной лист.");return target;
        }
        static int Col(string address){int col=0;foreach(char c in address){if(c<'A'||c>'Z')break;col=col*26+c-'A'+1;}return col;}
        static XElement Cell(XElement row,char column){
            string address=column+((string)row.Attribute("r"));var cell=row.Elements(N+"c").FirstOrDefault(c=>(string)c.Attribute("r")==address);
            if(cell!=null){if(cell.Element(N+"f")!=null)throw new InvalidDataException("Ячейка "+address+" содержит формулу; запись отменена, чтобы сохранить её.");return cell;}
            cell=new XElement(N+"c",new XAttribute("r",address));var next=row.Elements(N+"c").FirstOrDefault(c=>Col((string)c.Attribute("r"))>column-'A'+1);if(next==null)row.Add(cell);else next.AddBeforeSelf(cell);return cell;
        }
        static void SetText(XElement cell,string value){
            if(value.Length>32767)throw new InvalidDataException("Примечание в "+(string)cell.Attribute("r")+" превышает лимит Excel (32767 символов).");
            cell.RemoveNodes();cell.SetAttributeValue("t","inlineStr");cell.Add(new XElement(N+"is",new XElement(N+"t",new XAttribute(XNamespace.Xml+"space","preserve"),value)));
        }
        static string Append(string before,IEnumerable<string> values){
            var parts=(before??"").Split(new[]{'\n'},StringSplitOptions.RemoveEmptyEntries).Select(s=>s.TrimEnd('\r')).ToList();
            foreach(var value in values)if(!parts.Contains(value))parts.Add(value);return string.Join("\n",parts);
        }
        static int Style(XDocument styles,int previous,int dateFormat,bool wrap){
            var xfs=styles.Root.Element(N+"cellXfs");var original=xfs.Elements().ElementAtOrDefault(previous)??xfs.Elements().First();var xf=new XElement(original);
            if(dateFormat>=0){xf.SetAttributeValue("numFmtId",dateFormat);xf.SetAttributeValue("applyNumberFormat",1);}
            if(wrap){var a=xf.Element(N+"alignment");if(a==null){a=new XElement(N+"alignment");xf.Add(a);}a.SetAttributeValue("wrapText",1);a.SetAttributeValue("vertical","top");xf.SetAttributeValue("applyAlignment",1);}
            int index=xfs.Elements().Count();xfs.Add(xf);xfs.SetAttributeValue("count",index+1);return index;
        }
        static int DateFormat(XDocument styles){
            var formats=styles.Root.Element(N+"numFmts");if(formats==null){formats=new XElement(N+"numFmts");styles.Root.AddFirst(formats);}
            var existing=formats.Elements().FirstOrDefault(e=>(string)e.Attribute("formatCode")=="dd.mm.yyyy");if(existing!=null)return (int)existing.Attribute("numFmtId");
            int id=Math.Max(164,formats.Elements().Select(e=>(int)e.Attribute("numFmtId")).DefaultIfEmpty(163).Max()+1);formats.Add(new XElement(N+"numFmt",new XAttribute("numFmtId",id),new XAttribute("formatCode","dd.mm.yyyy")));formats.SetAttributeValue("count",formats.Elements().Count());return id;
        }
        static double ColumnWidth(XDocument sheet,int number){var cols=sheet.Root.Element(N+"cols");var col=cols==null?null:cols.Elements(N+"col").FirstOrDefault(c=>(int)c.Attribute("min")<=number&&(int)c.Attribute("max")>=number);return col==null?8.43:(double?)col.Attribute("width")??8.43;}
        static void FitNoteColumn(XDocument sheet,int number){
            if(ColumnWidth(sheet,number)>=52)return;
            var cols=sheet.Root.Element(N+"cols");if(cols==null){cols=new XElement(N+"cols");sheet.Root.Element(N+"sheetData").AddBeforeSelf(cols);}
            var old=cols.Elements(N+"col").FirstOrDefault(c=>(int)c.Attribute("min")<=number&&(int)c.Attribute("max")>=number);var target=old==null?new XElement(N+"col"):new XElement(old);
            if(old!=null){int min=(int)old.Attribute("min"),max=(int)old.Attribute("max");if(min<number){var left=new XElement(old);left.SetAttributeValue("max",number-1);old.AddBeforeSelf(left);}if(max>number){var right=new XElement(old);right.SetAttributeValue("min",number+1);old.AddAfterSelf(right);}old.ReplaceWith(target);}
            else{var next=cols.Elements().FirstOrDefault(c=>(int)c.Attribute("min")>number);if(next==null)cols.Add(target);else next.AddBeforeSelf(target);}
            target.SetAttributeValue("min",number);target.SetAttributeValue("max",number);target.SetAttributeValue("width",52);target.SetAttributeValue("customWidth",1);
        }
        static void FitRow(XElement row,XDocument sheet,XDocument styles){
            double height=(double?)row.Attribute("ht")??(double?)sheet.Root.Element(N+"sheetFormatPr").NullAttribute("defaultRowHeight")??15;
            foreach(char column in new[]{'I','S','T'}){var cell=row.Elements(N+"c").FirstOrDefault(c=>Col((string)c.Attribute("r"))==column-'A'+1);if(cell==null||cell.Element(N+"is")==null)continue;
                string text=string.Concat(cell.Descendants(N+"t").Select(t=>t.Value));int perLine=Math.Max(1,(int)(ColumnWidth(sheet,column-'A'+1)/1.25));int lines=text.Split('\n').Sum(line=>Math.Max(1,(line.Length+perLine-1)/perLine));
                int style=(int?)cell.Attribute("s")??0;var xf=styles.Root.Element(N+"cellXfs").Elements().ElementAt(style);int font=(int?)xf.Attribute("fontId")??0;var f=styles.Root.Element(N+"fonts").Elements().ElementAt(font);double size=(double?)f.Element(N+"sz").NullAttribute("val")??11;
                height=Math.Max(height,Math.Min(409.5,lines*(size*1.45)+5));
            }
            row.SetAttributeValue("ht",height);row.SetAttributeValue("customHeight",1);
        }
        static XAttribute NullAttribute(this XElement element,string name){return element==null?null:element.Attribute(name);}
        static void Store(ZipArchive zip,string name,XDocument data){var entry=zip.CreateEntry(name,CompressionLevel.Optimal);using(var stream=entry.Open())data.Save(stream,SaveOptions.DisableFormatting);}
        public static void Save(SummaryPlan plan,string output,string surname,DateTime checkedAt,ISet<int> confirmed){
            surname=(surname??"").Trim();if(!ValidSurname(surname))throw new ArgumentException("Введите фамилию буквами (до 80 символов).");
            if(plan.Rows.Count==0)throw new InvalidOperationException("Нет однозначно сопоставленных строк для заполнения.");
            output=Path.GetFullPath(output);if(string.Equals(plan.Source,output,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Выберите новое имя файла: исходная таблица сохраняется без изменений.");
            ParallelWork.CheckDiskSpace(output,new FileInfo(plan.Source).Length*2);
            // Re-read at save time so a table edited while the dialog was open is not overwritten by stale data.
            plan=Preview(plan.Result,plan.Source,plan.IncludeInventoryMetadata);if(plan.Rows.Count==0)throw new InvalidOperationException("Строки таблицы изменились. Выберите файл заново.");
            var result=plan.Result;confirmed=confirmed??new HashSet<int>();string temp=Path.Combine(Path.GetDirectoryName(output),".audit-summary-"+Guid.NewGuid().ToString("N")+".xlsx");
            try{using(var input=new FileStream(plan.Source,FileMode.Open,FileAccess.Read,FileShare.Read))using(var zip=new ZipArchive(input,ZipArchiveMode.Read)){
                var book=Load(zip,"xl/workbook.xml");var rels=Load(zip,"xl/_rels/workbook.xml.rels");string path=SheetPath(book,rels);var sheet=Load(zip,path);var styles=Load(zip,"xl/styles.xml");
                if(sheet.Root.Element(N+"sheetProtection")!=null)throw new InvalidDataException("Основной лист защищён. Снимите защиту в своей копии Excel перед заполнением.");
                var rows=sheet.Root.Element(N+"sheetData").Elements(N+"row").ToDictionary(row=>(int)row.Attribute("r"));int format=DateFormat(styles);var styleCache=new Dictionary<string,int>();
                if(result.Findings.Count>0||result.InventoryOnly)FitNoteColumn(sheet,20);
                if(result.Findings.Select((f,i)=>new {f,i}).Any(v=>confirmed.Contains(v.i)&&Columns(result,v.f).Contains("S")))FitNoteColumn(sheet,19);
                var wp=book.Root.Element(N+"workbookPr");bool date1904=wp!=null&&new[]{"1","true"}.Contains((string)wp.Attribute("date1904"));double date=checkedAt.Date.ToOADate()-(date1904?1462:0);
                foreach(var item in plan.Rows){
                    XElement row;if(!rows.TryGetValue(item.Source.Row,out row))throw new InvalidDataException("В Excel исчезла строка "+item.Source.Row);
                    var applicable=result.Findings.Select((f,i)=>new {Finding=f,Index=i}).Where(f=>AuditEngine.DocumentCode(f.Finding.Code)==item.Code||AuditEngine.DocumentCode(f.Finding.Code)==null).ToList();
                    var reviewer=Cell(row,'I');var names=XlsxReader.Text(item.Source.Values[8]).Split(new[]{',',';','\n'},StringSplitOptions.RemoveEmptyEntries).Select(n=>n.Trim()).ToList();if(!names.Any(n=>string.Equals(n,surname,StringComparison.OrdinalIgnoreCase)))names.Add(surname);SetText(reviewer,string.Join(", ",names));
                    var dc=Cell(row,'J');int prior=(int?)dc.Attribute("s")??0;string styleKey=prior+"|date";int style;if(!styleCache.TryGetValue(styleKey,out style)){style=Style(styles,prior,format,false);styleCache[styleKey]=style;}dc.RemoveNodes();dc.SetAttributeValue("t",null);dc.SetAttributeValue("s",style);dc.Add(new XElement(N+"v",date.ToString(Inv)));
                    var notes=new List<string>();var inventory=new List<string>();var marks=new HashSet<char>();
                    if(result.InventoryOnly){
                        notes.Add("Сверено только по описи. Титулы, ИУЛ, подписи, печати и CRC32 по документам не проверялись.");
                        if(plan.IncludeInventoryMetadata){
                            var entry=item.Inventory;
                            if(entry!=null&&!string.IsNullOrWhiteSpace(entry.DocumentationKind)&&string.IsNullOrWhiteSpace(XlsxReader.Text(item.Source.Values[10])))SetText(Cell(row,'K'),entry.DocumentationKind);
                            if(entry!=null&&entry.Box.HasValue&&string.IsNullOrWhiteSpace(XlsxReader.Text(item.Source.Values[11]))){var box=Cell(row,'L');box.RemoveNodes();box.SetAttributeValue("t",null);box.Add(new XElement(N+"v",entry.Box.Value));}
                            notes.AddRange(MetadataNotes(item));
                        }
                    }
                    foreach(var f in applicable){
                        string level=confirmed.Contains(f.Index)?"Расхождение":f.Finding.Level=="Расхождение"?"Проверить":f.Finding.Level;
                        string note=level+": "+f.Finding.Topic+". "+(f.Finding.Detail??"").Replace("\r","").Replace("\n"," ");if(AuditEngine.DocumentCode(f.Finding.Code)==null)note="Общая проверка — "+note;
                        notes.Add(note);if(!confirmed.Contains(f.Index)||AuditEngine.DocumentCode(f.Finding.Code)==null)continue;
                        foreach(char c in Columns(result,f.Finding))if(c=='S')inventory.Add(note);else marks.Add(c);
                    }
                    foreach(char c in marks){var cell=Cell(row,c);cell.RemoveNodes();cell.SetAttributeValue("t",null);cell.Add(new XElement(N+"v","1"));}
                    if(inventory.Count>0)SetText(Cell(row,'S'),Append(XlsxReader.Text(item.Source.Values[18]),inventory));
                    if(notes.Count>0)SetText(Cell(row,'T'),Append(XlsxReader.Text(item.Source.Values[19]),notes));
                    foreach(char c in new[]{'I','S','T'}){var cell=row.Elements(N+"c").FirstOrDefault(e=>(string)e.Attribute("r")==c+item.Source.Row.ToString(Inv));if(cell==null||cell.Element(N+"is")==null)continue;prior=(int?)cell.Attribute("s")??0;styleKey=prior+"|wrap";if(!styleCache.TryGetValue(styleKey,out style)){style=Style(styles,prior,-1,true);styleCache[styleKey]=style;}cell.SetAttributeValue("s",style);}
                    FitRow(row,sheet,styles);
                }
                var calc=book.Root.Element(N+"calcPr");if(calc==null){calc=new XElement(N+"calcPr");var after=book.Root.Elements().FirstOrDefault(e=>new[]{"oleSize","customWorkbookViews","pivotCaches","smartTagPr","smartTagTypes","webPublishing","fileRecoveryPr","webPublishObjects","extLst"}.Contains(e.Name.LocalName));if(after==null)book.Root.Add(calc);else after.AddBeforeSelf(calc);}calc.SetAttributeValue("calcMode","auto");calc.SetAttributeValue("fullCalcOnLoad",1);calc.SetAttributeValue("forceFullCalc",1);
                using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write))using(var dest=new ZipArchive(stream,ZipArchiveMode.Create)){
                    foreach(var entry in zip.Entries){if(entry.FullName==path||entry.FullName=="xl/workbook.xml"||entry.FullName=="xl/styles.xml")continue;var copy=dest.CreateEntry(entry.FullName,CompressionLevel.Optimal);using(var sourceStream=entry.Open())using(var target=copy.Open())sourceStream.CopyTo(target);}
                    Store(dest,path,sheet);Store(dest,"xl/workbook.xml",book);Store(dest,"xl/styles.xml",styles);
                }
            }
                if(File.Exists(output))File.Replace(temp,output,null,true);else File.Move(temp,output);
            }finally{if(File.Exists(temp))File.Delete(temp);}
        }
    }
}
