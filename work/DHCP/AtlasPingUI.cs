using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using System.Windows.Forms;

class EchoValue {
    public long? Ms;
    public string Error;
    public string Text {get{return Ms.HasValue?Ms+" ms":Error=="TimedOut"?"Zeitüberschreitung (1 s)":Error;}}
    public static async Task<EchoValue> Read(string host){
        try{using(var ping=new Ping()){
            var reply=await ping.SendPingAsync(host,1000,new byte[32]).ConfigureAwait(false);
            return reply.Status==IPStatus.Success?new EchoValue{Ms=reply.RoundtripTime}:new EchoValue{Error=reply.Status.ToString()};
        }}catch(Exception ex){return new EchoValue{Error="Messfehler · "+ex.GetBaseException().Message};}
    }
}
class EchoPair {
    public string TargetIp;
    public string TargetKey,TargetName;
    public string HistoryKey(){return !String.IsNullOrEmpty(TargetKey)?TargetKey:"alt:"+TargetIp;}
    public bool Matches(string key){return HistoryKey()==key;}
    public long Time;public string Session;public EchoValue Router,Repeater;
    public static bool Gap(EchoPair a,EchoPair b){return a.Session!=b.Session||b.Time-a.Time>TimeSpan.TicksPerSecond*8;}
    public static DateTime Local(long ticks){return new DateTime(ticks,DateTimeKind.Utc).ToLocalTime();}
    public long Minute {get{return Time-Time%TimeSpan.TicksPerMinute;}}
}
class EchoStats {
    public int Sent,Ok,Timeout;public double? Median;public long? Min,Max;
    public EchoStats(IEnumerable<EchoValue> values){
        var all=values.ToList();Sent=all.Count;Timeout=all.Count(v=>v.Error=="TimedOut");
        var sorted=all.Where(v=>v.Ms.HasValue).Select(v=>v.Ms.Value).OrderBy(v=>v).ToList();Ok=sorted.Count;
        if(Ok==0)return;Min=sorted[0];Max=sorted[Ok-1];Median=(sorted[(Ok-1)/2]+(double)sorted[Ok/2])/2;
    }
    public string Range(){return Ok==0?"—":Min+"–"+Max+" ms";}
    public string Summary(){return (Ok==0?"—":Median.Value.ToString("0.#")+" / "+Max)+" ms · "+Ok+"/"+Sent;}
}
class EchoMinute {
    public long Time;public List<EchoPair> Samples=new List<EchoPair>();
    public EchoStats Router,Repeater;
    public void Add(EchoPair pair){Samples.Add(pair);Router=new EchoStats(Samples.Select(x=>x.Router));Repeater=new EchoStats(Samples.Select(x=>x.Repeater));}
}
class EchoPlot:Control{
    public List<EchoMinute> Minutes=new List<EchoMinute>();
    public long Selected=-1;public Action<long> Pick;
    public EchoPlot(){DoubleBuffered=true;Dock=DockStyle.Fill;BackColor=Color.White;}
    float X(long t){return 45+(Width-68)*(float)((double)(t-Minutes[0].Time)/Math.Max(TimeSpan.TicksPerMinute,Minutes.Last().Time-Minutes[0].Time));}
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);if(Minutes.Count>0&&Pick!=null)Pick(Minutes.OrderBy(m=>Math.Abs(X(m.Time)-e.X)).First().Time);}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;
        if(Minutes.Count==0){g.DrawString("Für diesen Tag liegen noch keine Laufzeitmessungen vor.",Font,Brushes.DimGray,12,12);return;}
        if(Width<150||Height<80)return;
        double hi=Math.Max(5,Minutes.Max(m=>Math.Max(m.Router.Max??0,m.Repeater.Max??0)));
        float bottom=Height-42;Func<double,float> y=v=>bottom-(float)(v/hi)*(bottom-15);
        foreach(double tick in new[]{0,hi/2,hi}){float yy=y(tick);g.DrawLine(Pens.Gainsboro,45,yy,Width-20,yy);g.DrawString(tick.ToString("0.#"),Font,Brushes.DimGray,2,yy-7);}
        if(Selected>=Minutes[0].Time&&Selected<=Minutes.Last().Time)g.DrawLine(Pens.Silver,X(Selected),6,X(Selected),bottom+17);
        for(int k=0;k<2;k++){
            using(var pen=new Pen(k==0?Color.SteelBlue:Color.DarkOrange,1.6f)){
                var brush=k==0?Brushes.SteelBlue:Brushes.DarkOrange;
                EchoMinute prev=null;PointF old=PointF.Empty;
                foreach(var m in Minutes){var stat=k==0?m.Router:m.Repeater;float x=X(m.Time)+(k==0?-1:1);
                    if(stat.Timeout>0)g.DrawLine(pen,x,bottom+3+7*k,x,bottom+9+7*k);
                    if(!stat.Median.HasValue){prev=null;continue;}
                    var pt=new PointF(x,y(stat.Median.Value));
                    if(prev!=null&&!EchoPair.Gap(prev.Samples.Last(),m.Samples.First()))g.DrawLine(pen,old,pt);
                    g.DrawLine(pen,x,y(stat.Max.Value),x,pt.Y);g.FillEllipse(brush,x-2,pt.Y-2,4,4);prev=m;old=pt;
                }
            }
        }
        g.DrawString(EchoPair.Local(Minutes[0].Time).ToString("HH:mm"),Font,Brushes.DimGray,40,Height-20);
        g.DrawString(EchoPair.Local(Minutes.Last().Time).ToString("HH:mm"),Font,Brushes.DimGray,Width-54,Height-20);
    }
}
partial class Atlas {
    readonly Timer latencyTimer=new Timer{Interval=5000};
    readonly CheckBox echoEnabled=new CheckBox{Text="Ping-Laufzeit mit aufzeichnen",AutoSize=true};
    string echoSession;bool echoBusy;event Action<EchoPair> EchoAdded;
    string PingPath(DateTime date){return Path.Combine(archive,"Laufzeit",date.ToString("yyyy-MM-dd")+".jsonl");}
    readonly ComboBox echoTarget=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=148,AccessibleName="Vergleichsziel"};
    class PingTarget{
        public string Name,Ip,Key;public Device Device;
        public PingTarget(string name,string ip){Name=name;Ip=ip;}
        public override string ToString(){return Name+(String.IsNullOrEmpty(Ip)?"":" · "+Ip);}
    }
    ComboBox archiveTarget;
    void RefreshPingTargets(){
        var old=echoTarget.SelectedItem as PingTarget;
        var targets=map.Data.Devices.Where(v=>v.AddressKnown && v.Infrastructure).OrderBy(v=>v.Name)
            .Select(v=>new PingTarget(v.Name,v.IP){Key=v.Key,Device=v}).ToArray();
        echoTarget.Items.Clear();echoTarget.Items.AddRange(targets);
        if(targets.Length>0)echoTarget.SelectedItem=targets.FirstOrDefault(v=>old!=null && v.Key==old.Key)??targets[0];
        UpdateArchiveTargets();
    }
    void UpdateArchiveTargets(){
        if(archiveTarget==null)return;var prior=archiveTarget.SelectedItem as PingTarget;
        var choices=echoTarget.Items.Cast<PingTarget>().ToList();var keys=new HashSet<string>(choices.Select(x=>x.Key));
        try{var path=PingPath(shown);if(File.Exists(path))foreach(var line in File.ReadLines(path)){
            try{var row=json.Deserialize<EchoPair>(line);if(row!=null && keys.Add(row.HistoryKey()))
                choices.Add(new PingTarget(row.TargetName??(String.IsNullOrEmpty(row.TargetIp)?"Altbestand · Ziel unbekannt":"Altbestand · nur IP"),row.TargetIp){Key=row.HistoryKey()});}catch(ArgumentException){}
        }}catch(IOException){}
        archiveTarget.Items.Clear();archiveTarget.Items.AddRange(choices.ToArray());
        if(choices.Count>0)archiveTarget.SelectedItem=choices.FirstOrDefault(x=>prior!=null && x.Key==prior.Key)??choices[0];
    }
    void InitPingPage(TabPage page){
        var options=ActionRow();page.AutoScroll=true;page.Controls.Add(options);
        var open=new Button{Text="Tagesverlauf öffnen …",AutoSize=true};

        options.FlowDirection=FlowDirection.TopDown;options.WrapContents=false;
        options.Controls.Add(new Label{Text="Messung vom PC · alle 5 Sekunden",AutoSize=true});
        options.Controls.Add(echoEnabled);options.Controls.Add(new Label{Text="Vergleichsziel zur FRITZ!Box",AutoSize=true});options.Controls.Add(echoTarget);
        var browse=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,AccessibleName="Ziel im Ping-Verlauf",Width=170};
        archiveTarget=browse;browse.DropDown+=(s,e)=>UpdateArchiveTargets();
        options.Controls.Add(new Label{Text="Aufzeichnungen ansehen",AutoSize=true,Margin=new Padding(3,12,3,3)});
        options.Controls.Add(browse);options.Controls.Add(open);
        echoTarget.SelectedIndexChanged+=(s,e)=>{StopLatency();StartLatency();};
        var tip=new ToolTip();tip.SetToolTip(echoEnabled,"Alle 5 s zur FRITZ!Box und zum gewählten Vergleichsziel, solange die Beobachtung läuft.");
        FormClosed+=(s,e)=>tip.Dispose();
        open.Click+=(s,e)=>{UpdateArchiveTargets();var item=browse.SelectedItem as PingTarget;if(item!=null)ShowLatencyFor(item);else MessageBox.Show("Für diesen Tag liegen keine Ping-Ziele vor.");};echoEnabled.CheckedChanged+=(s,e)=>{if(echoEnabled.Checked)StartLatency();else StopLatency();};
        latencyTimer.Tick+=async(s,e)=>await MeasurePair();
    }
    async void StartLatency(){if(echoTarget.SelectedItem==null||!echoEnabled.Checked||session==null||echoSession!=null)return;echoSession=Guid.NewGuid().ToString("N");latencyTimer.Start();await MeasurePair();}
    void StopLatency(){echoSession=null;latencyTimer.Stop();}
    async Task MeasurePair(){
        if(echoBusy||echoSession==null)return;echoBusy=true;
        var target=(PingTarget)echoTarget.SelectedItem;string ru=user.Text.Trim(),rp=password.Text;var pair=new EchoPair{TargetKey=target.Key,TargetName=target.Name,Time=DateTime.UtcNow.Ticks,Session=echoSession};
        try{
            pair.TargetIp=await Task.Run(()=>Reader.Address(Reader.CurrentHosts(ru,rp),target.Device));
            var tasks=await Task.WhenAll(EchoValue.Read("fritz.box"),pair.TargetIp==null?Task.FromResult(new EchoValue{Error="Adresse unbestätigt"}):EchoValue.Read(pair.TargetIp));
            if(IsDisposed||echoSession!=pair.Session)return;
            pair.Router=tasks[0];pair.Repeater=tasks[1];
            string file=PingPath(EchoPair.Local(pair.Time));Directory.CreateDirectory(Path.GetDirectoryName(file));File.AppendAllText(file,json.Serialize(pair)+Environment.NewLine);
            if(EchoAdded!=null)EchoAdded(pair);
        }catch(Exception ex){if(!IsDisposed){echoEnabled.Checked=false;status.Text="Laufzeitmessung gestoppt · "+ex.Message;}}
        finally{echoBusy=false;}
    }
    string RepeaterLoad(long minute,PingTarget target){
        var frames=plot.Frames.Where(f=>f.Time>=minute&&f.Time<minute+TimeSpan.TicksPerMinute&&(f.Kind=="gap"||f.Kind=="sample")).ToList();
        if(frames.Count==0)return "Keine Mesh-Messung";
        var frame=frames.Last();if(frame.Devices==null)return "Routerabfrage fehlgeschlagen";
        Observation ap;frame.Devices.TryGetValue(target.Key,out ap);
        if(ap==null||ap.Radios==null)return "Nicht gemeldet";
        return string.Join(" / ",ap.Radios.Values.Select(r=>r.Format()).Distinct());
    }
    static ListView EchoTable(params string[] labels){
        var view=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true,HideSelection=false,MultiSelect=false};
        foreach(string label in labels)view.Columns.Add(label);return view;
    }
    void ShowLatencyFor(PingTarget target){
        DateTime date=shown;
        using(var dialog=new Form{Text="Laufzeit · "+date.ToShortDateString(),Size=new Size(930,700),MinimumSize=new Size(580,470),StartPosition=FormStartPosition.CenterParent}){
            var graph=new EchoPlot();
            var legend=new Label{Dock=DockStyle.Top,Height=39,Text="Blau: FRITZ!Box     Orange: "+target+"\r\nLinie: Median in ms · Spitze: Maximum · Strich unten: Zeitüberschreitung"};
            var minutes=EchoTable("Minute","Router · Median / Max · Antworten",target.Name+" · Median / Max · Antworten","Kanalauslastung · "+target.Name);
            var details=EchoTable("Messzeit","FRITZ!Box",target.ToString(),"Beobachtung");
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,RowCount=6,ColumnCount=1,Padding=new Padding(6)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Percent,36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,23));layout.RowStyles.Add(new RowStyle(SizeType.Percent,36));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute,23));layout.RowStyles.Add(new RowStyle(SizeType.Percent,28));
            var headline=new Label{AutoSize=true,Text="Minutenvergleich · Minute für Einzelwerte auswählen"};
            var sub=new Label{AutoSize=true,Text="Einzelmessungen"};
            layout.Controls.Add(legend,0,0);layout.Controls.Add(graph,0,1);layout.Controls.Add(headline,0,2);layout.Controls.Add(minutes,0,3);layout.Controls.Add(sub,0,4);layout.Controls.Add(details,0,5);dialog.Controls.Add(layout);
            minutes.Columns[0].Width=110;minutes.Columns[1].Width=200;minutes.Columns[2].Width=200;minutes.Columns[3].Width=280;
            details.Columns[0].Width=100;details.Columns[1].Width=130;details.Columns[2].Width=130;details.Columns[3].Width=380;
            var rows=new Dictionary<long,ListViewItem>();var buckets=graph.Minutes;
            Action showDetail=()=>{
                details.BeginUpdate();details.Items.Clear();var item=minutes.SelectedItems.Cast<ListViewItem>().FirstOrDefault();var m=item==null?null:item.Tag as EchoMinute;
                graph.Selected=m==null?-1:m.Time;
                if(m!=null){sub.Text="Einzelmessungen · "+EchoPair.Local(m.Time).ToString("HH:mm")+" Uhr · Min–Max: Router "+m.Router.Range()+" · "+target.Name+" "+m.Repeater.Range();
                    foreach(var pair in m.Samples){var row=new ListViewItem(new[]{EchoPair.Local(pair.Time).ToString("HH:mm:ss"),pair.Router.Text,pair.Repeater.Text,""});
                        details.Items.Add(row);}
                }else{sub.Text="Einzelmessungen";if(item!=null)details.Items.Add(new ListViewItem(new[]{"—","—","—",item.Text+" · "+item.SubItems[3].Text}));}
                details.EndUpdate();graph.Invalidate();
            };
            minutes.SelectedIndexChanged+=(s,e)=>showDetail();
            graph.Pick=time=>{var row=rows[time];row.Selected=true;row.EnsureVisible();minutes.Focus();};
            bool loading=true;EchoPair last=null;
            Action<EchoPair> add=pair=>{
                if(!pair.Matches(target.Key)||EchoPair.Local(pair.Time).Date!=date||last!=null&&pair.Time<=last.Time)return;
                if(last!=null&&EchoPair.Gap(last,pair)){
                    var gap=new ListViewItem(new[]{"Messlücke","—","—",EchoPair.Local(last.Time).ToString("T")+" bis "+EchoPair.Local(pair.Time).ToString("T")+" · keine durchgehende Messreihe"});
                    gap.ForeColor=Color.DimGray;minutes.Items.Insert(0,gap);
                }
                EchoMinute m=buckets.LastOrDefault();ListViewItem row;
                if(m==null||m.Time!=pair.Minute){
                    m=new EchoMinute{Time=pair.Minute};buckets.Add(m);row=new ListViewItem(new[]{EchoPair.Local(m.Time).ToString("HH:mm"),"","",""});row.Tag=m;rows[m.Time]=row;minutes.Items.Insert(0,row);
                }else row=rows[m.Time];
                last=pair;m.Add(pair);
                row.SubItems[1].Text=m.Router.Summary();row.SubItems[2].Text=m.Repeater.Summary();
                if(!loading){row.SubItems[3].Text=RepeaterLoad(m.Time,target);if(buckets.Count>1){var prior=buckets[buckets.Count-2];rows[prior.Time].SubItems[3].Text=RepeaterLoad(prior.Time,target);}if(minutes.SelectedItems.Count==0)row.Selected=true;if(row.Selected)showDetail();graph.Invalidate();}
            };
            int damaged=0;string error=null;
            minutes.BeginUpdate();
            try{string file=PingPath(date);if(File.Exists(file))foreach(var line in File.ReadLines(file)){
                try{var pair=json.Deserialize<EchoPair>(line);if(pair==null||pair.Router==null||pair.Repeater==null)damaged++;else add(pair);}
                catch(ArgumentException){damaged++;}catch(InvalidOperationException){damaged++;}
            }}catch(IOException ex){error=ex.Message;}catch(UnauthorizedAccessException ex){error=ex.Message;}
            foreach(var kv in rows)kv.Value.SubItems[3].Text=RepeaterLoad(kv.Key,target);
            loading=false;minutes.EndUpdate();if(error!=null||damaged>0)headline.Text=error??(damaged+" unlesbare Laufzeitmessungen");
            dialog.Shown+=(s,e)=>{if(minutes.Items.Count>0)minutes.Items[0].Selected=true;};
            EchoAdded+=add;try{dialog.ShowDialog(this);}finally{EchoAdded-=add;}
        }
    }
}
