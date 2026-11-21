using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Xml.Linq;

class IncidentReading {
    public string Phase,Short,Report,Timestamp,Ap,ApName;
    public Frame Mesh;
    public List<string> Stations;
    public static string Parent(Frame f,string key){
        Observation o;if(f==null||f.Devices==null||!f.Devices.TryGetValue(key,out o))return "";
        var ids=o.Local.Select(s=>s.Id.Split('|')).Where(p=>p.Length>=2).Select(p=>p[1]).Distinct().ToArray();
        return ids.Length==1?ids[0]:"";
    }
    public static string State(Frame f,string key){
        Observation o;return f.Devices.TryGetValue(key,out o)?o.State:"im Mesh nicht gemeldet";
    }
    public static string Compare(Frame first,Frame next,string ap,string key){
        if(first==null||next==null||ap=="")return "Kein Vergleich der WLAN-Geräte möglich.";
        var before=first.Devices.Keys.Where(k=>k!=key&&Parent(first,k)==ap).ToArray();
        var after=next.Devices.Keys.Where(k=>k!=key&&Parent(next,k)==ap).ToArray();
        var gone=before.Except(after).Select(k=>first.Devices[k].Name).ToArray();
        var added=after.Except(before).Select(k=>next.Devices[k].Name).ToArray();
        return "Weitere WLAN-Geräte am Zugangspunkt: "+before.Intersect(after).Count()+" weiterhin zugeordnet"+
            (gone.Length>0?" · nicht mehr dort: "+string.Join(", ",gone):"")+
            (added.Length>0?" · jetzt dort: "+string.Join(", ",added):"")+".";
    }
}
partial class Reader {
    public void ReadRouterHosts(Inventory inv){
        var desc=XDocument.Parse(Request("/tr64desc.xml"));
        var hosts=desc.Descendants().First(x=>x.Name.LocalName=="service"&&Field(x,"serviceType").Contains(":Hosts:"));
        var reply=XDocument.Parse(Request(Field(hosts,"controlURL"),"X_AVM-DE_GetHostListPath",Field(hosts,"serviceType")));
        string path=reply.Descendants().First(x=>x.Name.LocalName=="NewX_AVM-DE_HostListPath").Value;
        inv.HostList(Request(path));
    }
    public List<string> StationSnapshot(){
        var desc=Description();string model=desc.Descendants().First(x=>x.Name.LocalName=="modelName").Value;
        if(model.IndexOf("Repeater",StringComparison.OrdinalIgnoreCase)<0)throw new IOException("Gerätetyp nicht als Repeater bestätigt");
        var services=desc.Descendants().Where(x=>x.Name.LocalName=="service"&&Field(x,"serviceType").Contains(":WLANConfiguration:")).ToArray();
        if(services.Length==0)throw new IOException("Kein WLAN-Dienst gemeldet");
        var seen=new Inventory();var ap=new Device(model);
        foreach(var svc in services)Stations(seen,ap,svc,Field(svc,"serviceType"));
        return seen.Macs.Keys.ToList();
    }
}
partial class Atlas {
    DateTime inventoryTime;Frame latestMesh;
    static readonly string[] repeaterIps={"192.168.178.23","192.168.178.25","192.168.178.54"};
    async Task CaptureIncident(Frame marker,string key,string name){
        string u=user.Text.Trim(),pw=password.Text;
        var inventory=map.Data;var access=new Dictionary<string,NetworkCredential>(deviceAccess);
        Frame scan=null;
        if(inventory.Devices.Any(d=>d.Role=="master")){
            scan=Frame.Capture(inventory);scan.Time=inventoryTime.ToUniversalTime().Ticks;
        }
        var candidates=new[]{scan,latestMesh}.Concat(plot.Frames.Where(f=>f.Kind=="sample"&&f.Time<=marker.Time))
            .Where(f=>f!=null).OrderByDescending(f=>f.Time).ToList();
        var last=candidates.FirstOrDefault(f=>IncidentReading.Parent(f,key)!="");
        var baseline=candidates.FirstOrDefault();
        string ap=IncidentReading.Parent(last,key);
        IncidentReading first=null;
        for(int i=0;i<(pw==""?1:2);i++){
            if(i==1){await Task.Delay(TimeSpan.FromMinutes(1));if(IsDisposed||Disposing)return;}
            string phase=i==0?"Beim Markieren":"Folgemessung";
            var r=await Task.Run(()=>{
                var now=new IncidentReading{Timestamp=DateTime.Now.ToString("G"),Phase=phase};
                var lines=new List<string>();Inventory fresh=null;Reader router=null;
                if(i==0&&baseline!=null&&key!="")lines.Add("Vorher ("+baseline.LocalTime.ToString("g")+") · "+name+": "+IncidentReading.State(baseline,key));
                try{
                    if(pw=="")throw new IOException("Routerkennwort fehlt");
                    var inv=new Inventory();router=new Reader("fritz.box",u,pw);router.Collect(inv,true,null,true);fresh=inv;
                    now.Mesh=Frame.Capture(inv);
                    now.Short=key==""?"Mesh: "+inv.Devices.Count+" Geräte":name+": "+IncidentReading.State(now.Mesh,key);
                    if(ap=="")ap=IncidentReading.Parent(now.Mesh,key);
                }catch(Exception ex){now.Short="Routerabfrage fehlgeschlagen · "+Reader.Reason(ex);}
                lines.Add(now.Short);
                now.Ap=ap;var d=inventory.Devices.FirstOrDefault(n=>n.Key==ap&&ap!="");
                if((d==null||String.IsNullOrEmpty(d.IP))&&ap!=""&&fresh!=null){
                    try{router.ReadRouterHosts(fresh);d=fresh.Devices.FirstOrDefault(n=>n.Key==ap);}
                    catch(Exception ex){lines.Add("IP-Zuordnung am Router fehlgeschlagen · "+Reader.Reason(ex));}
                }
                Observation known;
                now.ApName=d!=null?d.Name:last!=null&&last.Devices.TryGetValue(ap,out known)?known.Name:
                    now.Mesh!=null&&now.Mesh.Devices.TryGetValue(ap,out known)?known.Name:ap;
                if(ap!=""&&now.Mesh!=null){
                    lines.Add("Zugangspunkt "+now.ApName+" · "+IncidentReading.State(now.Mesh,ap));
                    lines.Add(IncidentReading.Compare(i==0?baseline:(first.Mesh??baseline),now.Mesh,ap,key));
                }
                NetworkCredential cred;
                if(key=="")lines.Add("Gesamtes Mesh erfasst. Für einen Stationsvergleich ein WLAN-Gerät auswählen.");
                else if(d==null||!repeaterIps.Contains(d.IP))lines.Add("Stationsliste: kein bekannter Repeater mit zugeordneter IP.");
                else if(!access.TryGetValue(d.IP,out cred))lines.Add(d.Name+" · Stationsliste nicht abgefragt (kein eigener Zugang übernommen).");
                else try{
                    now.Stations=new Reader(d.IP,cred.UserName,cred.Password).StationSnapshot();
                    var aliases=inventory.Macs.Where(v=>v.Value.Key==key).Select(v=>v.Key).Concat(new[]{key});
                    string state=now.Stations.Intersect(aliases,StringComparer.OrdinalIgnoreCase).Any()?"als WLAN-Station gemeldet":"in der Stationsliste nicht gemeldet";
                    lines.Add(d.Name+" · "+name+" "+state+" · "+now.Stations.Count+" Stationen.");
                    now.Short+=" · "+d.Name+": "+state;
                    if(first!=null&&first.Stations!=null){
                        var old=first.Stations.Except(aliases,StringComparer.OrdinalIgnoreCase);
                        var current=now.Stations.Except(aliases,StringComparer.OrdinalIgnoreCase);
                        lines.Add("Weitere Stationen laut Repeater: "+old.Intersect(current).Count()+" weiterhin da, "+old.Except(current).Count()+" fehlen, "+current.Except(old).Count()+" hinzugekommen.");
                    }
                }catch(Exception ex){lines.Add(d.Name+" · Stationsabfrage fehlgeschlagen · "+Reader.Reason(ex));now.Short+=" · Stationsabfrage fehlgeschlagen";}
                now.Report=string.Join(Environment.NewLine,lines);return now;
            });
            if(IsDisposed||Disposing)return;
            if(!Save(new Frame{Kind="incident",Time=marker.Time,Marker=marker.Marker,Incident=r}))return;
            if(i==0)first=r;
        }
    }
    void ShowIncident(){
        var line=eventsList.SelectedItem as EventLine;
        if(line==null||line.F.Kind!="mark"){monitorState.Text="Bitte eine Störungsmarke auswählen";return;}
        var records=plot.Frames.Where(f=>f.Kind=="incident"&&f.Marker==line.F.Marker&&f.Incident!=null).Select(f=>f.Incident).ToList();
        using(var dialog=new Form{Text=line.F.Message+" · "+line.F.LocalTime.ToString("g"),Width=715,Height=385,StartPosition=FormStartPosition.CenterParent}){
            var text=new TextBox{Dock=DockStyle.Fill,Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical};
            text.Text=records.Count==0?"Noch keine Momentaufnahme zu dieser Marke gespeichert.":string.Join(Environment.NewLine+Environment.NewLine,records.Select(r=>r.Phase+" · "+r.Timestamp+Environment.NewLine+r.Report));
            dialog.Controls.Add(text);dialog.ShowDialog(this);
        }
    }
}
