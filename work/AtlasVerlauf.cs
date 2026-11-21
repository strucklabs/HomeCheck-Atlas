using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

class Segment {
    public string Id,Peer,Radio;
    public int? Up,Down;
}
class Observation {
    public string Name,State;
    public List<Segment> Local=new List<Segment>(),Upstream=new List<Segment>();
}
class Frame {
    public long Time;
    public string Session,Kind,Message,Marker;
    public Dictionary<string,Observation> Devices;
    public DateTime LocalTime {get{return new DateTime(Time,DateTimeKind.Utc).ToLocalTime();}}
    public static Frame Capture(Inventory inv){
        var frame=new Frame{Kind="sample",Devices=new Dictionary<string,Observation>()};
        var root=inv.Devices.First(n=>n.Role=="master");
        var depth=new Dictionary<Device,int>{{root,0}};var queue=new Queue<Device>();queue.Enqueue(root);
        var adj=inv.Links.SelectMany(l=>new[]{new{D=l.A,L=l},new{D=l.B,L=l}}).ToLookup(x=>x.D,x=>x.L);
        while(queue.Count>0){var d=queue.Dequeue();foreach(var l in adj[d]){
            var p=l.A==d?l.B:l.A;if(depth.ContainsKey(p))continue;depth[p]=depth[d]+1;queue.Enqueue(p);}}
        foreach(var d in inv.Devices.Where(n=>n.Key!="")){
            var o=new Observation{Name=d.Name,State=d==root?"Mesh-Master":"Ohne gemeldete Verbindung"};frame.Devices[d.Key]=o;
            if(!depth.ContainsKey(d)||d==root)continue;
            var parents=adj[d].Where(l=>depth[l.A==d?l.B:l.A]<depth[d]).ToList();
            var names=new List<string>();
            foreach(var l in parents){
                var p=l.A==d?l.B:l.A;names.Add(p.Name+" ("+l.Type+")");
                if(l.Type=="WLAN")o.Local.Add(Measure(l,d));
                foreach(var back in adj[p].Where(b=>b.Type=="WLAN"&&(b.A==root||b.B==root)&&p!=root))o.Upstream.Add(Measure(back,p));
            }
            o.State=string.Join(", ",names.Distinct());
        } return frame;
    }
    static Segment Measure(Link l,Device from){
        var peer=l.A==from?l.B:l.A;
        return new Segment{Id=from.Key+"|"+peer.Key+"|"+l.PortKey+"|"+l.Radio,Peer=peer.Name,Radio=l.Radio,
            Up=l.A==from?l.Tx:l.Rx,Down=l.A==from?l.Rx:l.Tx};
    }
}
class HistoryPlot:Control {
    public List<Frame> Frames=new List<Frame>();
    public string Key="";public bool Up=true,Down=true;
    public HistoryPlot(){DoubleBuffered=true;BackColor=Color.White;Dock=DockStyle.Fill;}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;if(Width<120||Height<120)return;
        var data=Frames.Where(f=>f.Kind!="note").OrderBy(f=>f.Time).ToList();
        if(data.Count==0){g.DrawString("Für diesen Tag liegen keine Messungen vor.",Font,Brushes.DimGray,5,7);return;}
        long start=data.First().Time,end=Math.Max(start+TimeSpan.TicksPerMinute,data.Last().Time);
        Func<long,float> x=t=>38+(Width-51)*(float)((double)(t-start)/(end-start));
        int half=(Height-24)/2;
        for(int row=0;row<2;row++){
            int top=row*half+20,bottom=(row+1)*half-5;
            g.DrawString(row==0?"Gerät ↔ Zugangspunkt · dB":"Zugangspunkt ↔ Router · dB",Font,Brushes.Black,4,row*half+1);
            var levels=data.Where(f=>f.Devices!=null&&f.Devices.ContainsKey(Key)).SelectMany(f=>row==0?f.Devices[Key].Local:f.Devices[Key].Upstream).SelectMany(v=>new[]{v.Up,v.Down}).Where(v=>v.HasValue).Select(v=>v.Value);
            int lo=Math.Min(0,levels.DefaultIfEmpty(0).Min()),hi=Math.Max(40,levels.DefaultIfEmpty(40).Max());
            Func<int,float> y=v=>bottom-(bottom-top)*(float)(v-lo)/(hi-lo);
            foreach(int tick in new[]{lo,20,hi}.Distinct()){float yy=y(tick);g.DrawLine(Pens.Gainsboro,38,yy,Width-12,yy);g.DrawString(tick.ToString(),Font,Brushes.DimGray,2,yy-7);}
            var prev=new Dictionary<string,PointF>();Frame last=null;
            foreach(var f in data){
                float xx=x(f.Time);if(f.Kind=="mark"){g.DrawLine(Pens.Firebrick,xx,top,xx,bottom);continue;}
                if(last==null||last.Session!=f.Session||f.Time-last.Time>TimeSpan.TicksPerSecond*95)prev.Clear();last=f;
                var next=new Dictionary<string,PointF>();Observation o;
                if(f.Devices!=null&&f.Devices.TryGetValue(Key,out o)){
                    foreach(var seg in row==0?o.Local:o.Upstream)for(int j=0;j<2;j++){
                        var val=j==0?seg.Up:seg.Down;if(!val.HasValue||(j==0?!Up:!Down))continue;
                        var pt=new PointF(xx,y(val.Value));var id=seg.Id+j;PointF old;
                        if(prev.TryGetValue(id,out old))g.DrawLine(j==0?Pens.Teal:Pens.DarkOrange,old,pt);
                        g.FillEllipse(j==0?Brushes.Teal:Brushes.DarkOrange,pt.X-2,pt.Y-2,4,4);next[id]=pt;
                    }
                }prev=next;
            }
        }
        g.DrawString(data.First().LocalTime.ToString("HH:mm:ss"),Font,Brushes.DimGray,36,Height-19);
        g.DrawString(new DateTime(end,DateTimeKind.Utc).ToLocalTime().ToString("HH:mm:ss"),Font,Brushes.DimGray,Width-63,Height-19);
    }
}
partial class Atlas {
    readonly string archive=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"HomeCheck","Meshverlauf");
    readonly JavaScriptSerializer json=new JavaScriptSerializer(){MaxJsonLength=32000000};
    readonly HistoryPlot plot=new HistoryPlot();
    readonly ComboBox devices=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=160};
    readonly DateTimePicker day=new DateTimePicker{Format=DateTimePickerFormat.Short,Width=103};
    readonly ListBox eventsList=new ListBox{Dock=DockStyle.Bottom,Height=90,HorizontalScrollbar=true};
    readonly Button monitor=new Button{Text="Beobachtung starten",AutoSize=true};
    readonly Timer timer=new Timer{Interval=60000};
    readonly Label monitorState=new Label{AutoSize=true,Text="Beobachtung aus"};
    bool busy,refreshing;string session;DateTime shown;
    class Choice {public string Key,Name;public override string ToString(){return Name;}}
    class EventLine {public Frame F;public string Text;public override string ToString(){return F.LocalTime.ToString("T")+"  "+Text;}}
    void InitHistory(FlowLayoutPanel bar,TabControl pages){
        var mark=new Button{Text="Störung markieren",AutoSize=true};
        bar.Controls.Add(monitor);bar.Controls.Add(mark);
        var page=new TabPage("Verlauf");pages.TabPages.Add(page);
        var options=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};
        var up=new CheckBox{Text="Hin ↑",ForeColor=Color.Teal,AutoSize=true,Checked=true};
        var down=new CheckBox{Text="Zurück ↓",ForeColor=Color.DarkOrange,AutoSize=true,Checked=true};
        var edit=new Button{Text="Marke beschriften",AutoSize=true};
        options.Controls.AddRange(new Control[]{day,devices,up,down,edit,monitorState});
        page.Controls.Add(plot);page.Controls.Add(eventsList);page.Controls.Add(options);
        up.CheckedChanged+=(a,b)=>{plot.Up=up.Checked;plot.Invalidate();};
        down.CheckedChanged+=(a,b)=>{plot.Down=down.Checked;plot.Invalidate();};
        day.ValueChanged+=(a,b)=>LoadDay();devices.SelectedIndexChanged+=(a,b)=>{if(!refreshing)RenderEvents();};
        timer.Tick+=async(a,b)=>await Poll();
        monitor.Click+=async(a,b)=>{
            if(session!=null){timer.Stop();session=null;monitor.Text="Beobachtung starten";monitorState.Text="Beobachtung aus";return;}
            if(password.Text==""){status.Text="Kennwort erforderlich";return;}
            session=Guid.NewGuid().ToString("N");monitor.Text="Beobachtung stoppen";monitorState.Text="Mesh wird gelesen …";timer.Start();await Poll();
        };
        mark.Click+=(a,b)=>{var f=new Frame{Kind="mark",Marker=Guid.NewGuid().ToString("N"),Message="Störung markiert"};
            if(Save(f)){day.Value=DateTime.Today;pages.SelectedTab=page;SelectMarker(f.Marker);status.Text="Störung markiert · "+f.LocalTime.ToString("T");}};
        edit.Click+=(a,b)=>EditMarker();eventsList.DoubleClick+=(a,b)=>EditMarker();
        FormClosed+=(a,b)=>{session=null;timer.Dispose();};LoadDay();
    }
    async Task Poll(){
        if(busy||session==null)return;busy=true;string id=session,u=user.Text.Trim(),pw=password.Text;Frame f;
        try{f=await Task.Run(()=>{var inv=new Inventory();new Reader("fritz.box",u,pw).Collect(inv,true,null,true);return Frame.Capture(inv);});}
        catch(Exception ex){f=new Frame{Kind="gap",Message="Routerabfrage fehlgeschlagen · "+ex.Message};}
        finally{busy=false;}
        if(IsDisposed||session!=id)return;
        if(shown==DateTime.Today.AddDays(-1)&&day.Value.Date==shown)day.Value=DateTime.Today;
        if(Save(f))monitorState.Text=f.Kind=="gap"?"Abfragelücke · "+f.LocalTime.ToString("t"):"Letzte Messung "+f.LocalTime.ToString("T");
    }
    string FileFor(DateTime d){return Path.Combine(archive,d.ToString("yyyy-MM-dd")+".jsonl");}
    bool Save(Frame f){
        if(f.Time==0)f.Time=DateTime.UtcNow.Ticks;f.Session=session;
        try{Directory.CreateDirectory(archive);File.AppendAllText(FileFor(f.LocalTime),json.Serialize(f)+Environment.NewLine);}
        catch(Exception ex){status.Text="Verlauf nicht gespeichert: "+ex.Message;return false;}
        if(f.LocalTime.Date==shown){plot.Frames.Add(f);RefreshChoices();}return true;
    }
    void LoadDay(){
        shown=day.Value.Date;plot.Frames.Clear();int ignored=0;
        try{string file=FileFor(shown);if(File.Exists(file))foreach(var line in File.ReadLines(file)){
            try{var f=json.Deserialize<Frame>(line);if(f!=null&&f.Time>0)plot.Frames.Add(f);}catch{ignored++;}}}
        catch(Exception ex){status.Text="Verlauf nicht lesbar: "+ex.Message;}
        if(ignored>0)status.Text=ignored+" beschädigte Verlaufseinträge übersprungen";
        RefreshChoices();
    }
    void RefreshChoices(){
        var old=devices.SelectedItem as Choice;var current=list.SelectedItem as Device;
        string key=old!=null?old.Key:current!=null?current.Key:"";
        var names=new Dictionary<string,string>();foreach(var frame in plot.Frames.Where(f=>f.Devices!=null))
            foreach(var d in frame.Devices)names[d.Key]=d.Value.Name;
        foreach(Device d in list.Items)if(names.ContainsKey(d.Key))names[d.Key]=d.Name;
        refreshing=true;devices.BeginUpdate();devices.Items.Clear();
        foreach(var p in names.OrderBy(v=>v.Value))devices.Items.Add(new Choice{Key=p.Key,Name=p.Value});
        devices.SelectedItem=devices.Items.Cast<Choice>().FirstOrDefault(v=>v.Key==key)??devices.Items.Cast<Choice>().FirstOrDefault();
        devices.EndUpdate();refreshing=false;RenderEvents();
    }
    void ChooseHistory(Device d){if(d!=null)devices.SelectedItem=devices.Items.Cast<Choice>().FirstOrDefault(c=>c.Key==d.Key);}
    void SelectMarker(string id){foreach(EventLine e in eventsList.Items)if(e.F.Marker==id){eventsList.SelectedItem=e;break;}}
    void RenderEvents(){
        var c=devices.SelectedItem as Choice;plot.Key=c==null?"":c.Key;plot.Invalidate();
        var selected=eventsList.SelectedItem as EventLine;
        eventsList.BeginUpdate();eventsList.Items.Clear();string previous=null;
        var notes=plot.Frames.Where(f=>f.Kind=="note").GroupBy(f=>f.Marker).ToDictionary(g=>g.Key,g=>g.Last().Message);
        foreach(var f in plot.Frames.OrderBy(v=>v.Time)){
            string msg=f.Message;
            if(f.Kind=="note")continue;
            if(f.Kind=="mark"&&notes.ContainsKey(f.Marker))msg+=" · "+notes[f.Marker];
            if(f.Kind=="sample"){
                if(c==null)continue;Observation o;
                msg=f.Devices.TryGetValue(c.Key,out o)?o.State:"Gerät im Mesh nicht gemeldet";
                if(msg==previous)continue;previous=msg;
            }else if(f.Kind=="gap")previous=null;
            eventsList.Items.Insert(0,new EventLine{F=f,Text=msg});
        }
        if(selected!=null&&selected.F.Kind=="mark")SelectMarker(selected.F.Marker);eventsList.EndUpdate();
    }
    void EditMarker(){
        var line=eventsList.SelectedItem as EventLine;if(line==null||line.F.Kind!="mark"){monitorState.Text="Bitte eine Störungsmarke auswählen";return;}
        using(var dialog=new Form{Text="Notiz zur Störung um "+line.F.LocalTime.ToString("T"),Width=420,Height=155,StartPosition=FormStartPosition.CenterParent}){
            var old=plot.Frames.LastOrDefault(f=>f.Kind=="note"&&f.Marker==line.F.Marker);
            var box=new TextBox{Dock=DockStyle.Fill,Multiline=true,Text=old==null?"":old.Message};
            var ok=new Button{Text="Speichern",Dock=DockStyle.Bottom,DialogResult=DialogResult.OK};
            dialog.Controls.Add(box);dialog.Controls.Add(ok);dialog.AcceptButton=ok;
            if(dialog.ShowDialog(this)==DialogResult.OK)Save(new Frame{Kind="note",Time=line.F.Time,Marker=line.F.Marker,Message=box.Text.Trim()});
        }
    }
}
