using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace PhotoAudit {
    static class AuditProgram {
        [STAThread]static int Main(string[] args){
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            try {
                if(args.Length>0&&args[0]=="--apply-update")return DesktopUpdates.Updates.Apply(args);
                if(args.Length>=4&&args[0]=="--compare"){QualityBenchmark.Compare(args[1],args[2],args[3],args.Length>4?args[4]:null);return 0;}
                if(args.Length>=3&&args[0]=="--reanalyze"){AuditEngine.Reanalyze(Path.GetFullPath(args[1]),args[2]=="-"?null:args[2]);return 0;}
                if(args.Length>=4&&args[0]=="--batch"){
                    string dest=Path.GetFullPath(args[1]);Directory.CreateDirectory(dest);
                    var result=AuditEngine.Run(args.Skip(3).ToList(),args[2]=="-"?null:args[2],dest,(p,s)=>File.WriteAllText(Path.Combine(dest,"Ход_проверки.txt"),p+"% "+s,new UTF8Encoding(false)),CancellationToken.None);return 0;
                }
                if(args.Length>=2&&args[0]=="--snapshot")using(var form=new AuditForm()){foreach(var p in args.Skip(2))form.AddFile(p);form.Opacity=0;form.Show();Application.DoEvents();form.PerformLayout();using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(0,0,form.Width,form.Height));bmp.Save(args[1]);}return 0;}
                Application.Run(new AuditForm());return 0;
            }catch(Exception ex){if(args.Length>1&&args[0]=="--batch")File.WriteAllText(Path.Combine(args[1],"Ошибка.txt"),ex.ToString(),Encoding.UTF8);else MessageBox.Show(ex.Message,"Автопроверка документов",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}
        }
    }
    public sealed class AuditForm:Form {
        readonly List<string> inputs=new List<string>();readonly ListBox list=new ListBox();
        readonly TextBox registry=new TextBox(),output=new TextBox();readonly RichTextBox log=new RichTextBox();
        readonly ProgressBar bar=new ProgressBar();readonly Button run=new Button(),cancel=new Button(),open=new Button();
        readonly Label counts=new Label();readonly List<Button> editing=new List<Button>();CancellationTokenSource token;bool busy;string report;
        public AuditForm(){
            Text="Автопроверка документов — изображения и PDF";Font=new Font("Segoe UI",10);ClientSize=new Size(1030,715);MinimumSize=new Size(950,730);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(246,248,250);AllowDrop=true;
            var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=10};Controls.Add(grid);
            float[] fixedRows={44,45,40,0,42,42,60,26,0,50};for(int i=0;i<fixedRows.Length;i++)grid.RowStyles.Add(new RowStyle(fixedRows[i]==0?SizeType.Percent:SizeType.Absolute,fixedRows[i]==0?50:fixedRows[i]));
            var titleBar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};titleBar.Controls.Add(new Label{Text="Автопроверка документов",Font=new Font("Segoe UI",19,FontStyle.Bold),AutoSize=true,ForeColor=Color.FromArgb(28,46,65)});titleBar.Controls.Add(DesktopUpdates.Updates.Attach(this,()=>busy));grid.Controls.Add(titleBar,0,0);
            grid.Controls.Add(new Label{Text="Загрузите опись, титулы и все страницы ИУЛ. Программа распознает текст и покажет возможные расхождения.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(73,91,108)},0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};actions.Controls.Add(Edit("Добавить фото / PDF / ZIP",215,(s,e)=>Choose()));actions.Controls.Add(Edit("Добавить папку",150,(s,e)=>Folder()));actions.Controls.Add(Edit("Удалить",100,(s,e)=>{foreach(int i in list.SelectedIndices.Cast<int>().OrderByDescending(x=>x).ToList())inputs.RemoveAt(i);RefreshInputs();}));actions.Controls.Add(Edit("Сравнить версии",170,(s,e)=>Benchmark()));grid.Controls.Add(actions,0,2);
            list.Dock=DockStyle.Fill;list.SelectionMode=SelectionMode.MultiExtended;list.HorizontalScrollbar=true;grid.Controls.Add(list,0,3);
            grid.Controls.Add(PathRow("Реестр Excel:",registry,(s,e)=>{using(var d=new OpenFileDialog{Filter="Книги Excel|*.xlsx",Title="Реестр для сверки CRC32"})if(d.ShowDialog(this)==DialogResult.OK)registry.Text=d.FileName;}),0,4);
            output.Text=AppDomain.CurrentDomain.BaseDirectory;grid.Controls.Add(PathRow("Папка отчётов:",output,(s,e)=>{using(var d=new FolderBrowserDialog{Description="Папка для нового отчёта и изображений"})if(d.ShowDialog(this)==DialogResult.OK)output.Text=d.SelectedPath;}),0,5);
            grid.Controls.Add(new Label{Text="Названия, шифры, изменения, CRC32 и фамилии — по распознанному тексту.\nКаждая подписная строка и круглая печать проверяются отдельно; учитываются чёрные штрихи.\nРаспознавание выполняется локально средствами Windows; нужен русский компонент OCR.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(73,91,108)},0,6);
            bar.Dock=DockStyle.Fill;grid.Controls.Add(bar,0,7);log.Dock=DockStyle.Fill;log.ReadOnly=true;log.BackColor=Color.White;log.Text="Добавьте изображения, PDF или ZIP. Реестр Excel нужен для сверки контрольных сумм.\nИсходные файлы сохраняются без изменений. Каждый запуск создаёт отдельную папку отчёта.";grid.Controls.Add(log,0,8);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,6,0,0)};
            run.Text="Проверить";run.Width=155;run.Height=33;run.BackColor=Color.FromArgb(35,69,100);run.ForeColor=Color.White;run.FlatStyle=FlatStyle.Flat;run.Click+=(s,e)=>Start();bottom.Controls.Add(run);
            cancel.Text="Отмена";cancel.Width=100;cancel.Height=33;cancel.Enabled=false;cancel.Click+=(s,e)=>{if(token!=null)token.Cancel();cancel.Enabled=false;};bottom.Controls.Add(cancel);
            open.Text="Открыть отчёт";open.Width=155;open.Height=33;open.Enabled=false;open.Click+=(s,e)=>{if(File.Exists(report))Process.Start(new ProcessStartInfo(report){UseShellExecute=true});};bottom.Controls.Add(open);
            counts.AutoSize=true;counts.Padding=new Padding(0,6,10,0);counts.Text="Файлы не выбраны";bottom.Controls.Add(counts);grid.Controls.Add(bottom,0,9);
            DragEnter+=(s,e)=>{if(!busy&&e.Data.GetDataPresent(DataFormats.FileDrop))e.Effect=DragDropEffects.Copy;};DragDrop+=(s,e)=>{if(!busy)foreach(string p in (string[])e.Data.GetData(DataFormats.FileDrop))AddFile(p);};
            FormClosing+=(s,e)=>{if(busy){e.Cancel=true;if(token!=null)token.Cancel();Append("Запрошена отмена. Дождитесь завершения текущего распознавания.");}};
        }
        Button Edit(string text,int width,EventHandler handler){var b=new Button{Text=text,Width=width,Height=31};b.Click+=handler;editing.Add(b);return b;}
        TableLayoutPanel PathRow(string label,TextBox box,EventHandler action){var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3};p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));p.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,110));p.Controls.Add(new Label{Text=label,AutoSize=true,Anchor=AnchorStyles.Left},0,0);box.Dock=DockStyle.Fill;p.Controls.Add(box,1,0);p.Controls.Add(Edit("Обзор…",100,action),2,0);return p;}
        void Benchmark(){
            using(var before=new OpenFileDialog{Filter="Результаты проверки|*.json",Title="Результаты ДО изменения программы"})if(before.ShowDialog(this)==DialogResult.OK)
            using(var after=new OpenFileDialog{Filter="Результаты проверки|*.json",Title="Результаты ПОСЛЕ изменения программы"})if(after.ShowDialog(this)==DialogResult.OK)
            using(var truth=new OpenFileDialog{Filter="Ручной эталон (необязательно)|*.json",Title="Выберите ручной эталон; Отмена — сравнение без эталона"}){
                string reference=truth.ShowDialog(this)==DialogResult.OK?truth.FileName:null;
                using(var dest=new SaveFileDialog{Filter="Отчёт сравнения|*.html",FileName="Сравнение_качества.html"})if(dest.ShowDialog(this)==DialogResult.OK){try{QualityBenchmark.Compare(before.FileName,after.FileName,dest.FileName,reference);Process.Start(new ProcessStartInfo(dest.FileName){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,ex.Message,"Сравнение качества");}}
            }
        }
        void Choose(){using(var d=new OpenFileDialog{Filter="Изображения, PDF и ZIP|*.jpg;*.jpeg;*.png;*.bmp;*.pdf;*.zip",Multiselect=true,Title="Выберите фотографии, PDF или архив"})if(d.ShowDialog(this)==DialogResult.OK)foreach(var p in d.FileNames)AddFile(p);}
        void Folder(){using(var d=new FolderBrowserDialog{Description="Папка фотографий и PDF, включая вложенные папки"})if(d.ShowDialog(this)==DialogResult.OK)AddFile(d.SelectedPath);}
        public void AddFile(string path){path=Path.GetFullPath(path);if(!inputs.Any(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase))){inputs.Add(path);RefreshInputs();}}
        void RefreshInputs(){list.Items.Clear();foreach(var p in inputs)list.Items.Add(p);counts.Text="Выбрано: "+inputs.Count;}
        void Append(string s){log.AppendText("\n"+s);log.SelectionStart=log.TextLength;log.ScrollToCaret();}
        void Ui(Action action){if(!IsDisposed&&IsHandleCreated)BeginInvoke(action);}
        void SetBusy(bool value){busy=value;run.Enabled=!value;cancel.Enabled=value;open.Enabled=!value&&report!=null;registry.Enabled=!value;output.Enabled=!value;list.Enabled=!value;foreach(var b in editing)b.Enabled=!value;}
        void Start(){
            if(inputs.Count==0){MessageBox.Show(this,"Добавьте фотографии, PDF или ZIP.",Text);return;}
            if(registry.Text!=""&&!File.Exists(registry.Text)){MessageBox.Show(this,"Реестр Excel не найден.",Text);return;}
            string dest;try{dest=Path.Combine(Path.GetFullPath(output.Text),"Проверка_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));}catch(Exception ex){MessageBox.Show(this,ex.Message,Text);return;}
            var selected=inputs.ToList();string reference=registry.Text;token=new CancellationTokenSource();SetBusy(true);bar.Value=0;Append("Начата проверка.");
            var worker=new Thread(()=>{try{var result=AuditEngine.Run(selected,reference,dest,(p,s)=>Ui(()=>{bar.Value=Math.Min(100,p);Append(s);}),token.Token);Ui(()=>{report=result.Report;Append("Отчёт: "+report);counts.Text="Фото: "+result.Images+"; пунктов: "+result.Findings.Count;SetBusy(false);token.Dispose();token=null;});}
                catch(OperationCanceledException){Ui(()=>{Append("Проверка отменена. Частичные данные сохранены в папке запуска.");SetBusy(false);token.Dispose();token=null;});}
                catch(Exception ex){Ui(()=>{Append("Ошибка: "+ex.Message);SetBusy(false);token.Dispose();token=null;MessageBox.Show(this,ex.Message,"Не удалось выполнить проверку",MessageBoxButtons.OK,MessageBoxIcon.Error);});}});worker.IsBackground=true;worker.SetApartmentState(ApartmentState.STA);worker.Start();
        }
    }
}
