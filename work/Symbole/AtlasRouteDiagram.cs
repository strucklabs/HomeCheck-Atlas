using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

// A topology view, not a traceroute: all probes originate at this PC.
sealed class RouteDiagram:ScrollableControl {
    readonly RoutePlan plan;
    readonly Inventory inventory;
    readonly Func<Device,string> iconFor;
    readonly ToolTip tip=new ToolTip();
    const int Step=158,OriginWidth=105;
    public RouteDiagram(RoutePlan route,Inventory data,Func<Device,string> icons){
        plan=route;inventory=data;iconFor=icons;
        Dock=DockStyle.Fill;Height=145;MinimumSize=new Size(0,145);
        DoubleBuffered=true;BackColor=Color.White;AutoScroll=true;
        AutoScrollMinSize=new Size(OriginWidth+Math.Max(1,plan.Hops.Count)*Step+12,0);
        AccessibleName="Grafischer Pfad laut Netzerfassung";
        AccessibleDescription="Messungen ab diesem PC; "+String.Join(" – ",plan.Hops.Select(h=>h.Name));
        tip.SetToolTip(this,"Alle Pings starten auf diesem PC. Die Verbindungslinien zeigen die erfasste Topologie, keine einzelnen Ping-Teilstrecken. Bei langen Pfaden unten horizontal scrollen.");
    }
    public string Connection(int index){
        var a=plan.Hops[index].Device;var b=plan.Hops[index+1].Device;
        var edges=inventory.Links.Where(e=>e.A==a&&e.B==b||e.A==b&&e.B==a).ToArray();
        if(edges.Length==0)return "ungeklärt";
        if(edges.Any(e=>e.Manual))return "LAN · manuell";
        var types=edges.Select(e=>e.Type).Where(t=>!String.IsNullOrWhiteSpace(t)).Distinct().ToArray();
        return types.Length==1?types[0]:"mehrdeutig";
    }
    protected override void OnPaint(PaintEventArgs e){
        base.OnPaint(e);var g=e.Graphics;g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TranslateTransform(AutoScrollPosition.X,0);
        const float cy=41;
        using(var small=new Font(Font.FontFamily,8f))
        using(var centered=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter}){
            DeviceIcons.Draw(g,"desktop-tower-monitor",new RectangleF(31,21,40,40),Color.DimGray);
            g.DrawString("Dieser PC",Font,Brushes.DimGray,new RectangleF(5,67,91,22),centered);
            g.DrawString("Messquelle",small,Brushes.DimGray,new RectangleF(5,93,91,24),centered);
            g.DrawLine(Pens.Gainsboro,OriginWidth-5,12,OriginWidth-5,119);
            for(int k=0;k<plan.Hops.Count-1;k++){
                float x=OriginWidth+Step*k+Step/2f;string text=Connection(k);
                bool muted=plan.Hops[k].Device.Inactive||plan.Hops[k+1].Device.Inactive;
                using(var pen=new Pen(muted?Color.Silver:Color.SlateGray,1.6f)){
                    if(text=="WLAN")pen.DashPattern=new float[]{2,1.5f};
                    else if(text=="ungeklärt"||text=="mehrdeutig")pen.DashStyle=DashStyle.Dot;
                    g.DrawLine(pen,x+24,cy,x+Step-24,cy);
                }
                g.DrawString(text,small,Brushes.DimGray,new RectangleF(x+24,9,Step-48,26),centered);
            }
            for(int k=0;k<plan.Hops.Count;k++){
                var hop=plan.Hops[k];float x=OriginWidth+Step*k+Step/2f;
                var color=hop.Device.Inactive?Color.Silver:hop.Host==null?Color.SlateGray:RoutePlot.Ink(k);
                using(var brush=new SolidBrush(color))g.FillEllipse(brush,x-22,cy-22,44,44);
                DeviceIcons.Draw(g,iconFor(hop.Device),new RectangleF(x-18,cy-18,36,36),Color.White);
                g.DrawString(hop.Name,Font,hop.Device.Inactive?Brushes.Gray:Brushes.DarkSlateGray,new RectangleF(x-Step/2+5,65,Step-10,34),centered);
                g.DrawString(hop.Host??hop.Skip??"Adresse unbestätigt",small,Brushes.DimGray,new RectangleF(x-Step/2+5,101,Step-10,24),centered);
            }
        }
    }
    protected override void Dispose(bool disposing){if(disposing)tip.Dispose();base.Dispose(disposing);}
}
