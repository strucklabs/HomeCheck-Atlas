using System;
using System.Linq;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
class RouteDiagramTest {
 static int checks;
 static void Check(bool ok,string msg){if(!ok)throw new Exception(msg);Console.WriteLine("OK "+msg);checks++;}
 static Control Find(Control c,Type type){if(c.GetType()==type)return c;foreach(Control child in c.Controls){var found=Find(child,type);if(found!=null)return found;}return null;}
 [STAThread]static void Main(){
 Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();
 var inv=new Inventory();var root=new Device("FRITZ!Box – Test"){Role="master",Mac="00:11:22:33:44:01",IP="192.0.2.1"};
 var repeater=new Device("Repeater – Arbeitszimmer"){Role="slave",Mac="00:11:22:33:44:02",IP="192.0.2.2"};
 var sw=new Device("Switch ohne Ping-Ziel"){Kind="switch",MeshId="manual:test"};
 var target=new Device("Lautsprecher mit eigenem Symbol"){Mac="00:11:22:33:44:03",IP="192.0.2.3"};
 inv.Devices.AddRange(new[]{root,repeater,sw,target});
 inv.Links.Add(new Link{A=root,B=repeater,Type="WLAN"});inv.Links.Add(new Link{A=repeater,B=sw,Type="LAN",Manual=true});inv.Links.Add(new Link{A=sw,B=target,Type="LAN",Manual=true});
 var plan=RoutePlan.Build(inv,target);
 using(var graph=new RouteDiagram(plan,inv,d=>d==target?"speaker-wireless":DeviceIcons.Automatic(d))){
 Check(graph.Connection(0)=="WLAN","WLAN connection");Check(graph.Connection(1)=="LAN · manuell","Manual cable marked");
 var missing=new Inventory();using(var unknown=new RouteDiagram(plan,missing,DeviceIcons.Automatic))Check(unknown.Connection(0)=="ungeklärt","Unknown edge is not invented");
 }
 using(var atlas=new Atlas()){
 var map=(Chart)typeof(Atlas).GetField("map",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(atlas);map.SetData(inv);
 Exception failure=null;
 using(var timer=new Timer{Interval=300}){
 timer.Tick+=(o,e)=>{var form=Application.OpenForms.Cast<Form>().FirstOrDefault(f=>f!=atlas&&f.Text.StartsWith("Streckentest"));if(form==null)return;timer.Stop();
 try{var diagram=(RouteDiagram)Find(form,typeof(RouteDiagram));Check(diagram!=null&&diagram.Height>=140,"Graph visible in actual route dialog");
 Check(Find(form,typeof(RoutePlot)).Height>=120,"Ping chart retains space");
 using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height));image.Save("RoutePreview.png");}
 form.Size=new Size(650,560);Application.DoEvents();Check(diagram.HorizontalScroll.Visible,"Narrow window scrolls horizontally");
 using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(0,0,form.Width,form.Height));image.Save("RouteNarrowPreview.png");}
 }catch(Exception ex){failure=ex;}finally{form.Close();}};
 timer.Start();typeof(Atlas).GetMethod("ShowRoute",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(atlas,new object[]{target});if(failure!=null)throw failure;
 }
 }
 Console.WriteLine(checks+" route checks passed; no network requests");
 }
}
