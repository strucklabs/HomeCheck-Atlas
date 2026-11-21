using System;
class DhcpTest {
 static void Check(bool pass){if(!pass)throw new Exception("DHCP regression");}
 static string Row(string mac,string ip,string active){return "<Item><MACAddress>"+mac+"</MACAddress><IPAddress>"+ip+"</IPAddress><Active>"+active+"</Active></Item>";}
 static Inventory Hosts(string rows){var inv=new Inventory();inv.HostList("<List>"+rows+"</List>");return inv;}
 static void Main(){const string a="00:11:22:33:44:55", b="00:11:22:33:44:66";
 var old=Hosts(Row(a,"10.7.1.66","1"));var device=old.ByMac(a);
 Check(Reader.Address(Hosts(Row(b,"10.7.1.66","1")+Row(a,"10.7.1.89","1")),device)=="10.7.1.89");
 Check(Reader.Address(Hosts(Row(b,"10.7.1.66","1")),device)==null);
 Check(Reader.Address(Hosts(Row(a,"10.7.1.66","0")),device)==null);
 Check(Reader.Address(Hosts(Row(a,"10.7.1.66","1")+Row(b,"10.7.1.66","1")),device)==null);
 Check(!new EchoPair{TargetIp="10.7.1.66"}.Matches(a));
 Check(new EchoPair{TargetKey=a,TargetIp="10.7.1.89"}.Matches(a));
 device.IP="192.168.178.66";Check(RoutePlan.Build(old,device).Hops.Count==1);
 Console.WriteLine("7 DHCP checks passed");}
}