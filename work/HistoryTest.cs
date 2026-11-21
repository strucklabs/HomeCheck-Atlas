using System;using System.IO;using System.Linq;using System.Drawing;using System.Reflection;using System.Windows.Forms;using System.Collections.Generic;
class HistoryTest {
static void Check(bool v,string s){if(!v)throw new Exception(s);}
[STAThread]static void Main(string[] args){
var inv=new Inventory();inv.Mesh(File.ReadAllText(args[0]));var d=inv.Devices[2];inv.Host(d.Mac,"","fallback");Check(d.Name=="Nest Mini","friendly");inv.Host(inv.Devices[1].Mac,"","Repeater");Check(inv.Devices[1].Name=="Repeater","host");
var f=Frame.Capture(inv);var o=f.Devices[d.Key];Check(o.Local[0].Up==24&&o.Local[0].Down==31&&o.Upstream[0].Up==12&&o.Upstream[0].Down==null,"SNR direction");Check(o.Local[0].Radio=="2,4 GHz · Kanal 6","radio");
inv.Links[1].A=inv.Devices[0];var moved=Frame.Capture(inv);Check(o.Local[0].Id!=moved.Devices[d.Key].Local[0].Id&&moved.Devices[d.Key].Upstream.Count==0,"roam");
using(var a=new Atlas()){
var flags=BindingFlags.Instance|BindingFlags.NonPublic;var t=typeof(Atlas);t.GetField("archive",flags).SetValue(a,args[1]);var save=t.GetMethod("Save",flags);
foreach(var v in new[]{f,moved,new Frame{Kind="gap",Message="Routerabfrage fehlgeschlagen"},new Frame{Kind="mark",Marker="m",Message="Störung"},new Frame{Kind="note",Marker="m",Message="Testnotiz"}})Check((bool)save.Invoke(a,new object[]{v}),"save");
t.GetMethod("LoadDay",flags).Invoke(a,null);var plot=(HistoryPlot)t.GetField("plot",flags).GetValue(a);Check(plot.Frames.Count==5,"reload");var ev=(ListBox)t.GetField("eventsList",flags).GetValue(a);Check(ev.Items.Cast<object>().Any(x=>x.ToString().Contains("Testnotiz")),"note");plot.Key=d.Key;plot.Dock=DockStyle.None;plot.Size=new Size(420,230);using(var bmp=new Bitmap(420,230))plot.DrawToBitmap(bmp,plot.ClientRectangle);
}Console.WriteLine("OK");}}
