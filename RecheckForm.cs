using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace PhotoAudit {
    public sealed class RecheckForm:Form {
        readonly TextBox source=new TextBox{ReadOnly=true,Dock=DockStyle.Fill};
        readonly DataGridView pages=new DataGridView{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoGenerateColumns=false,RowHeadersVisible=false,BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.FullRowSelect};
        readonly CheckBox cached=new CheckBox{Text="Только пересчитать замечания по сохранённому OCR",AutoSize=true};
        readonly Label count=new Label{Dock=DockStyle.Fill};readonly Button start=new Button{Text="Повторить OCR",Width=190,Height=34,Enabled=false};
        List<Photo> loaded=new List<Photo>();
        public string SourceRoot {get;private set;}public bool RepeatOcr {get{return !cached.Checked;}}
        public List<int> SelectedPages {get{return pages.Rows.Cast<DataGridViewRow>().Where(r=>Convert.ToBoolean(r.Cells[0].Value??false)).Select(r=>(int)r.Tag).ToList();}}
        public RecheckForm(string initial){
            Text="Повторная проверка — выбрать страницы";Font=new Font("Segoe UI",10);ClientSize=new Size(960,620);MinimumSize=new Size(820,520);StartPosition=FormStartPosition.CenterParent;Icon=Icon.ExtractAssociatedIcon(typeof(AuditForm).Assembly.Location);
            var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(20),ColumnCount=1,RowCount=6};Controls.Add(grid);
            foreach(float h in new[]{60f,42f,44f,0f,35f,45f})grid.RowStyles.Add(new RowStyle(h==0?SizeType.Percent:SizeType.Absolute,h==0?100:h));
            grid.Controls.Add(new Label{Text="Выберите сохранённый отчёт и отметьте страницы для повторного OCR. Остальные результаты будут взяты из кэша. Исходные PDF и фотографии открывать не требуется.",Dock=DockStyle.Fill},0,0);
            var path=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2};path.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));path.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,170));path.Controls.Add(source);var choose=new Button{Text="Открыть отчёт…",Dock=DockStyle.Fill};choose.Click+=(s,e)=>{using(var d=new OpenFileDialog{Filter="Сохранённый отчёт|Отчет.html;Результаты.json|HTML / JSON|*.html;*.json",Title="Отчёт предыдущей проверки"})if(d.ShowDialog(this)==DialogResult.OK)LoadReport(d.FileName);};path.Controls.Add(choose);grid.Controls.Add(path,0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};AddAction(actions,"Все",80,p=>true);AddAction(actions,"Снять выбор",125,p=>false);AddAction(actions,"Только опись",130,p=>p.Kind=="Опись"||p.ManualInventory);cached.Margin=new Padding(15,8,0,0);actions.Controls.Add(cached);cached.CheckedChanged+=(s,e)=>{pages.Enabled=!cached.Checked;UpdateCount();};grid.Controls.Add(actions,0,2);
            pages.Columns.Add(new DataGridViewCheckBoxColumn{HeaderText="OCR",Width=55});pages.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="№",Width=50,ReadOnly=true});pages.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Вид",Width=130,ReadOnly=true});pages.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Шифр",Width=220,ReadOnly=true});pages.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Файл / страница",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,ReadOnly=true});
            pages.CurrentCellDirtyStateChanged+=(s,e)=>{if(pages.IsCurrentCellDirty)pages.CommitEdit(DataGridViewDataErrorContexts.Commit);};pages.CellValueChanged+=(s,e)=>UpdateCount();
            pages.CellDoubleClick+=(s,e)=>{if(e.RowIndex>=0&&e.ColumnIndex!=0){var p=loaded.First(v=>v.Id==(int)pages.Rows[e.RowIndex].Tag);Process.Start(new ProcessStartInfo(p.Image){UseShellExecute=true});}};grid.Controls.Add(pages,0,3);
            grid.Controls.Add(count,0,4);var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};start.Click+=(s,e)=>{pages.EndEdit();if(RepeatOcr&&SelectedPages.Count==0)return;DialogResult=DialogResult.OK;Close();};buttons.Controls.Add(start);var back=new Button{Text="Отмена",Width=120,Height=34,DialogResult=DialogResult.Cancel};buttons.Controls.Add(back);grid.Controls.Add(buttons,0,5);CancelButton=back;
            if(!string.IsNullOrEmpty(initial))LoadReport(initial);else UpdateCount();
        }
        void AddAction(FlowLayoutPanel host,string text,int width,Func<Photo,bool> select){var b=new Button{Text=text,Width=width,Height=31};b.Click+=(s,e)=>{pages.EndEdit();foreach(DataGridViewRow row in pages.Rows)row.Cells[0].Value=select(loaded.First(p=>p.Id==(int)row.Tag));UpdateCount();};host.Controls.Add(b);}
        void LoadReport(string file){try{var next=Recheck.Load(file);SourceRoot=Recheck.ReportRoot(file);loaded=next;source.Text=SourceRoot;pages.Rows.Clear();foreach(var p in loaded){int index=pages.Rows.Add(false,p.Id,p.Kind,p.Code,p.Original);pages.Rows[index].Tag=p.Id;}UpdateCount();}catch(Exception ex){MessageBox.Show(this,ex.Message,"Не удалось открыть сохранённый отчёт",MessageBoxButtons.OK,MessageBoxIcon.Error);}}
        void UpdateCount(){int selected=SelectedPages.Count;count.Text=SourceRoot==null?"Откройте отчёт предыдущей проверки.":cached.Checked?"Повторное OCR не выполняется. Страниц из кэша: "+loaded.Count:"Повторное OCR: "+selected+"; страниц из кэша: "+(loaded.Count-selected)+". Двойной щелчок открывает сохранённую страницу.";start.Text=cached.Checked?"Пересчитать замечания":"Повторить OCR";start.Enabled=SourceRoot!=null&&(cached.Checked||selected>0);}
    }
}
