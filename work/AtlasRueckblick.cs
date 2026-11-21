using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;

class RadioSample {
    public string Label;
    public int? Percent;
    public string Format(){return (String.IsNullOrWhiteSpace(Label)?"WLAN":Label)+" · "+(Percent.HasValue?Percent+" %":"nicht gemeldet");}
}
partial class Atlas {
    class RadioRef {public string Key="",Port="";}
    static string ChannelText(Frame f,string key,ref RadioRef radio){
        if(f==null||f.Devices==null)return "Keine Mesh-Messung";
        Observation device,ap;bool known=f.Devices.TryGetValue(key,out device);
        if(known&&device.Local.Count>0){
            var ports=device.Local.Select(x=>(x.ParentKey??IncidentReading.Parent(f,key))+"|"+x.ParentPort).Distinct().ToArray();
            if(ports.Length==1){var s=device.Local[0];radio.Key=s.ParentKey??IncidentReading.Parent(f,key);radio.Port=s.ParentPort??"";}
            else{radio.Key="";radio.Port="";return "Mehrere WLAN-Verbindungen · "+string.Join(" / ",device.Local.Select(s=>{
                Observation parent;RadioSample reading;
                return s.ParentKey!=null&&s.ParentPort!=null&&f.Devices.TryGetValue(s.ParentKey,out parent)&&parent.Radios!=null&&parent.Radios.TryGetValue(s.ParentPort,out reading)?parent.Name+" · "+reading.Format():s.Peer+" · nicht gemeldet";
            }));}
        }
        if((!known||device.Local.Count==0)&&known&&device.Radios!=null&&device.Radios.Count>0)
            return device.Name+" · "+string.Join(" / ",device.Radios.Values.Select(r=>r.Format()));
        RadioSample value;
        if(radio.Key==""||!f.Devices.TryGetValue(radio.Key,out ap))return "Keine zuordenbare Funkmessung";
        string label=ap.Name+(known&&device.Local.Count>0?"":" (zuletzt zugeordnet)");
        return label+" · "+(ap.Radios!=null&&ap.Radios.TryGetValue(radio.Port,out value)?value.Format():"nicht gemeldet");
    }
    void ShowLoadDay(){
        var choice=devices.SelectedItem as Choice;
        if(choice==null){monitorState.Text="Bitte ein Gerät im Verlauf auswählen";return;}
        using(var f=new Form{Text="Kanalauslastung · "+choice.Name+" · "+shown.ToShortDateString(),Width=650,Height=470,StartPosition=FormStartPosition.CenterParent}){
            var table=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true};
            table.Columns.Add("Zeit",85);table.Columns.Add("Funkmessung",520);var radio=new RadioRef();
            Frame previous=null;
            foreach(var item in plot.Frames.Where(s=>s.Kind=="sample"||s.Kind=="gap").OrderBy(s=>s.Time)){
                if(previous!=null&&(item.Time-previous.Time>95*TimeSpan.TicksPerSecond||previous.Session!=item.Session))
                    table.Items.Add(new ListViewItem(new[]{previous.LocalTime.ToString("T")+" …","Beobachtung unterbrochen bis "+item.LocalTime.ToString("T")}));
                table.Items.Add(new ListViewItem(new[]{item.LocalTime.ToString("T"),item.Kind=="gap"?"Routerabfrage fehlgeschlagen":ChannelText(item,choice.Key,ref radio)}));previous=item;
            }
            f.Controls.Add(table);f.ShowDialog(this);
        }
    }
    class ReviewPoint {
        public Frame Sample;public string Device,Ap,ApState,Load;
        public string When {get{return Sample.LocalTime.ToString("dd.MM.yyyy HH:mm:ss");}}
    }
    static string Association(Frame f,string key){
        Observation o;if(!f.Devices.TryGetValue(key,out o))return "(fehlt)";
        if(o.Local.Count==0)return o.State;
        return string.Join("|",o.Local.Select(s=>s.ParentKey??s.Id.Split('|').ElementAtOrDefault(1)).Distinct().OrderBy(v=>v));
    }
    public static string ReviewArchive(string folder,Frame marker,string key,CancellationToken cancel){
        var serializer=new JavaScriptSerializer{MaxJsonLength=32000000};
        ReviewPoint prev=null,first=null;var radio=new RadioRef();string ap="",change=null,after=null;
        bool pendingAfter=false,seen=false,failed=false;int failures=0,gaps=0,bad=0,count=0;var gapTimes=new List<string>();
        var files=Directory.Exists(folder)?Directory.GetFiles(folder,"????-??-??.jsonl").OrderBy(x=>x,StringComparer.Ordinal):Enumerable.Empty<string>();
        foreach(string file in files){
            if(String.CompareOrdinal(Path.GetFileNameWithoutExtension(file),marker.LocalTime.ToString("yyyy-MM-dd"))>0)break;
            using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite))
            using(var reader=new StreamReader(stream)){
                string line;while((line=reader.ReadLine())!=null){
                    cancel.ThrowIfCancellationRequested();Frame f;
                    try{f=serializer.Deserialize<Frame>(line);}catch{bad++;failed=true;continue;}
                    if(f==null||f.Time>marker.Time||f.Time<=0)continue;
                    if(f.Kind=="gap"){failures++;failed=true;continue;}
                    if(f.Kind!="sample"||f.Devices==null)continue;
                    if(prev!=null&&f.Time<=prev.Sample.Time)continue;
                    var parent=IncidentReading.Parent(f,key);if(parent!="")ap=parent;
                    var point=new ReviewPoint{Sample=f,Device=Association(f,key),Ap=ap,ApState=ap==""?"":Association(f,ap),Load=ChannelText(f,key,ref radio)};
                    bool gap=prev!=null&&(failed||prev.Sample.Session!=f.Session||f.Time-prev.Sample.Time>95*TimeSpan.TicksPerSecond);
                    if(gap){gaps++;gapTimes.Add(prev.When+" bis "+point.When);if(gapTimes.Count>3)gapTimes.RemoveAt(0);}
                    failed=false;
                    if(pendingAfter){after=point.When+" · "+point.Load+(gap?" (nach Messlücke)":"");pendingAfter=false;}
                    if(prev!=null&&(point.Device!=prev.Device||(ap!=""&&ap==prev.Ap&&point.ApState!=prev.ApState))){
                        var description=new List<string>();
                        if(point.Device!=prev.Device)description.Add("Gerät: "+IncidentReading.State(prev.Sample,key)+" → "+IncidentReading.State(f,key));
                        if(ap!=""&&ap==prev.Ap&&point.ApState!=prev.ApState)description.Add("Zugangspunkt: "+IncidentReading.State(prev.Sample,ap)+" → "+IncidentReading.State(f,ap));
                        change="Letzte beobachtete Änderung zwischen "+prev.When+" und "+point.When+(gap?" (Messlücke).":".")+Environment.NewLine+
                            string.Join(Environment.NewLine,description)+Environment.NewLine+
                            "Kanalauslastung vorher: "+prev.When+" · "+prev.Load+Environment.NewLine+
                            "Bei erster geänderter Messung: "+point.When+" · "+point.Load;
                        pendingAfter=true;after=null;
                    }
                    seen|=f.Devices.ContainsKey(key);if(first==null)first=point;prev=point;count++;
                }
            }
        }
        var rows=new List<string>{"Rückschau bis zur Meldung · "+marker.LocalTime.ToString("G")};
        if(prev==null)rows.Add("Keine erfolgreichen Minutenmessungen vor dieser Meldung vorhanden.");
        else{
            rows.Add(count+" Messungen · "+first.When+" bis "+prev.When);
            rows.Add(change??(!seen?"Gerät in den archivierten Messungen nicht gemeldet.":count<2?"Nur eine Messung verfügbar.":"In den aufgezeichneten Messungen keine Änderung der WLAN-Zuordnung des Geräts oder seines bekannten Zugangspunkts sichtbar."));
            if(change!=null)rows.Add("Danach: "+(after??"keine weitere Minutenmessung vor der Meldung."));
            rows.Add("Letzte Funkmessung vor der Meldung: "+prev.When+" · "+prev.Load);
            if(failed||marker.Time-prev.Sample.Time>95*TimeSpan.TicksPerSecond)rows.Add("Keine durchgehende Messung von "+prev.When+" bis zur Meldung.");
            if(gaps>0)rows.Add("Unterbrechungen: "+gaps+" · zuletzt "+string.Join(" / ",gapTimes));
        }
        if(failures>0||bad>0)rows.Add("Fehlgeschlagene Routerabfragen: "+failures+" · unlesbare Einträge: "+bad);
        return string.Join(Environment.NewLine,rows);
    }
}
