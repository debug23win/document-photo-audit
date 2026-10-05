using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using System.Web.Script.Serialization;

namespace PhotoAudit {
    // The reference is supplied by a human. Expected values are never passed into OCR.
    public static class QualityBenchmark {
        static readonly JavaScriptSerializer Json=new JavaScriptSerializer{MaxJsonLength=100000000};
        static string H(object value){return HttpUtility.HtmlEncode(Convert.ToString(value));}
        static Dictionary<string,object> Read(string file){return (Dictionary<string,object>)Json.DeserializeObject(File.ReadAllText(file,Encoding.UTF8));}
        static List<Dictionary<string,object>> Rows(Dictionary<string,object> d,string name){return d.ContainsKey(name)?((object[])d[name]).Select(o=>(Dictionary<string,object>)o).ToList():new List<Dictionary<string,object>>();}
        static string S(Dictionary<string,object> row,string name){return row.ContainsKey(name)?Convert.ToString(row[name]):"";}
        public static Dictionary<string,object> Evaluate(Dictionary<string,object> report,Dictionary<string,object> reference) {
            var photos=Rows(report,"Photos");var findings=Rows(report,"Findings");var m=new Dictionary<string,object>();
            m["Images"]=photos.Count;m["Warnings"]=findings.Count(f=>S(f,"Level")=="Проверить");m["Discrepancies"]=findings.Count(f=>S(f,"Level")=="Расхождение");
            m["CRCRecognized"]=photos.Count(p=>S(p,"CRC")!="");m["RevisionRecognized"]=photos.Count(p=>S(p,"Revision")!="");m["PagesRecognized"]=photos.Count(p=>S(p,"Page")!="");
            if(reference==null)return m;
            var known=Rows(reference,"KnownFindings");var negatives=Rows(reference,"NegativeChecks");var fields=Rows(reference,"Fields");
            Func<Dictionary<string,object>,Dictionary<string,object>,bool> same=(a,b)=>S(a,"Code")==S(b,"Code")&&(a.ContainsKey("Topics")?((object[])a["Topics"]).Any(t=>Convert.ToString(t)==S(b,"Topic")):S(a,"Topic")==S(b,"Topic"))&&(S(a,"Photo")==""||((object[])b["Photos"]).Any(p=>Convert.ToString(p)==S(a,"Photo")));
            m["KnownTotal"]=known.Count;m["KnownFound"]=known.Count(k=>findings.Any(f=>same(k,f)));
            m["NegativeTotal"]=negatives.Count;m["FalseWarnings"]=negatives.Count(k=>findings.Any(f=>same(k,f)&&(S(k,"Photo")==""||((object[])f["Photos"]).Any(p=>Convert.ToString(p)==S(k,"Photo")))));
            m["UnlabelledFindings"]=findings.Count(f=>!known.Any(k=>same(k,f))&&!negatives.Any(k=>same(k,f)));
            int correct=0,wrong=0,missing=0;var details=new List<object>();
            foreach(var item in fields){var p=photos.FirstOrDefault(a=>S(a,"Id")==S(item,"Photo"));string actual=p==null?"":S(p,S(item,"Field")),expected=S(item,"Value"),status=actual==expected?"Верно":actual==""?"Не распознано":"Неверно";
                if(status=="Верно")correct++;else if(status=="Не распознано")missing++;else wrong++;
                details.Add(new Dictionary<string,object>{{"Photo",S(item,"Photo")},{"Field",S(item,"Field")},{"Expected",expected},{"Actual",actual},{"Status",status}});}
            m["FieldsTotal"]=fields.Count;m["FieldsCorrect"]=correct;m["FieldsWrong"]=wrong;m["FieldsMissing"]=missing;m["FieldDetails"]=details;
            return m;
        }
        public static void Compare(string before,string after,string destination,string referenceFile) {
            var a=Read(before);var b=Read(after);var reference=string.IsNullOrWhiteSpace(referenceFile)?null:Read(referenceFile);var first=Evaluate(a,reference);var second=Evaluate(b,reference);
            var titles=new Dictionary<string,string>{{"Images","Страниц / изображений"},{"Discrepancies","Отметок «Расхождение»"},{"Warnings","Отметок «Проверить»"},{"CRCRecognized","Распознано CRC"},{"RevisionRecognized","Распознано изменений"},{"PagesRecognized","Распознано номеров листов"},{"KnownTotal","Известных ручных находок"},{"KnownFound","Найдено ручных находок"},{"NegativeTotal","Проверок без ошибки по эталону"},{"FalseWarnings","Ложных предупреждений на размеченных проверках"},{"UnlabelledFindings","Находок без оценки в эталоне"},{"FieldsTotal","Полей в ручном эталоне"},{"FieldsCorrect","Полей распознано верно"},{"FieldsWrong","Полей распознано неверно"},{"FieldsMissing","Полей не распознано"}};
            var html=new StringBuilder("<!doctype html><html lang='ru'><meta charset='utf-8'><title>Сравнение качества</title><style>body{font:16px/1.5 Segoe UI,Arial;background:#f3f5f7;color:#1c2e41;margin:32px auto;max-width:1100px;padding:20px}table{border-collapse:collapse;background:white;width:100%;margin:20px 0}td,th{padding:12px;border:1px solid #dce3ea;text-align:left}th{background:#234564;color:white}</style><h1>Сравнение качества проверки</h1>");
            html.Append("<p>До: "+H(Path.GetFileName(before))+"<br>После: "+H(Path.GetFileName(after))+"</p><p>"+(reference==null?"Ручной эталон не выбран. Количество предупреждений и распознанных полей показывает покрытие, но не доказывает точность.":"Эталон: "+H(S(reference,"Name"))+". Оценены только размеченные поля и проверки. Остальные находки не объявляются верными или ложными.")+"</p><table><tr><th>Показатель</th><th>До</th><th>После</th></tr>");
            foreach(var title in titles)if(first.ContainsKey(title.Key)&&second.ContainsKey(title.Key))html.Append("<tr><td>"+H(title.Value)+"</td><td>"+H(first[title.Key])+"</td><td>"+H(second[title.Key])+"</td></tr>");html.Append("</table>");
            if(reference!=null){html.Append("<h2>Поля нового запуска</h2><table><tr><th>Фото / страница</th><th>Поле</th><th>Эталон</th><th>Прочитано</th><th>Результат</th></tr>");foreach(var o in (List<object>)second["FieldDetails"]){var row=Json.ConvertToType<Dictionary<string,object>>(o);html.Append("<tr><td>"+H(S(row,"Photo"))+"</td><td>"+H(S(row,"Field"))+"</td><td>"+H(S(row,"Expected"))+"</td><td>"+H(S(row,"Actual"))+"</td><td>"+H(S(row,"Status"))+"</td></tr>");}html.Append("</table>");}
            html.Append("<p>Проверяйте новую версию также на независимых документах. Искусственные тесты проверяют отдельные сценарии и не заменяют оценку на новых реальных документах.</p></html>");
            File.WriteAllText(destination,html.ToString(),new UTF8Encoding(false));File.WriteAllText(Path.ChangeExtension(destination,".json"),Json.Serialize(new {Before=first,After=second,Reference=referenceFile}),new UTF8Encoding(false));
        }
    }
}
