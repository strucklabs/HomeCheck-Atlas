using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Drawing;
using System.Windows.Forms;
class IconTests {
 static int checks;
 static void Check(bool value,string text){if(!value)throw new Exception(text);checks++;Console.WriteLine("OK "+text);}
 static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(target);}
 static void Call(object target,string name,params object[] args){target.GetType().GetMethod(name,BindingFlags.NonPublic|BindingFlags.Instance).Invoke(target,args);}
 [STAThread]static void Main(){
 Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);Application.EnableVisualStyles();
 var folder=Path.Combine(Path.GetTempPath(),"HomeCheck-Icons-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
 var prefs=new IconPreferences(Path.Combine(folder,"icons.json"));
 var d=new Device("Desktop"){Mac="AA:BB:CC:DD:EE:01",IP="192.0.2.3"};
 prefs.Save(d,"printer");d.IP="192.0.2.88";d.Name="Neuer Name";
 Check(new IconPreferences(Path.Combine(folder,"icons.json")).Resolve(d)=="printer","Selection survives DHCP, rename and reload");
 var other=new Device("Other"){Mac="AA:BB:CC:DD:EE:02"};var second=new IconPreferences(Path.Combine(folder,"icons.json"));second.Save(other,"tablet");prefs.Save(d,"laptop");
 Check(new IconPreferences(Path.Combine(folder,"icons.json")).Resolve(other)=="tablet","Other instance changes preserved");
 prefs.Save(d,null);Check(prefs.Choice(d)==null,"Automatic reset persisted");
 Check(DeviceIcons.Identity(new Device("Thermostat"){Ain="123 456"})=="ain:123456","Smart-home stable ID");
 bool failed=false;try{prefs.Save(new Device("No ID"),"tablet");}catch(IOException){failed=true;}Check(failed,"No IP-only persistence");
 Check(DeviceIcons.Automatic(new Device("Mini-PC"){Kind="computer"})=="desktop-tower-monitor","Mini-PC is not a speaker");
 using(var bmp=new Bitmap(112,112))using(var g=Graphics.FromImage(bmp)){
 foreach(var icon in DeviceIcons.All){g.Clear(Color.Transparent);DeviceIcons.Draw(g,icon.Id,new RectangleF(0,0,112,112),Color.Teal);int count=0;
 for(int y=0;y<112;y++)for(int x=0;x<112;x++)if(bmp.GetPixel(x,y).A>0)count++;
 Check(count>150&&count<10000,"Embedded glyph "+icon.Id);}}
 var inv=new Inventory();var router=new Device("Router"){Mac="AA:BB:CC:DD:EE:10",Role="master",Linked=true};
 inv.Devices.Add(router);int i=0;foreach(var icon in DeviceIcons.All.Take(10)){var node=new Device(icon.Name){Mac="AA:BB:CC:DD:EF:"+(i++).ToString("X2"),Linked=true,HostStatus=i==5?"inaktiv":"aktiv"};inv.Devices.Add(node);prefs.Save(node,icon.Id);inv.Links.Add(new Link{A=router,B=node,Type=i%2==0?"LAN":"WLAN",Rx=30});}
 using(var form=new Atlas()){
 var map=(Chart)Field(form,"map");var list=(ListBox)Field(form,"list");
 typeof(Atlas).GetField("iconPreferences",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(form,prefs);
 map.SetData(inv);foreach(var node in inv.Devices)list.Items.Add(node);list.SelectedItem=router;
 form.Show();Application.DoEvents();map.SetData(inv);map.SelectDevice(router);Call(form,"UpdateSummary",router);
 Check(((Button)Field(form,"iconButton")).Enabled,"Icon action enabled for stable identity");
 using(var shot=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(shot,form.ClientRectangle);shot.Save("MapPreview.png");}
 form.Hide();}
 using(var picker=new IconPicker(d,"printer")){
 picker.Show();Application.DoEvents();
 using(var shot=new Bitmap(picker.Width,picker.Height)){picker.DrawToBitmap(shot,new Rectangle(0,0,picker.Width,picker.Height));shot.Save("PickerPreview.png");}
 var grid=(ListView)Field(picker,"grid");Check(grid.Items.Count==36,"All 36 choices visible");picker.Hide();}
 Console.WriteLine(checks+" checks passed");
 }
}
