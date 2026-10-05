using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PhotoAudit {
    public sealed class SummaryFillForm:Form {
        readonly AuditResult result;readonly DateTime checkedAt;readonly TextBox surname=new TextBox(),source=new TextBox(),status=new TextBox();
        readonly DataGridView findings=new DataGridView();readonly Button save=new Button();SummaryPlan plan;
        readonly CheckBox inventoryMetadata=new CheckBox{Text="Заполнить вид документации и номер короба по описи",Checked=true,AutoSize=true};
        readonly DataGridView inventoryRows=new DataGridView();
        public string SavedPath {get;private set;}
        public SummaryFillForm(AuditResult audit,string workbook,DateTime date){
            result=audit;checkedAt=date;Icon=Icon.ExtractAssociatedIcon(typeof(AuditForm).Assembly.Location);
            Text="Заполнить сводную таблицу";Font=new Font("Segoe UI",10);ClientSize=new Size(940,result.InventoryOnly?720:670);MinimumSize=new Size(840,result.InventoryOnly?700:650);StartPosition=FormStartPosition.CenterParent;BackColor=Color.FromArgb(246,248,250);
            var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=8};Controls.Add(grid);
            foreach(int h in new[]{40,42,44,result.InventoryOnly?32:1,100,72,0,44})grid.RowStyles.Add(new RowStyle(h==0?SizeType.Percent:SizeType.Absolute,h==0?100:h));
            grid.Controls.Add(new Label{Text="Результаты проверки → сводная таблица",Dock=DockStyle.Fill,Font=new Font("Segoe UI",17,FontStyle.Bold)},0,0);
            var who=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};who.Controls.Add(new Label{Text="Фамилия:",AutoSize=true,Padding=new Padding(0,6,8,0)});surname.Width=270;who.Controls.Add(surname);who.Controls.Add(new Label{Text="Дата проверки: "+checkedAt.ToString("dd.MM.yyyy"),AutoSize=true,Padding=new Padding(20,6,0,0)});grid.Controls.Add(who,0,1);
            var file=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};file.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));file.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));file.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));file.Controls.Add(new Label{Text="Таблица Excel:",AutoSize=true,Anchor=AnchorStyles.Left},0,0);source.Dock=DockStyle.Fill;source.ReadOnly=true;source.Text=workbook??"";file.Controls.Add(source,1,0);var choose=new Button{Text="Выбрать…",Width=105,Height=30};choose.Click+=(s,e)=>{using(var d=new OpenFileDialog{Filter="Сводная таблица Excel|*.xlsx",Title="Таблица с листом «Все загруженные файлы»"})if(d.ShowDialog(this)==DialogResult.OK){source.Text=d.FileName;RefreshPreview();}};file.Controls.Add(choose,2,0);grid.Controls.Add(file,0,2);
            inventoryMetadata.Visible=result.InventoryOnly;inventoryMetadata.CheckedChanged+=(s,e)=>RefreshPreview();grid.Controls.Add(inventoryMetadata,0,3);
            status.Dock=DockStyle.Fill;status.ReadOnly=true;status.Multiline=true;status.ScrollBars=ScrollBars.Vertical;status.BackColor=Color.White;grid.Controls.Add(status,0,4);
            grid.Controls.Add(new Label{Text=result.InventoryOnly?"По описи заполняются фамилия, дата, вид документации и номер короба.\nВ примечании будет указано, что титулы и ИУЛ не проверялись. Прежние значения и формулы сохраняются.\nНеоднозначные строки пропускаются; результат записывается в отдельную копию Excel.":"Отметка «Подтверждено» переносит ошибку в соответствующую графу.\nОстальные пункты сохраняются в примечании как «Проверить». Если пунктов нет, заполняются только фамилия и дата.\nПрежние замечания и отметки сохраняются. Результат записывается в отдельную копию Excel.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(65,80,95)},0,5);
            findings.Dock=DockStyle.Fill;findings.AllowUserToAddRows=false;findings.AllowUserToDeleteRows=false;findings.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;findings.RowHeadersVisible=false;findings.BackgroundColor=Color.White;findings.DefaultCellStyle.WrapMode=DataGridViewTriState.True;findings.SelectionMode=DataGridViewSelectionMode.FullRowSelect;findings.AutoGenerateColumns=false;
            findings.Columns.Add(new DataGridViewCheckBoxColumn{Name="Confirmed",HeaderText="Подтверждено",Width=115,SortMode=DataGridViewColumnSortMode.NotSortable});
            foreach(var spec in new[]{new[]{"Code","Шифр","165"},new[]{"Topic","Пункт отчёта","200"},new[]{"Column","Куда записать","150"}})findings.Columns.Add(new DataGridViewTextBoxColumn{Name=spec[0],HeaderText=spec[1],Width=int.Parse(spec[2]),ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
            findings.Columns.Add(new DataGridViewTextBoxColumn{Name="Detail",HeaderText="Пояснение",ReadOnly=true,AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,MinimumWidth=190,SortMode=DataGridViewColumnSortMode.NotSortable});
            for(int i=0;i<result.Findings.Count;i++){var f=result.Findings[i];int index=findings.Rows.Add(f.Level=="Расхождение",f.Code,f.Topic,SummaryWriter.ColumnLabel(result,f),f.Detail);findings.Rows[index].Tag=i;}
            if(result.InventoryOnly){
                var tabs=new TabControl{Dock=DockStyle.Fill};var entries=new TabPage("Строки описи");var issues=new TabPage("Замечания");entries.Controls.Add(inventoryRows);issues.Controls.Add(findings);tabs.TabPages.Add(entries);tabs.TabPages.Add(issues);grid.Controls.Add(tabs,0,6);
                inventoryRows.Dock=DockStyle.Fill;inventoryRows.ReadOnly=true;inventoryRows.AllowUserToAddRows=false;inventoryRows.AllowUserToDeleteRows=false;inventoryRows.RowHeadersVisible=false;inventoryRows.BackgroundColor=Color.White;inventoryRows.AutoSizeRowsMode=DataGridViewAutoSizeRowsMode.AllCells;inventoryRows.DefaultCellStyle.WrapMode=DataGridViewTriState.True;
                foreach(var spec in new[]{new[]{"Code","Шифр","175"},new[]{"Kind","Вид","55"},new[]{"Box","Короб","60"},new[]{"Excel","Строки Excel","105"}})inventoryRows.Columns.Add(new DataGridViewTextBoxColumn{Name=spec[0],HeaderText=spec[1],Width=int.Parse(spec[2]),SortMode=DataGridViewColumnSortMode.NotSortable});
                inventoryRows.Columns.Add(new DataGridViewTextBoxColumn{Name="Book",HeaderText="Название из описи",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,MinimumWidth=240});
            }else grid.Controls.Add(findings,0,6);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};save.Text="Сохранить копию…";save.Width=180;save.Height=34;save.Click+=(s,e)=>SaveCopy();buttons.Controls.Add(save);var cancel=new Button{Text="Отмена",Width=110,Height=34,DialogResult=DialogResult.Cancel};buttons.Controls.Add(cancel);grid.Controls.Add(buttons,0,7);AcceptButton=save;CancelButton=cancel;
            Shown+=(s,e)=>{RefreshPreview();findings.ClearSelection();findings.CurrentCell=null;surname.Focus();};
        }
        void RefreshPreview(){
            plan=null;save.Enabled=false;
            if(string.IsNullOrWhiteSpace(source.Text)){status.Text="Выберите сводную таблицу Excel. Нужен основной лист «Все загруженные файлы» с графами «Проверил» и «Дата проверки».";return;}
            try{plan=SummaryWriter.Preview(result,source.Text,inventoryMetadata.Checked);save.Enabled=plan.Rows.Count>0;status.Text=(result.InventoryOnly?"Режим: только опись. Распознано записей: "+result.Inventory.Count+".\r\n":"")+"Будет заполнено строк: "+plan.Rows.Count+"; документов: "+plan.Documents+". Пунктов отчёта: "+result.Findings.Count+".\r\n"+(plan.Skipped.Count==0?"Все распознанные документы сопоставлены с таблицей.":"Не будут заполнены:\r\n"+string.Join("\r\n",plan.Skipped))+(plan.MetadataNotes.Count>0?"\r\nТребует внимания:\r\n"+string.Join("\r\n",plan.MetadataNotes):"");
                if(result.InventoryOnly){inventoryRows.Rows.Clear();foreach(var item in result.Inventory)inventoryRows.Rows.Add(item.Code,item.DocumentationKind??"—",item.Box.HasValue?item.Box.Value.ToString():"—",string.Join(", ",plan.Rows.Where(row=>row.Code==item.Code).Select(row=>row.Source.Row.ToString())),item.Book);inventoryRows.ClearSelection();inventoryRows.CurrentCell=null;}
            }
            catch(Exception ex){status.Text="Не удалось прочитать таблицу: "+ex.Message;}
        }
        void SaveCopy(){
            if(!SummaryWriter.ValidSurname(surname.Text)){MessageBox.Show(this,"Введите фамилию буквами (до 80 символов).",Text,MessageBoxButtons.OK,MessageBoxIcon.Information);surname.Focus();return;}
            if(plan==null)return;findings.EndEdit();var confirmed=new HashSet<int>(findings.Rows.Cast<DataGridViewRow>().Where(row=>Convert.ToBoolean(row.Cells["Confirmed"].Value)).Select(row=>(int)row.Tag));
            using(var d=new SaveFileDialog{Filter="Книга Excel|*.xlsx",DefaultExt="xlsx",AddExtension=true,OverwritePrompt=true,InitialDirectory=result.Directory,FileName="Свод_проверки_"+checkedAt.ToString("yyyyMMdd_HHmmss")+".xlsx",Title="Сохранить заполненную копию таблицы"})if(d.ShowDialog(this)==DialogResult.OK){
                try{UseWaitCursor=true;save.Enabled=false;SummaryWriter.Save(plan,d.FileName,surname.Text,checkedAt,confirmed);SavedPath=d.FileName;DialogResult=DialogResult.OK;Close();}
                catch(Exception ex){MessageBox.Show(this,ex.Message,"Не удалось заполнить таблицу",MessageBoxButtons.OK,MessageBoxIcon.Error);}
                finally{UseWaitCursor=false;save.Enabled=true;}
            }
        }
    }
}
