using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using PhotoAudit;

class InventorySelectionTest {
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static OcrPage Ocr(string text){return new OcrPage{width=800,height=1100,text=text,lines=new List<Line>{new Line{text=text,words=new List<Word>()}}};}
    static IEnumerable<Control> Controls(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var child in Controls(c))yield return child;}}
    static object Field(object obj,string name){return obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(obj);}
    static void Call(object obj,string name,params object[] args){obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);}
    [STAThread] static int Main(string[] args){
        Application.EnableVisualStyles();string root=Path.Combine(Path.GetTempPath(),"inventory-selection-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);string image=Path.Combine(root,"page.jpg");
        using(var bmp=new Bitmap(800,1100)){using(var g=Graphics.FromImage(bmp)){g.Clear(Color.White);g.DrawString("Synthetic page",SystemFonts.DefaultFont,Brushes.Black,40,40);}bmp.Save(image);}
        var photos=new List<Photo>{new Photo{Id=1,Original="synthetic.pdf / стр. 1",Image=image,Ocr=Ocr("Опись\nШифр тома\nНаименование\n123-45-6789-ТКР12.1")},new Photo{Id=2,Original="synthetic.pdf / стр. 2",Image=image,Ocr=Ocr("Информационно-удостоверяющий лист\nОбозначение документа: ABC-12")},new Photo{Id=3,Original="synthetic.jpg",Image=image,Ocr=Ocr("Не прочитан заголовок")}};
        var preview=AuditEngine.PreviewInventory(photos);Check(preview.Pages==1&&preview.Items.Count==3&&preview.Items[0].Selected,"Automatic inventory and source page list");
        photos[0].KindHint="Опись";preview.Items[0].Selected=false;preview.Items[2].Selected=true;preview.Apply(photos);
        var result=AuditEngine.Analyze(photos,null);Check(photos[0].Kind!="Опись"&&photos[0].InventoryOverride==false,"Removing an automatic mark must override both OCR and old kind hint");Check(photos[2].Kind=="Опись"&&photos[2].ManualInventory&&photos[2].InventoryOverride==true&&result.InventoryPages==1,"Explicit page must remain inventory after analysis");
        preview.Items[0].Selected=true;preview.Items[2].Selected=false;preview.Apply(photos);AuditEngine.Analyze(photos,null);Check(photos[0].Kind=="Опись"&&photos[2].Kind!="Опись","Selection can be changed twice");
        using(var gate=new InventoryReviewGate())using(var cancel=new CancellationTokenSource()){
            cancel.Cancel();bool cancelled=false;try{gate.Wait(cancel.Token);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"A paused review must be cancellable");
        }
        preview=AuditEngine.PreviewInventory(photos);Exception failure=null;bool? accepted=null;int phase=0,callbacks=0;Thread worker=null;
        using(var owner=new AuditForm())using(var token=new CancellationTokenSource())using(var timer=new System.Windows.Forms.Timer{Interval=80}){
            owner.Opacity=0;owner.Show();typeof(AuditForm).GetField("token",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(owner,token);Call(owner,"SetBusy",true);
            timer.Tick+=(s,e)=>{var dialog=Application.OpenForms.Cast<Form>().OfType<InventoryPreviewForm>().FirstOrDefault();try{
                if(phase==0&&dialog!=null){phase=1;var grid=Controls(dialog).OfType<DataGridView>().Single();grid.Rows[2].Cells[0].Value=true;Controls(dialog).OfType<Button>().Single(b=>b.Text=="Продолжить позже").PerformClick();}
                else if(phase==1&&dialog==null){phase=2;Check(worker.IsAlive&&accepted==null&&callbacks==1,"Pause must keep the same worker and preliminary data");Check(ReferenceEquals(Field(owner,"pendingInventory"),preview)&&preview.Items[2].Selected,"Paused selection preserved");Check(((Button)Field(owner,"run")).Enabled&&((Button)Field(owner,"run")).Text=="Продолжить…","Resume action offered without a fresh run");Call(owner,"Start");}
                else if(phase==2&&dialog!=null){phase=3;var grid=Controls(dialog).OfType<DataGridView>().Single();Check(Convert.ToBoolean(grid.Rows[2].Cells[0].Value),"Selection restored when reopening review");grid.Rows[0].Cells[0].Value=false;if(args.Length>0){dialog.PerformLayout();using(var shot=new Bitmap(dialog.Width,dialog.Height)){dialog.DrawToBitmap(shot,new Rectangle(0,0,dialog.Width,dialog.Height));shot.Save(args[0]);}}Controls(dialog).OfType<Button>().Single(b=>b.Text=="Начать проверку").PerformClick();}
                else if(phase==3&&accepted.HasValue){phase=4;timer.Stop();}
            }catch(Exception ex){failure=ex;token.Cancel();if(dialog!=null)dialog.Close();timer.Stop();}};
            worker=new Thread(()=>{try{callbacks++;accepted=(bool)typeof(AuditForm).GetMethod("BeforeAudit",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(owner,new object[]{preview});}catch(Exception ex){failure=ex;}});worker.IsBackground=true;worker.Start();timer.Start();var timeout=System.Diagnostics.Stopwatch.StartNew();
            while(phase<4&&failure==null&&timeout.ElapsedMilliseconds<15000){Application.DoEvents();Thread.Sleep(15);}token.Cancel();worker.Join(3000);Call(owner,"SetBusy",false);owner.Close();
        }
        if(failure!=null)throw failure;Check(phase==4&&accepted==true&&callbacks==1,"Continue exactly one pending preliminary review");Check(!preview.Items[0].Selected&&preview.Items[2].Selected,"Final page marks returned to the original run");
        Console.WriteLine("PASS: PDF page selection, automatic mark removal, persistent pause/resume, cached preliminary data, cancellation");return 0;
    }
}
