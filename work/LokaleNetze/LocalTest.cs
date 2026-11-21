using System;
using System.Linq;
class LocalTest{
 static void Check(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine(label);}
 static PcAddress N(int id,string ip,string mac){return new PcAddress{Id=id,IPAddress=ip,LinkLayerAddress=mac,State="Stale"};}
 [STAThread]static void Main(){
 var snap=new LocalSnapshot{Adapters=new[]{new PcAdapter{Id=7,Virtual=true,Name="Beliebiger Name"},new PcAdapter{Id=9,Physical=true,Name="LAN"}},Own=new[]{N(7,"10.7.8.1","")},Neighbors=new[]{N(7,"10.7.8.254","00-11-22-33-44-55"),N(9,"10.22.1.90","00-11-22-33-44-56"),N(7,"10.7.8.1","00-11-22-33-44-57"),N(7,"224.0.0.1","01-00-5E-00-00-01")}};
 var inv=new Inventory();snap.ImportHomeNeighbors(inv);Check(inv.Devices.Count==1 && inv.Devices[0].IP=="10.22.1.90","Physical LAN only in map");
 Check(snap.UnicastNeighbors().Count()==2,"Self/multicast omitted from neighbors");
 var live=LocalSnapshot.Read();var actual=new Inventory();live.ImportHomeNeighbors(actual);
 foreach(var ip in new[]{"192.168.116.254","192.168.152.254"}){var v=live.UnicastNeighbors().FirstOrDefault(x=>x.IPAddress==ip);
 if(v!=null){Check(live.Adapter(v.Id).Virtual && !actual.Devices.Any(x=>x.IP==ip),ip+" on "+live.Adapter(v.Id).Name+" only");}else Console.WriteLine(ip+" not currently cached");}
 Check(live.Own.Length>0 && live.Own.Any(x=>x.IPAddress.Contains(":")),"Own IPv4/IPv6 snapshot");
 using(var form=new Atlas()){var m=typeof(Atlas).GetMethod("ShowLocal",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);m.Invoke(form,new object[]{live});Console.WriteLine("UI populated");}
 }
}