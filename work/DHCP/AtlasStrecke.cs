using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

class RouteHop {
    public Device Device;public string Name,Host,Skip;
    public readonly SortedDictionary<int,EchoValue> Points=new SortedDictionary<int,EchoValue>();
    public RouteHop(Device d,string name=null){
        Device=d;Name=name??d.Name;IPAddress address;
        if(d.Inactive)Skip="als inaktiv gemeldet";
        else if(IPAddress.TryParse(d.IP,out address)&&address.AddressFamily==AddressFamily.InterNetwork &&
            address.GetAddressBytes()[0]>0&&address.GetAddressBytes()[0]<224&&address.GetAddressBytes()[3]!=255)Host=address.ToString();
        else if(d.Role=="master")Host="fritz.box";
        else Skip="keine bekannte IPv4-Adresse";
    }
}
class RoutePlan {
    public readonly List<RouteHop> Hops=new List<RouteHop>();public string Source;
    public static RoutePlan Build(Inventory inv,Device target){
        var plan=new RoutePlan();var root=inv.Devices.FirstOrDefault(d=>d.Role=="master");
        var pred=new Dictionary<Device,Device>();var ways=new Dictionary<Device,int>();var depth=new Dictionary<Device,int>();
        var queue=new Queue<Device>();if(root!=null){queue.Enqueue(root);depth[root]=0;ways[root]=1;}
        while(queue.Count>0){
            var d=queue.Dequeue();
            foreach(var peer in inv.Links.Where(l=>l.A==d||l.B==d).Select(l=>l.A==d?l.B:l.A).Distinct()){
                if(!depth.ContainsKey(peer)){pred[peer]=d;ways[peer]=ways[d];depth[peer]=depth[d]+1;queue.Enqueue(peer);}
                else if(depth[peer]==depth[d]+1)ways[peer]=Math.Min(2,ways[peer]+ways[d]);
            }
        }
        if(depth.ContainsKey(target)&&ways[target]==1){
            var route=new Stack<Device>();var d=target;route.Push(d);while(d!=root){d=pred[d];route.Push(d);}
            foreach(var hop in route)plan.Hops.Add(new RouteHop(hop));plan.Source="Pfad laut letzter Netzerfassung";
        }else{
            if(root!=null)plan.Hops.Add(new RouteHop(root));plan.Hops.Add(new RouteHop(target));
            plan.Source="Zwischenstationen unbekannt oder mehrdeutig · nur bekannte Endpunkte";
        }
        return plan;
    }
}
class RoutePlot:Control {
    public List<RouteHop> Hops;public static Color Ink(int i){return new[]{Color.SteelBlue,Color.DarkOrange,Color.SeaGreen,Color.MediumVioletRed,Color.SlateBlue,Color.SaddleBrown}[i%6];}
    public RoutePlot(){DoubleBuffered=true;Dock=DockStyle.Fill;BackColor=Color.White;}
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);if(Hops==null||Width<160||Height<95)return;
        var g=e.Graphics;float bottom=Height-36;long max=Math.Max(5,Hops.SelectMany(h=>h.Points.Values).Max(v=>v.Ms)??0);
        Func<int,float> x=s=>48+(Width-80)*s/59f;
        Func<long,float> y=v=>bottom-(bottom-17)*v/max;
        foreach(var tick in new[]{0L,max/2,max}.Distinct()){float yy=y(tick);g.DrawLine(Pens.Gainsboro,48,yy,Width-30,yy);g.DrawString(tick+" ms",Font,Brushes.DimGray,0,yy-7);}
        for(int t=0;t<=60;t+=15)g.DrawString(t+" s",Font,Brushes.DimGray,x(Math.Min(t,59))-9,Height-19);
        for(int i=0;i<Hops.Count;i++)using(var pen=new Pen(Ink(i),1.6f))using(var brush=new SolidBrush(Ink(i))){
            int previous=-2;PointF old=PointF.Empty;
            foreach(var point in Hops[i].Points){
                if(!point.Value.Ms.HasValue){if(point.Value.Error=="TimedOut"){float xx=x(point.Key);float yy=bottom+3+i%5*2;g.DrawLine(pen,xx,yy,xx,yy+4);}previous=-2;continue;}
                var pt=new PointF(x(point.Key),y(point.Value.Ms.Value));if(previous==point.Key-1)g.DrawLine(pen,old,pt);
                g.FillEllipse(brush,pt.X-2,pt.Y-2,4,4);previous=point.Key;old=pt;
            }
        }
    }
}
partial class Atlas {
    readonly Button routeButton=new Button{Text="Strecke testen …",Dock=DockStyle.Bottom,Enabled=false};
    internal static async Task RunRoute(List<RouteHop> hops,Func<string,Task<EchoValue>> read,Action changed,CancellationToken token,int slots=60,int period=1000){
        var targets=hops.Where(h=>h.Host!=null).GroupBy(h=>h.Host).ToArray();var clock=Stopwatch.StartNew();
        for(int slot=0;slot<slots;slot++){
            int wait=(int)(slot*period-clock.ElapsedMilliseconds);if(wait>0)await Task.Delay(wait,token);
            token.ThrowIfCancellationRequested();
            slot=Math.Max(slot,(int)(clock.ElapsedMilliseconds/period));if(slot>=slots)break;
            var values=await Task.WhenAll(targets.Select(group=>read(group.Key)));
            token.ThrowIfCancellationRequested();
            for(int k=0;k<targets.Length;k++)foreach(var hop in targets[k])hop.Points[slot]=values[k];
            changed();
        }
    }
    void ShowRoute(Device target){
        if(target==null)return;var plan=RoutePlan.Build(map.Data,target);
        using(var f=new Form{Text="Streckentest · "+target.Name,Size=new Size(800,505),MinimumSize=new Size(530,410),StartPosition=FormStartPosition.CenterParent})
        using(var cancel=new CancellationTokenSource()){
            var plot=new RoutePlot{Hops=plan.Hops};
            var pathLabel=new Label{Text=plan.Source+Environment.NewLine+"Dieser PC → "+string.Join(" → ",plan.Hops.Select(h=>h.Name)),AutoSize=true,MaximumSize=new Size(730,0)};
            var table=new ListView{View=View.Details,Dock=DockStyle.Fill,FullRowSelect=true};foreach(var label in new[]{"Station","Ping-Ziel","Median","Maximum","Antworten","Letzter Wert"})table.Columns.Add(label);
            int[] widths={155,185,65,70,85,175};for(int k=0;k<widths.Length;k++)table.Columns[k].Width=widths[k];
            for(int k=0;k<plan.Hops.Count;k++){
                var hop=plan.Hops[k];var row=new ListViewItem(new[]{hop.Name,hop.Host??hop.Skip,"—","—","—","—"});
                row.ForeColor=hop.Host==null?Color.DimGray:RoutePlot.Ink(k);table.Items.Add(row);}
            var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true};
            var start=new Button{Text="60 Sekunden starten",AutoSize=true,Enabled=plan.Hops.Any(h=>h.Host!=null)};
            var state=new Label{Text="Laufzeit vom PC je Station · Strich unten = Timeout",AutoSize=true,Anchor=AnchorStyles.Left};
            bar.Controls.Add(start);bar.Controls.Add(state);
            var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4,Padding=new Padding(6)};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent,65));layout.RowStyles.Add(new RowStyle(SizeType.Percent,35));layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(pathLabel,0,0);layout.Controls.Add(plot,0,1);layout.Controls.Add(table,0,2);layout.Controls.Add(bar,0,3);f.Controls.Add(layout);
            f.Resize+=(s,e)=>pathLabel.MaximumSize=new Size(Math.Max(200,f.ClientSize.Width-28),0);
            bool running=false;
            start.Click+=async(s,e)=>{
                if(running){cancel.Cancel();start.Enabled=false;state.Text="Abgebrochen";return;}
                running=true;start.Text="Abbrechen";state.Text="Messung läuft …";
                try{string ru=user.Text.Trim(),rp=password.Text;var hosts=await Task.Run(()=>Reader.CurrentHosts(ru,rp));cancel.Token.ThrowIfCancellationRequested();foreach(var hop in plan.Hops){hop.Host=Reader.Address(hosts,hop.Device);table.Items[plan.Hops.IndexOf(hop)].SubItems[1].Text=hop.Host??"Adresse unbestätigt";}await RunRoute(plan.Hops,EchoValue.Read,()=>{
                    for(int k=0;k<plan.Hops.Count;k++){var hop=plan.Hops[k];if(hop.Points.Count==0)continue;
                        var stats=new EchoStats(hop.Points.Values);var cells=table.Items[k].SubItems;
                        cells[2].Text=stats.Median.HasValue?stats.Median.Value.ToString("0.#")+" ms":"—";
                        cells[3].Text=stats.Max.HasValue?stats.Max+" ms":"—";cells[4].Text=stats.Ok+"/"+stats.Sent;
                        cells[5].Text=hop.Points.Last().Value.Text;}
                    plot.Invalidate();
                },cancel.Token);if(!f.IsDisposed)state.Text="Messung abgeschlossen";}
                catch(OperationCanceledException){}catch(Exception ex){if(!f.IsDisposed)state.Text="Messfehler · "+ex.Message;}
                finally{if(!f.IsDisposed){start.Enabled=false;start.Text="Beendet";}}
            };
            f.FormClosing+=(s,e)=>cancel.Cancel();f.ShowDialog(this);
        }
    }
}
