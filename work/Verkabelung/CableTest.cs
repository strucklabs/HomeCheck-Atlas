using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows.Forms;
class CableTest {
 static void Check(bool pass,string what){if(!pass)throw new Exception(what);Console.WriteLine(what);}
 [STAThread]static void Main(){
 var raw=new Inventory();var root=new Device("Router"){MeshId="root",Role="master"};var ap=new Device("Repeater"){MeshId="ap"};
 var hub=new Device("Mein Hub"){MeshId="hub",IP="10.22.4.11"};var tv=new Device("TV"){MeshId="tv"};raw.Devices.AddRange(new[]{root,ap,hub,tv});
 foreach(var node in new[]{ap,hub,tv})raw.Links.Add(new Link{A=node==ap?root:ap,B=node,Type="LAN"});
 const string sw="manual:s1";var conf=new CableSettings();conf.Names[sw]="Switch";conf.Parents[sw]="ap";conf.Parents["hub"]=sw;conf.Parents["tv"]=sw;conf.Validate(raw);
 var view=conf.View(raw);var path=RoutePlan.Build(view,hub);
 Check(path.Hops.Select(h=>h.Device.Key).SequenceEqual(new[]{"root","ap",sw,"hub"}) && path.Source.Contains("manuell"),"Manueller Pfad bis zum Hub");
 Check(path.Hops[2].Host==null && path.Hops[2].Skip=="ohne Ping-Ziel","Zwischenstation ohne Messziel");
 Check(RoutePlan.Build(view,tv).Hops[2].Device.Key==sw,"Mehrere Endgeräte");
 Check(raw.Links.Count==3 && raw.Devices.Count==4,"Erfassung bleibt separat");
 conf.Parents[sw]="root";hub.IP="10.22.4.96";conf.Validate(raw);
 Check(RoutePlan.Build(conf.View(raw),hub).Hops.Select(h=>h.Device.Key).SequenceEqual(new[]{"root",sw,"hub"}),"Umstecken und DHCP-Wechsel");
 var json=new JavaScriptSerializer();var saved=json.Serialize(conf);Check(!saved.Contains("10.22")&&json.Deserialize<CableSettings>(saved).View(raw).Links.Count==4,"Persistenz ohne IP-Bindung");
 conf.Parents.Remove("hub");Check(RoutePlan.Build(conf.View(raw),hub).Hops[1].Device==ap,"Automatische Verbindung wiederhergestellt");
 conf.Parents[sw]="tv";bool refused=false;try{conf.Validate(raw);}catch(IOException){refused=true;}Check(refused,"Manueller Kreis abgewiesen");
 conf.Parents.Clear();conf.Parents["ap"]="hub";refused=false;try{conf.Validate(raw);}catch(IOException){refused=true;}Check(refused||!conf.View(raw).Links.Any(e=>e.A==ap&&e.B==hub),"Keine doppelten Gegenkanten");
 hub.ReportModel("Mesh","Modell A");hub.ReportModel("UPnP","Modell B");Check(hub.Name=="Mein Hub"&&hub.ModelText().Contains("Modell A")&&hub.ModelText().Contains("Modell B"),"Name und widersprechende Modellangaben");
 using(var f=new Atlas()){Check(f.Controls.Count>0,"Windows-Oberfläche initialisiert");}
 }
}