using System;using System.Drawing;using System.Reflection;using System.Windows.Forms;using System.Linq;
class TestCenter {
static BindingFlags flags=BindingFlags.NonPublic|BindingFlags.Instance;
[STAThread]static void Main(){using(var f=new Atlas()){
 f.Show();Application.DoEvents();var type=typeof(Atlas);var m=(Chart)type.GetField("map",flags).GetValue(f);var msg=(Label)type.GetField("status",flags).GetValue(f);var button=(Button)type.GetField("scan",flags).GetValue(f);
 var inv=new Inventory();var root=new Device("Router"){Role="master"};inv.Devices.Add(root);for(int i=0;i<22;i++){var d=new Device("Device "+i);inv.Devices.Add(d);if(i<9)inv.Links.Add(new Link{A=root,B=d,Type="WLAN",Rx=22});}
 m.SetData(inv);foreach(int width in new[]{1280,850}){f.Width=width;Application.DoEvents();
 if(msg.PointToScreen(Point.Empty).Y<button.PointToScreen(new Point(0,button.Height)).Y)throw new Exception("Header");
 var c=typeof(Chart);float z=(float)c.GetField("zoom",flags).GetValue(m),x=(float)c.GetProperty("OffsetX",flags).GetValue(m,null),y=(float)c.GetProperty("OffsetY",flags).GetValue(m,null);
 float r=Link.DistanceForSnr(-30)*(float)c.GetField("layoutScale",flags).GetValue(m);
 foreach(var d in inv.Devices.Take(10))if(x+(d.Pos.X-r)*z<0||x+(d.Pos.X+r)*z>m.ClientSize.Width||y+(d.Pos.Y-r)*z<0||y+(d.Pos.Y+r)*z>m.ClientSize.Height)throw new Exception("Ring");
 if(Math.Abs(inv.Devices.Skip(10).First().Pos.X+inv.Devices.Skip(14).First().Pos.X-inv.Devices[20].Pos.X-inv.Devices[22].Pos.X)>0.1)throw new Exception("Raster");
 if(m.VerticalScroll.Visible||m.HorizontalScroll.Visible)throw new Exception("Scroll "+width+" "+m.ClientSize+" "+m.AutoScrollMinSize+" "+m.VerticalScroll.Visible+" "+m.HorizontalScroll.Visible);
 }Console.WriteLine("OK");}}}