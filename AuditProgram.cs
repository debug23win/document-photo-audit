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
            Application.ThreadException+=(s,e)=>MessageBox.Show("Ошибка сохранена: "+LogError(e.Exception,null)+"\n"+e.Exception.Message,"Автопроверка документов");
            try {
                bool adaptiveNumeric=true;var inventoryInputs=new List<string>();if(args.Length>0&&args[0]=="--batch"){var batch=args.ToList();adaptiveNumeric=!batch.Remove("--thorough");int index;while((index=batch.IndexOf("--inventory"))>=0){if(index+1>=batch.Count)throw new ArgumentException("После --inventory укажите файл описи.");inventoryInputs.Add(batch[index+1]);batch.RemoveRange(index,2);}args=batch.ToArray();}
                int requestedWorkers=0;if(args.Length>=3&&args[0]=="--batch"&&args[1]=="--workers"){requestedWorkers=int.Parse(args[2]);args=new[]{"--batch"}.Concat(args.Skip(3)).ToArray();}
                if(args.Length>0&&args[0]=="--apply-update")return DesktopUpdates.Updates.Apply(args);
                if(args.Length>=4&&args[0]=="--render-pdf"){NativeWindows.RenderPdf(args[1],Path.GetFullPath(args[2]),int.Parse(args[3]),ParallelWork.Workers(args.Length>4?int.Parse(args[4]):0,512),s=>Console.WriteLine(s),CancellationToken.None);return 0;}
                if(args.Length>=3&&args[0]=="--ocr"){NativeWindows.Recognize(args[1],args[2],ParallelWork.Workers(args.Length>3?int.Parse(args[3]):0,192),args.Length>4&&args[4]=="1",s=>Console.WriteLine(s),CancellationToken.None);return 0;}
                if(args.Length>=4&&args[0]=="--compare"){QualityBenchmark.Compare(args[1],args[2],args[3],args.Length>4?args[4]:null);return 0;}
                if(args.Length>=6&&args[0]=="--recheck"){Recheck.Run(args[1],args[3]=="-"?null:args[3],args[2],args[5]=="-"?new List<int>():args[5].Split(',').Select(int.Parse).ToList(),args[4]=="ocr",(p,t)=>Console.WriteLine(p+"% "+t),CancellationToken.None);return 0;}
                if(args.Length>=3&&args[0]=="--reanalyze"){AuditEngine.Reanalyze(Path.GetFullPath(args[1]),args[2]=="-"?null:args[2]);return 0;}
                if(args.Length>=3&&args[0]=="--batch"&&(args.Length>=4||inventoryInputs.Count>0)){
                    string dest=Path.GetFullPath(args[1]);Directory.CreateDirectory(dest);
                    var result=AuditEngine.Run(args.Skip(3).ToList(),args[2]=="-"?null:args[2],dest,(p,s)=>File.WriteAllText(Path.Combine(dest,"Ход_проверки.txt"),p+"% "+s,new UTF8Encoding(false)),CancellationToken.None,requestedWorkers,inventoryInputs,null,adaptiveNumeric);return 0;
                }
                if(args.Length>=2&&args[0]=="--snapshot")using(var form=new AuditForm()){foreach(var p in args.Skip(2))form.AddFile(p);form.Opacity=0;form.Show();Application.DoEvents();form.PerformLayout();using(var bmp=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bmp,new Rectangle(0,0,form.Width,form.Height));bmp.Save(args[1]);}return 0;}
                Application.Run(new AuditForm());return 0;
            }catch(Exception ex){string root=args.Length>1&&args[0]=="--batch"?args[1]:args.Length>2&&(args[0]=="--ocr"||args[0]=="--render-pdf")?args[2]:null;ex=ParallelWork.StorageError(ex,root);string log=LogError(ex,root);if(args.Length==0)MessageBox.Show(ex.Message+"\nЖурнал: "+log,"Автопроверка документов",MessageBoxButtons.OK,MessageBoxIcon.Error);else Console.Error.WriteLine(ex);return 1;}
        }
        internal static string LogError(Exception ex,string root){try{root=root??AppDomain.CurrentDomain.BaseDirectory;Directory.CreateDirectory(root);string path=Path.Combine(root,"Ошибка.txt");File.WriteAllText(path,ex.ToString(),new UTF8Encoding(true));return path;}catch{return "не удалось записать журнал";}}
    }
    public sealed class AuditForm:Form {
        readonly List<string> inputs=new List<string>();readonly ListBox list=new ListBox();
        readonly HashSet<string> inventoryInputs=new HashSet<string>(StringComparer.OrdinalIgnoreCase);readonly Label inventoryStatus=new Label();
        readonly CheckBox adaptive=new CheckBox{Text="Ускорять уверенно прочитанные поля",Checked=true,AutoSize=true,Padding=new Padding(8,5,0,0)};
        readonly TextBox registry=new TextBox(),output=new TextBox();readonly RichTextBox log=new RichTextBox();
        readonly ProgressBar bar=new ProgressBar();readonly Button run=new Button(),cancel=new Button(),open=new Button(),summary=new Button();
        AuditResult lastResult;DateTime completedAt;string lastRegistry;
        InventoryPreview pendingInventory;InventoryReviewGate pendingReview;
        readonly Label counts=new Label();readonly List<Button> editing=new List<Button>();CancellationTokenSource token;bool busy;string report;
        readonly ComboBox parallel=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=95};
        public AuditForm(){
            Icon=Icon.ExtractAssociatedIcon(typeof(AuditForm).Assembly.Location);
            Text="Автопроверка документов — изображения и PDF";Font=new Font("Segoe UI",10);ClientSize=new Size(1030,805);MinimumSize=new Size(950,810);StartPosition=FormStartPosition.CenterScreen;BackColor=Color.FromArgb(246,248,250);AllowDrop=true;
            var grid=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(24),ColumnCount=1,RowCount=12};Controls.Add(grid);
            float[] fixedRows={44,45,40,0,40,30,42,42,60,26,0,50};for(int i=0;i<fixedRows.Length;i++)grid.RowStyles.Add(new RowStyle(fixedRows[i]==0?SizeType.Percent:SizeType.Absolute,fixedRows[i]==0?50:fixedRows[i]));
            var titleBar=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};titleBar.Controls.Add(new Label{Text="Автопроверка документов",Font=new Font("Segoe UI",19,FontStyle.Bold),AutoSize=true,ForeColor=Color.FromArgb(28,46,65)});titleBar.Controls.Add(DesktopUpdates.Updates.Attach(this,()=>busy));grid.Controls.Add(titleBar,0,0);
            grid.Controls.Add(new Label{Text="Загрузите документы для проверки или только опись и Excel для заполнения таблицы. Программа распознает текст и покажет возможные расхождения.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(73,91,108)},0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};actions.Controls.Add(Edit("Добавить файлы",215,(s,e)=>Choose()));actions.Controls.Add(Edit("Добавить папку",150,(s,e)=>Folder()));actions.Controls.Add(Edit("Удалить",100,(s,e)=>{foreach(int i in list.SelectedIndices.Cast<int>().OrderByDescending(x=>x).ToList()){inventoryInputs.Remove(inputs[i]);inputs.RemoveAt(i);}RefreshInputs();}));actions.Controls.Add(Edit("Сравнить версии",170,(s,e)=>Benchmark()));actions.Controls.Add(new Label{Text="Потоки:",AutoSize=true,Padding=new Padding(8,7,0,0)});parallel.Items.AddRange(new object[]{"Авто","1","2","3","4","6","8"});parallel.SelectedIndex=0;parallel.Margin=new Padding(3,3,0,0);actions.Controls.Add(parallel);grid.Controls.Add(actions,0,2);
            list.Dock=DockStyle.Fill;list.SelectionMode=SelectionMode.MultiExtended;list.HorizontalScrollbar=true;grid.Controls.Add(list,0,3);
            var inventoryActions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};inventoryActions.Controls.Add(Edit("Указать файл описи…",195,(s,e)=>ChooseInventory()));inventoryActions.Controls.Add(Edit("Снять отметку описи",195,(s,e)=>{foreach(int i in list.SelectedIndices.Cast<int>())inventoryInputs.Remove(inputs[i]);RefreshInputs();}));inventoryActions.Controls.Add(adaptive);inventoryActions.Controls.Add(Edit("Повторная проверка…",180,(s,e)=>Repeat()));grid.Controls.Add(inventoryActions,0,4);
            inventoryStatus.Text="Перед основной проверкой будет показано количество страниц описи.";inventoryStatus.Dock=DockStyle.Fill;inventoryStatus.ForeColor=Color.FromArgb(35,69,100);grid.Controls.Add(inventoryStatus,0,5);
            grid.Controls.Add(PathRow("Файл проверки:",registry,(s,e)=>{using(var d=new OpenFileDialog{Filter="Книги Excel|*.xlsx",Title="Таблица для заполнения и сверки CRC32"})if(d.ShowDialog(this)==DialogResult.OK)registry.Text=d.FileName;}),0,6);
            output.Text=AppDomain.CurrentDomain.BaseDirectory;grid.Controls.Add(PathRow("Папка отчётов:",output,(s,e)=>{using(var d=new FolderBrowserDialog{Description="Папка для нового отчёта и изображений"})if(d.ShowDialog(this)==DialogResult.OK)output.Text=d.SelectedPath;}),0,7);
            grid.Controls.Add(new Label{Text="Названия, шифры, изменения, CRC32 и фамилии — по распознанному тексту.\nКаждая подписная строка и круглая печать проверяются отдельно; учитываются чёрные штрихи.\nФон очищается и контраст усиливается автоматически. OCR выполняется локально; нужен русский компонент OCR.",Dock=DockStyle.Fill,ForeColor=Color.FromArgb(73,91,108)},0,8);
            bar.Dock=DockStyle.Fill;grid.Controls.Add(bar,0,9);log.Dock=DockStyle.Fill;log.ReadOnly=true;log.BackColor=Color.White;log.Text="Добавьте изображения, PDF или ZIP. Файл проверки Excel нужен для заполнения таблицы и сверки контрольных сумм. Для заполнения по описи титулы и ИУЛ загружать не требуется.\nПосле предварительного поиска отметьте страницы описи. «Продолжить позже» оставляет запуск на паузе без повторного OCR; «Продолжить…» возвращает к выбору.\nИсходные файлы сохраняются без изменений. Каждый запуск создаёт отдельную папку отчёта.";grid.Controls.Add(log,0,10);
            var bottom=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Padding=new Padding(0,6,0,0)};
            run.Text="Проверить";run.Width=155;run.Height=33;run.BackColor=Color.FromArgb(35,69,100);run.ForeColor=Color.White;run.FlatStyle=FlatStyle.Flat;run.Click+=(s,e)=>Start();bottom.Controls.Add(run);
            cancel.Text="Отмена";cancel.Width=100;cancel.Height=33;cancel.Enabled=false;cancel.Click+=(s,e)=>{if(token!=null)token.Cancel();cancel.Enabled=false;};bottom.Controls.Add(cancel);
            open.Text="Открыть отчёт";open.Width=155;open.Height=33;open.Enabled=false;open.Click+=(s,e)=>{if(File.Exists(report))Process.Start(new ProcessStartInfo(report){UseShellExecute=true});};bottom.Controls.Add(open);
            summary.Text="Заполнить свод…";summary.Width=170;summary.Height=33;summary.Enabled=false;summary.Click+=(s,e)=>FillSummary(false);bottom.Controls.Add(summary);
            counts.AutoSize=true;counts.Padding=new Padding(0,6,10,0);counts.Text="Файлы не выбраны";bottom.Controls.Add(counts);grid.Controls.Add(bottom,0,11);
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
        void Choose(){using(var d=new OpenFileDialog{Filter="Изображения, PDF, ZIP и Excel|*.jpg;*.jpeg;*.png;*.bmp;*.pdf;*.zip;*.xlsx",Multiselect=true,Title="Выберите фотографии, PDF, архив или файл проверки Excel"})if(d.ShowDialog(this)==DialogResult.OK)foreach(var p in d.FileNames)AddFile(p);}
        void ChooseInventory(){using(var d=new OpenFileDialog{Filter="Фотографии описи или PDF|*.jpg;*.jpeg;*.png;*.bmp;*.pdf",Multiselect=true,Title="Укажите опись: все страницы выбранных файлов будут считаться описью"})if(d.ShowDialog(this)==DialogResult.OK){foreach(var p in d.FileNames){AddFile(p);inventoryInputs.Add(Path.GetFullPath(p));}RefreshInputs();}}
        void Folder(){using(var d=new FolderBrowserDialog{Description="Папка фотографий и PDF, включая вложенные папки"})if(d.ShowDialog(this)==DialogResult.OK)AddFile(d.SelectedPath);}
        public void AddFile(string path){path=Path.GetFullPath(path);if(Path.GetExtension(path).Equals(".xlsx",StringComparison.OrdinalIgnoreCase)){registry.Text=path;return;}if(!inputs.Any(p=>string.Equals(p,path,StringComparison.OrdinalIgnoreCase))){inputs.Add(path);RefreshInputs();}}
        void RefreshInputs(){list.Items.Clear();foreach(var p in inputs)list.Items.Add((inventoryInputs.Contains(p)?"[Опись] ":"")+p);counts.Text="Выбрано: "+inputs.Count;inventoryStatus.Text="Файлов описи указано вручную: "+inventoryInputs.Count+". Число страниц будет определено перед проверкой.";}
        void Append(string s){log.AppendText("\n"+s);log.SelectionStart=log.TextLength;log.ScrollToCaret();}
        void Ui(Action action){if(!IsDisposed&&IsHandleCreated)BeginInvoke(action);}
        void SetBusy(bool value){busy=value;run.Text="Проверить";run.Enabled=!value;cancel.Enabled=value;open.Enabled=!value&&report!=null;summary.Enabled=!value&&lastResult!=null;registry.Enabled=!value;output.Enabled=!value;list.Enabled=!value;parallel.Enabled=!value;adaptive.Enabled=!value;foreach(var b in editing)b.Enabled=!value;if(!value){pendingInventory=null;pendingReview=null;}}
        void ReviewInventory(){
            if(pendingInventory==null||pendingReview==null||token==null||token.IsCancellationRequested)return;
            run.Enabled=false;
            using(var dialog=new InventoryPreviewForm(pendingInventory)){
                var decision=dialog.ShowDialog(this);pendingInventory.Refresh();inventoryStatus.Text="Выбрано страниц описи: "+pendingInventory.Pages+" из "+pendingInventory.Images;
                if(decision==DialogResult.Retry){run.Text="Продолжить…";run.Enabled=true;Append("Выбор описи отложен. Подготовленные страницы и первичное OCR сохранены в текущем запуске. Нажмите «Продолжить…», чтобы вернуться к выбору страниц.");return;}
                bool proceed=decision==DialogResult.OK;var gate=pendingReview;pendingInventory=null;pendingReview=null;run.Text="Проверить";if(proceed)Append("Начата основная проверка с выбранными страницами описи.");gate.Complete(proceed);
            }
        }
        bool BeforeAudit(InventoryPreview preview){
            using(var gate=new InventoryReviewGate()){
                Invoke(new Action(()=>{pendingInventory=preview;pendingReview=gate;ReviewInventory();}));
                return gate.Wait(token.Token);
            }
        }
        void FillSummary(bool ask){
            if(lastResult==null)return;
            if(ask&&MessageBox.Show(this,"Проверка завершена. Заполнить сводную таблицу?","Проверка завершена",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return;
            using(var dialog=new SummaryFillForm(lastResult,lastRegistry,completedAt))if(dialog.ShowDialog(this)==DialogResult.OK)Append("Сводная таблица: "+dialog.SavedPath);
        }
        void Repeat(){
            using(var dialog=new RecheckForm(report)){
                if(dialog.ShowDialog(this)!=DialogResult.OK)return;
                if(registry.Text!=""&&!File.Exists(registry.Text)){MessageBox.Show(this,"Файл проверки Excel не найден.",Text);return;}
                string dest;try{dest=Path.Combine(Path.GetFullPath(output.Text),"Повторная_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));}catch(Exception ex){MessageBox.Show(this,ex.Message,Text);return;}
                string source=dialog.SourceRoot,reference=registry.Text;var ids=dialog.SelectedPages;bool repeat=dialog.RepeatOcr;int workers=parallel.SelectedIndex==0?0:int.Parse(Convert.ToString(parallel.SelectedItem));token=new CancellationTokenSource();SetBusy(true);bar.Value=0;Append("Повторная проверка сохранённого отчёта: "+source);
                var worker=new Thread(()=>{try{
                    var result=Recheck.Run(source,reference,dest,ids,repeat,(p,s)=>Ui(()=>{bar.Value=Math.Min(100,p);Append(s);}),token.Token,workers);var finished=DateTime.Now;
                    Ui(()=>{report=result.Report;lastResult=result;completedAt=finished;lastRegistry=reference;Append("Отчёт: "+report);inventoryStatus.Text="Страниц описи: "+result.InventoryPages;counts.Text="Фото: "+result.Images+"; пунктов: "+result.Findings.Count;SetBusy(false);token.Dispose();token=null;FillSummary(true);});
                }catch(OperationCanceledException){Ui(()=>{Append("Повторная проверка отменена. Исходный отчёт сохранён.");SetBusy(false);token.Dispose();token=null;});}
                catch(Exception ex){ex=ParallelWork.StorageError(ex,dest);string error=AuditProgram.LogError(ex,dest);Ui(()=>{Append("Ошибка: "+ex.Message+"\nЖурнал: "+error);SetBusy(false);token.Dispose();token=null;MessageBox.Show(this,ex.Message,"Не удалось повторить проверку",MessageBoxButtons.OK,MessageBoxIcon.Error);});}});worker.IsBackground=true;worker.SetApartmentState(ApartmentState.STA);worker.Start();
            }
        }
        void Start(){
            if(pendingInventory!=null){ReviewInventory();return;}
            if(inputs.Count==0){MessageBox.Show(this,"Добавьте фотографии, PDF или ZIP.",Text);return;}
            if(registry.Text!=""&&!File.Exists(registry.Text)){MessageBox.Show(this,"Файл проверки Excel не найден.",Text);return;}
            string dest;try{dest=Path.Combine(Path.GetFullPath(output.Text),"Проверка_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff"));}catch(Exception ex){MessageBox.Show(this,ex.Message,Text);return;}
            var selected=inputs.ToList();var manual=inventoryInputs.ToList();bool fast=adaptive.Checked;string reference=registry.Text;int workers=parallel.SelectedIndex==0?0:int.Parse(Convert.ToString(parallel.SelectedItem));token=new CancellationTokenSource();SetBusy(true);bar.Value=0;Append("Предварительный поиск страниц описи.");
            var worker=new Thread(()=>{try{var result=AuditEngine.Run(selected,reference,dest,(p,s)=>Ui(()=>{bar.Value=Math.Min(100,p);Append(s);}),token.Token,workers,manual,BeforeAudit,fast);var finished=DateTime.Now;Ui(()=>{report=result.Report;lastResult=result;completedAt=finished;lastRegistry=reference;Append("Отчёт: "+report);inventoryStatus.Text="Страниц описи в отчёте: "+result.InventoryPages;counts.Text="Фото: "+result.Images+"; пунктов: "+result.Findings.Count;SetBusy(false);token.Dispose();token=null;FillSummary(true);});}
                catch(OperationCanceledException){Ui(()=>{Append("Проверка отменена. Частичные данные сохранены в папке запуска.");SetBusy(false);token.Dispose();token=null;});}
                catch(Exception ex){ex=ParallelWork.StorageError(ex,dest);string errorLog=AuditProgram.LogError(ex,dest);Ui(()=>{Append("Ошибка: "+ex.Message+"\nЖурнал: "+errorLog);SetBusy(false);token.Dispose();token=null;MessageBox.Show(this,ex.Message,"Не удалось выполнить проверку",MessageBoxButtons.OK,MessageBoxIcon.Error);});}});worker.IsBackground=true;worker.SetApartmentState(ApartmentState.STA);worker.Start();
        }
    }
    public sealed class InventoryPreviewForm:Form {
        readonly InventoryPreview preview;
        bool finished;
        readonly DataGridView pages=new DataGridView{Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,RowHeadersVisible=false,BackgroundColor=Color.White,SelectionMode=DataGridViewSelectionMode.FullRowSelect};
        readonly Label count=new Label{Dock=DockStyle.Fill};readonly PictureBox image=new PictureBox{Dock=DockStyle.Fill,SizeMode=PictureBoxSizeMode.Zoom,BackColor=Color.FromArgb(229,234,239)};
        public InventoryPreviewForm(InventoryPreview preview){
            this.preview=preview;
            Icon=Icon.ExtractAssociatedIcon(typeof(AuditForm).Assembly.Location);
            Text="Выбрать страницы описи";Font=new Font("Segoe UI",10);ClientSize=new Size(1050,700);MinimumSize=new Size(930,620);MaximizeBox=true;MinimizeBox=false;StartPosition=FormStartPosition.CenterParent;Padding=new Padding(20);
            var grid=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=5};foreach(float h in new[]{38f,55f,42f,0f,46f})grid.RowStyles.Add(new RowStyle(h==0?SizeType.Percent:SizeType.Absolute,h==0?100:h));Controls.Add(grid);
            count.Font=new Font("Segoe UI",16,FontStyle.Bold);grid.Controls.Add(count,0,0);
            grid.Controls.Add(new Label{Text="Отметьте галочками фотографии и отдельные страницы PDF, которые являются описью. Автоматически найденные страницы уже отмечены. Пока это окно открыто, программа ждёт; повторный поиск не выполняется.",Dock=DockStyle.Fill},0,1);
            var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,WrapContents=false};AddAction(actions,"Автовыбор",120,p=>p.Automatic||p.Manual);AddAction(actions,"Отметить все",145,p=>true);AddAction(actions,"Снять все",120,p=>false);AddRowsAction(actions,"Отметить выделенные",205,true);AddRowsAction(actions,"Снять с выделенных",195,false);grid.Controls.Add(actions,0,2);
            pages.Columns.Add(new DataGridViewCheckBoxColumn{HeaderText="Опись",Width=60,SortMode=DataGridViewColumnSortMode.NotSortable});pages.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="№",Width=48,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});pages.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Найдена",Width=95,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});pages.Columns.Add(new DataGridViewTextBoxColumn{HeaderText="Файл / страница PDF",AutoSizeMode=DataGridViewAutoSizeColumnMode.Fill,ReadOnly=true,SortMode=DataGridViewColumnSortMode.NotSortable});
            foreach(var p in preview.Items){int row=pages.Rows.Add(p.Selected,p.Id,p.Manual?"Вручную":p.Automatic?"Авто":"",p.Original);pages.Rows[row].Tag=p;}
            pages.CurrentCellDirtyStateChanged+=(s,e)=>{if(pages.IsCurrentCellDirty)pages.CommitEdit(DataGridViewDataErrorContexts.Commit);};pages.CellValueChanged+=(s,e)=>Sync();pages.SelectionChanged+=(s,e)=>ShowPage();
            var body=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1};body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,60));body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,40));body.Controls.Add(pages,0,0);body.Controls.Add(image,1,0);grid.Controls.Add(body,0,3);
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false};var start=new Button{Text="Начать проверку",Width=180,Height=34};start.Click+=(s,e)=>Finish(DialogResult.OK);var later=new Button{Text="Продолжить позже",Width=190,Height=34};later.Click+=(s,e)=>Finish(DialogResult.Retry);var stop=new Button{Text="Отменить запуск",Width=165,Height=34};stop.Click+=(s,e)=>Finish(DialogResult.Cancel);buttons.Controls.Add(start);buttons.Controls.Add(later);buttons.Controls.Add(stop);grid.Controls.Add(buttons,0,4);AcceptButton=start;CancelButton=later;
            FormClosing+=(s,e)=>{Sync();if(!finished)DialogResult=DialogResult.Retry;};Shown+=(s,e)=>ShowPage();Sync();
        }
        void AddAction(FlowLayoutPanel host,string text,int width,Func<InventoryPreviewPage,bool> select){var b=new Button{Text=text,Width=width,Height=31};b.Click+=(s,e)=>{pages.EndEdit();foreach(DataGridViewRow row in pages.Rows)row.Cells[0].Value=select((InventoryPreviewPage)row.Tag);Sync();};host.Controls.Add(b);}
        void AddRowsAction(FlowLayoutPanel host,string text,int width,bool selected){var b=new Button{Text=text,Width=width,Height=31};b.Click+=(s,e)=>{pages.EndEdit();foreach(DataGridViewRow row in pages.SelectedRows)row.Cells[0].Value=selected;Sync();};host.Controls.Add(b);}
        void Sync(){foreach(DataGridViewRow row in pages.Rows)((InventoryPreviewPage)row.Tag).Selected=Convert.ToBoolean(row.Cells[0].Value??false);preview.Refresh();count.Text="Выбрано страниц описи: "+preview.Pages+" из "+preview.Images;}
        void Finish(DialogResult result){pages.EndEdit();Sync();finished=true;DialogResult=result;Close();}
        void ShowPage(){if(pages.CurrentRow==null)return;var p=pages.CurrentRow.Tag as InventoryPreviewPage;Image next=null;try{if(p!=null&&File.Exists(p.Image))using(var loaded=Image.FromFile(p.Image))next=new Bitmap(loaded);}catch{}var old=image.Image;image.Image=next;if(old!=null)old.Dispose();}
        protected override void Dispose(bool disposing){if(disposing&&image.Image!=null){var old=image.Image;image.Image=null;old.Dispose();}base.Dispose(disposing);}
    }
}
