﻿using System;
using System.IO;
using System.Net;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Web.Script.Serialization;
using D = System.Collections.Generic.Dictionary<string,object>;

using System.Drawing;
using System.Drawing.Drawing2D;
using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

class Device {
    public string Name,IP="",Mac="",Role="",Kind="",Details="";
    public bool Friendly; public string MeshId=""; public string Key {get{return Mac!=""?Mac:MeshId;}}
    public string Model="",HostStatus="";
    public bool Inactive {get{return HostStatus=="inaktiv";}}
    public PointF Pos; public bool Linked;
    public override string ToString(){return Name+(IP==""?"":"   "+IP);}
    public Device(string name){ Name=name; }
    public void Note(string section,string data){Details+="\r\n"+section+"\r\n"+data+"\r\n";}
}
class Link {
    public string Radio="",PortKey=""; public Device A,B; public string Type; public int? Rx,Tx;
    public string Label {get{return Type=="WLAN"?"SNR "+(Rx.HasValue||Tx.HasValue?
        (Rx.HasValue?Rx.ToString():"–")+"/"+(Tx.HasValue?Tx.ToString():"–")+" dB":"k. A."):Type;}}
    public static float DistanceForSnr(int db){return 350-4*Math.Max(-30,Math.Min(55,db));}
    public float Length {get {var values=new[]{Rx,Tx}.Where(v=>v.HasValue).ToArray();
        if(Type!="WLAN"||values.Length==0)return 215;
        return DistanceForSnr(values.Min(v=>v.Value));}}
}
class Inventory {
    public List<Device> Devices=new List<Device>(); public List<Link> Links=new List<Link>();
    public List<string> Messages=new List<string>();
    public Dictionary<string,Device> Macs=new Dictionary<string,Device>(StringComparer.OrdinalIgnoreCase);
    public static string Val(D d,string k){object v;return d.TryGetValue(k,out v)?Convert.ToString(v):"";}
    public static IEnumerable<D> Rows(D d,string k){object v;var e=d.TryGetValue(k,out v)?v as System.Collections.IEnumerable:null;return e==null?Enumerable.Empty<D>():e.OfType<D>();}
    static int? Level(D d,string key){int v;return int.TryParse(Val(d,key),out v)&&v!=255?(int?)v:null;}
    static string CleanMac(string mac){return mac.Replace('-',':').ToUpperInvariant();}
    public Device ByMac(string mac){Device d;return Macs.TryGetValue(CleanMac(mac),out d)?d:null;}
    void Attach(Device d,string mac){mac=CleanMac(mac);if(mac.Length!=17||mac=="00:00:00:00:00:00")return;Macs[mac]=d;if(d.Mac=="")d.Mac=mac;}
    public void Mesh(string json){
        var serializer=new JavaScriptSerializer(){MaxJsonLength=32000000};
        var doc=serializer.Deserialize<D>(json);var byInterface=new Dictionary<string,Device>();var ifaces=new List<D>();var radios=new Dictionary<string,string>();
        foreach(var n in Rows(doc,"nodes")){
            var name=Val(n,"device_friendly_name");if(name=="")name=Val(n,"device_name");
            var d=new Device(name==""?"Unbenannt":name){Role=Val(n,"mesh_role"),Kind=Val(n,"device_class")};Devices.Add(d);d.MeshId=Val(n,"uid");d.Friendly=!String.IsNullOrWhiteSpace(Val(n,"device_friendly_name"));Attach(d,Val(n,"device_mac_address"));
            foreach(var i in Rows(n,"node_interfaces")){byInterface[Val(i,"uid")]=d;Attach(d,Val(i,"mac_address"));ifaces.Add(i);radios[Val(i,"uid")]=RadioInfo(i);}
            d.Note("Mesh · Routerdaten",serializer.Serialize(n));
        }
        var seen=new HashSet<string>();
        foreach(var i in ifaces)foreach(var l in Rows(i,"node_links")){
            if(Val(l,"state")!="CONNECTED")continue;
            string a=Val(l,"node_interface_1_uid"),b=Val(l,"node_interface_2_uid"),id=Val(l,"uid");
            Device x,y;if(!byInterface.TryGetValue(a,out x)||!byInterface.TryGetValue(b,out y)||x==y||!seen.Add(id==""?a+"|"+b:id))continue;
            var edge=new Link(){A=x,B=y,Type=Val(l,"type"),Rx=Level(l,"rx_rsni"),Tx=Level(l,"tx_rsni")};
            edge.Radio=string.Join(" / ",new[]{radios[a],radios[b]}.Where(v=>v!="").Distinct());edge.PortKey=String.CompareOrdinal(a,b)<0?a+"|"+b:b+"|"+a;Links.Add(edge);x.Linked=y.Linked=true;
            string detail=x.Name+" → "+y.Name+" · "+edge.Label+" (Messort in dieser Reihenfolge)";
            x.Note("Verbindung",detail);y.Note("Verbindung",detail);
        }
    }
    static string RadioInfo(D i){
        int ch,freq=0;int.TryParse(Val(i,"current_channel"),out ch);
        object info;if(i.TryGetValue("current_channel_info",out info)&&info is D)int.TryParse(Val((D)info,"primary_freq"),out freq);
        string band=freq>=5925000&&freq<=7125000?"6 GHz":freq>=4900000&&freq<5925000?"5 GHz":freq>=2400000&&freq<=2500000?"2,4 GHz":"";
        return string.Join(" · ",new[]{band,ch>0?"Kanal "+ch:""}.Where(v=>v!=""));
    }
    public Device Host(string mac,string ip,string name){
        var d=ByMac(mac);
        if(d==null){d=new Device(name==""?(ip==""?"Unbenannt":ip):name);Devices.Add(d);Attach(d,mac);}
        if(!d.Friendly&&!String.IsNullOrWhiteSpace(name))d.Name=name;
        if(ip!="")d.IP=ip;
        return d;
    }
    public void HostList(string xml){
        foreach(var n in XDocument.Parse(xml).Descendants().Where(e=>e.Name.LocalName=="Item")){
            var d=Host(Reader.Field(n,"MACAddress"),Reader.Field(n,"IPAddress"),Reader.Field(n,"HostName"));
            string active=Reader.Field(n,"Active");
            d.HostStatus=active=="1"||active=="true"?"aktiv":active=="0"||active=="false"?"inaktiv":"";
            d.Note("Geräteliste der FRITZ!Box",string.Join("\r\n",n.Elements().Select(e=>e.Name.LocalName+": "+e.Value)));
        }
    }
    public void LocalCache(){
        using(var proc=new Process()){proc.StartInfo=new ProcessStartInfo(System.IO.Path.Combine(Environment.SystemDirectory,"arp.exe"),"-a"){CreateNoWindow=true,UseShellExecute=false,RedirectStandardOutput=true};proc.Start();
            var text=proc.StandardOutput.ReadToEnd();proc.WaitForExit();
            foreach(Match m in Regex.Matches(text,@"(?m)^\s*(192\.168\.178\.\d+)\s+([\da-fA-F]{2}(?:-[\da-fA-F]{2}){5})\s+")){
                var mac=m.Groups[2].Value;if((Convert.ToByte(mac.Substring(0,2),16)&1)!=0)continue;
                var d=ByMac(mac);if(d==null)d=Host(mac,m.Groups[1].Value,"");
                d.Note("Windows-Nachbarcache",m.Groups[1].Value+" · "+mac+" (gespeicherte Beobachtung)");
            }
        }
    }
}
class Reader {
    Uri router = new Uri("http://fritz.box:49000/");
    CredentialCache auth = new CredentialCache();
    public static string SingleUser(XDocument doc) {
        var users=doc.Descendants("Users").Elements("User").Select(x=>x.Value.Trim()).Where(x=>x.Length>0).Distinct().ToArray();
        if(users.Length!=1) throw new InvalidOperationException("Benutzer nicht eindeutig. Bitte den Namen unter System > FRITZ!Box-Benutzer nachsehen und eintragen.");
        return users[0];
    }
    static string DiscoverUser() {
        var request=(HttpWebRequest)WebRequest.Create("http://fritz.box/login_sid.lua?version=2");
        request.AllowAutoRedirect=false; request.Proxy=null; request.Timeout=8000; request.ReadWriteTimeout=8000;
        using(var response=(HttpWebResponse)request.GetResponse()) {
            if(response.StatusCode!=HttpStatusCode.OK) throw new InvalidOperationException("Automatische Benutzerermittlung nicht verfügbar. Bitte Benutzernamen eintragen.");
            using(var stream=response.GetResponseStream()) return SingleUser(XDocument.Load(stream));
        }
    }
    public Reader(string host,string user,string pass){router=new Uri("http://"+host+":49000/");username=user;password=pass;}
    string Request(string path,string action=null,string service=null,XElement argument=null) {
        Uri uri=new Uri(router,path);
        if(uri.GetLeftPart(UriPartial.Authority)!=router.GetLeftPart(UriPartial.Authority)) throw new Exception("Unerwartete Routeradresse.");
        var req=(HttpWebRequest)WebRequest.Create(uri);
        req.Credentials=auth; req.Proxy=null; req.AllowAutoRedirect=false;
        req.Timeout=12000; req.ReadWriteTimeout=12000;
        if(action!=null) {
            req.Method="POST"; req.ContentType="text/xml; charset=utf-8";
            req.Headers["SOAPAction"]="\""+service+"#"+action+"\"";
            XNamespace env="http://schemas.xmlsoap.org/soap/envelope/";
            var body=new XElement(env+"Envelope",new XElement(env+"Body",new XElement(XName.Get(action,service),argument)));
            var bytes=Encoding.UTF8.GetBytes(body.ToString()); req.ContentLength=bytes.Length;
            using(var stream=req.GetRequestStream()) stream.Write(bytes,0,bytes.Length);
        }
        using(var res=req.GetResponse())
        using(var stream=new StreamReader(res.GetResponseStream())) return stream.ReadToEnd();
    }
    public static string Field(XElement node,string name) {
        return (string)node.Elements().FirstOrDefault(x=>x.Name.LocalName==name)??"";
    }
    public void Collect(Inventory inv,bool master,Device target,bool meshOnly=false) {
        var doc=XDocument.Parse(Request("/tr64desc.xml"));
        string model=doc.Descendants().First(x=>x.Name.LocalName=="modelName").Value;
        if(!master && model.IndexOf("Repeater",StringComparison.OrdinalIgnoreCase)<0)throw new Exception("Gerätetyp nicht als Repeater bestätigt");
        var services=doc.Descendants().Where(x=>x.Name.LocalName=="service").ToList();
        var hosts=services.FirstOrDefault(x=>Field(x,"serviceType").Contains(":Hosts:"));
        Func<string,string> list=kind=>{
            if(hosts==null)throw new Exception("Hosts-Dienst fehlt");
            string action="X_AVM-DE_Get"+kind+"ListPath";
            var response=XDocument.Parse(Request(Field(hosts,"controlURL"),action,Field(hosts,"serviceType")));
            return Request(response.Descendants().First(x=>x.Name.LocalName=="NewX_AVM-DE_"+kind+"ListPath").Value);
        };
        if(master){
            if(String.IsNullOrWhiteSpace(username))username=DiscoverUser();
            auth.Add(router,"Digest",new NetworkCredential(username,password));
            if(meshOnly){inv.Mesh(list("Mesh"));if(!inv.Devices.Any(n=>n.Role=="master"))throw new IOException("Mesh-Master fehlt");return;}
            try{inv.Mesh(list("Mesh"));}catch(Exception e){inv.Messages.Add("Mesh: "+e.Message);}
            try{inv.HostList(list("Host"));}catch(Exception e){inv.Messages.Add("Geräteliste: "+e.Message);}
            target=inv.Devices.FirstOrDefault(n=>n.Role=="master");
        } else if(!String.IsNullOrWhiteSpace(username))auth.Add(router,"Digest",new NetworkCredential(username,password));
        if(target==null){target=new Device(model);inv.Devices.Add(target);}
        target.Model=model;
        target.Note("Beschreibung · "+router.Host,model);
        foreach(var svc in services){
            string type=Field(svc,"serviceType");
            if(master && type.Contains(":X_AVM-DE_Homeauto:")){try{SmartDevices(inv,svc);}catch(Exception e){inv.Messages.Add("Smarthome-Liste: "+e.Message);}continue;}
            if(!(type.Contains(":DeviceInfo:")||type.Contains(":WLANConfiguration:")))continue;
            try{
                var reply=XDocument.Parse(Request(Field(svc,"controlURL"),"GetInfo",type));
                if(type.Contains(":WLANConfiguration:")) Stations(inv,target,svc,type);
                target.Note(type,string.Join("\r\n",reply.Descendants().Where(x=>!x.HasElements).Select(x=>x.Name.LocalName+": "+x.Value)));
            }catch(Exception e){inv.Messages.Add(router.Host+" · "+type+" · "+e.Message);}
        }
    }
    void SmartDevices(Inventory inv,XElement service){
        string type=Field(service,"serviceType");
        for(int index=0;index<512;index++){
            XDocument reply;
            try{reply=XDocument.Parse(Request(Field(service,"controlURL"),"GetGenericDeviceInfos",type,new XElement("NewIndex",index)));}
            catch(WebException ex){
                var response=ex.Response;
                if(response!=null)using(response)using(var stream=response.GetResponseStream()){
                    var fault=XDocument.Load(stream);
                    if(fault.Descendants().Any(x=>x.Name.LocalName=="errorCode"&&(x.Value=="713"||x.Value=="714"||x.Value=="600")))return;
                }
                throw;
            }
            var fields=reply.Descendants().Where(x=>!x.HasElements).ToList();
            string name=fields.Where(x=>x.Name.LocalName=="NewDeviceName").Select(x=>x.Value).FirstOrDefault();
            var device=new Device(String.IsNullOrWhiteSpace(name)?"FRITZ!-Smarthome-Gerät":name);
            device.Kind="smarthome";device.Note("FRITZ!-Smarthome-Liste",string.Join("\r\n",fields.Select(x=>x.Name.LocalName+": "+x.Value)));
            inv.Devices.Add(device);
        }
    }
    void Stations(Inventory inv,Device ap,XElement svc,string type){
        string url=Field(svc,"controlURL");
        var count=XDocument.Parse(Request(url,"GetTotalAssociations",type)).Descendants().First(n=>n.Name.LocalName=="NewTotalAssociations");
        int total=int.Parse(count.Value);
        for(int j=0;j<total;j++){
            var record=XDocument.Parse(Request(url,"GetGenericAssociatedDeviceInfo",type,new XElement("NewAssociatedDeviceIndex",j)));
            var fields=record.Descendants().Where(n=>!n.HasElements).ToDictionary(n=>n.Name.LocalName,n=>n.Value);
            string mac,ip; if(!fields.TryGetValue("NewAssociatedDeviceMACAddress",out mac)||mac=="")continue;
            fields.TryGetValue("NewAssociatedDeviceIPAddress",out ip);
            var client=inv.Host(mac,ip??"","");
            client.Note("WLAN-Stationsliste · "+ap.Name,string.Join("\r\n",fields.Select(f=>f.Key+": "+f.Value)));
            if(client!=ap&&!inv.Links.Any(e=>(e.A==ap&&e.B==client)||(e.B==ap&&e.A==client))){
                inv.Links.Add(new Link(){A=ap,B=client,Type="WLAN"});ap.Linked=client.Linked=true;
            }
        }
    }
    string username,password;
    public static Inventory Scan(string user,string pass){
        var inv=new Inventory();var reader=new Reader("fritz.box",user,pass);
        try{reader.Collect(inv,true,null);}catch(Exception e){inv.Messages.Add("FRITZ!Box: "+e.Message);}
        foreach(var d in inv.Devices.Where(d=>d.Linked && (d.Role=="slave"||d.Name.IndexOf("Repeater",StringComparison.OrdinalIgnoreCase)>=0) && new[]{"192.168.178.25","192.168.178.23","192.168.178.54"}.Contains(d.IP)).ToArray()){
            try{new Reader(d.IP,reader.username,pass).Collect(inv,false,d);}catch(Exception e){inv.Messages.Add(d.Name+" · "+e.Message);}
        }
        try{inv.LocalCache();}catch(Exception e){inv.Messages.Add("Nachbarcache: "+e.Message);}
        return inv;
    }
}class Chart:ScrollableControl {
    public Inventory Data=new Inventory();
    public Action<Device> Selected;
    float zoom=1,layoutScale=1;
    static readonly Color Ink=Color.FromArgb(34,55,65);
    public Chart(){DoubleBuffered=true;BackColor=Color.White;AutoScroll=true;Dock=DockStyle.Fill;}
    sealed class Branch {
        public Device Item;
        public Link Incoming;
        public List<Branch> Children=new List<Branch>();
        public int Weight=1;
    }

    static string OrderKey(Device d){
        return d.Name+"|"+d.Mac+"|"+d.IP;
    }

    // The parent edge fixes the radius. Each subtree inherits an angular sector.
    static void Place(Branch parent,double start,double span){
        int total=parent.Children.Sum(child=>child.Weight);
        foreach(var child in parent.Children){
            double slice=span*child.Weight/total;
            double direction=start+slice/2;
            double length=child.Incoming.Length;
            child.Item.Pos=new PointF(
                parent.Item.Pos.X+(float)(length*Math.Cos(direction)),
                parent.Item.Pos.Y+(float)(length*Math.Sin(direction)));
            double fan=Math.Min(slice,Math.PI*.90);
            Place(child,direction-fan/2,fan);
            start+=slice;
        }
    }

    public void SetData(Inventory data){
        selection=null;
        Data=data;
        var adjacency=data.Devices.ToDictionary(d=>d,d=>new List<Link>());
        foreach(var link in data.Links){
            adjacency[link.A].Add(link);
            adjacency[link.B].Add(link);
        }
        var roots=data.Devices.Where(d=>adjacency[d].Count>0 || d.Role=="master")
            .OrderBy(d=>d.Role=="master"?0:1).ThenBy(OrderKey,StringComparer.Ordinal);
        var placed=new HashSet<Device>();
        float nextX=0,bottom=0;
        foreach(var device in roots){
            if(!placed.Add(device))continue;
            var root=new Branch{Item=device};
            var queue=new List<Branch>{root};
            for(int index=0;index<queue.Count;index++){
                var parent=queue[index];
                var edges=adjacency[parent.Item].Select(e=>new {Edge=e,Other=e.A==parent.Item?e.B:e.A})
                    .OrderBy(p=>p.Other.Role=="slave"?0:1).ThenBy(p=>OrderKey(p.Other),StringComparer.Ordinal)
                    .ThenByDescending(p=>p.Edge.Rx.HasValue||p.Edge.Tx.HasValue).ThenBy(p=>p.Edge.Length);
                foreach(var pair in edges){
                    if(!placed.Add(pair.Other))continue;
                    var child=new Branch{Item=pair.Other,Incoming=pair.Edge};
                    parent.Children.Add(child);
                    queue.Add(child);
                }
            }
            for(int i=queue.Count-1;i>=0;i--)
                queue[i].Weight=Math.Max(1,queue[i].Children.Sum(c=>c.Weight));
            device.Pos=PointF.Empty;
            Place(root,-Math.PI/2,2*Math.PI);
            float minX=queue.Min(n=>n.Item.Pos.X),minY=queue.Min(n=>n.Item.Pos.Y);
            foreach(var branch in queue){
                branch.Item.Pos.X+=nextX-minX+120;
                branch.Item.Pos.Y+=55-minY;
            }
            nextX=queue.Max(n=>n.Item.Pos.X)+120;
            bottom=Math.Max(bottom,queue.Max(n=>n.Item.Pos.Y));
        }
        // Uniform enlargement preserves relative lengths, including across branches.
        var connected=placed.ToList();
        double closest=double.MaxValue;
        for(int i=0;i<connected.Count;i++)
            for(int j=0;j<i;j++){
                double dx=connected[i].Pos.X-connected[j].Pos.X,dy=connected[i].Pos.Y-connected[j].Pos.Y;
                closest=Math.Min(closest,dx*dx+dy*dy);
            }
        float scale=(float)Math.Max(1,44/Math.Sqrt(Math.Max(closest,1)));
        layoutScale=scale;
        foreach(var d in connected)d.Pos=new PointF(d.Pos.X*scale,d.Pos.Y*scale);
        radialDevices=placed;
        gridBoundary=data.Devices.Count>placed.Count?bottom*scale+70:float.PositiveInfinity;
        int slot=0;
        foreach(var d in data.Devices.Where(d=>!placed.Contains(d)).OrderBy(OrderKey,StringComparer.Ordinal)){
            d.Pos=new PointF(120+slot%5*205,bottom*scale+105+slot/5*80);
            slot++;
        }
        AutoScrollPosition=Point.Empty;
        ResizeMap();
        var master=data.Devices.FirstOrDefault(d=>d.Role=="master");
        if(master!=null)AutoScrollPosition=new Point(Math.Max(0,(int)(master.Pos.X*zoom)-ClientSize.Width/2),
            Math.Max(0,(int)(master.Pos.Y*zoom)-ClientSize.Height/2));
    }
    public void Zoom(float factor){zoom=Math.Min(2,Math.Max(.25f,zoom*factor));ResizeMap();}
    void ResizeMap(){
        AutoScrollMinSize=Data.Devices.Count==0?Size.Empty:new Size((int)((Data.Devices.Max(n=>n.Pos.X)+120)*zoom),(int)((Data.Devices.Max(n=>n.Pos.Y)+55)*zoom));Invalidate();
    }
    static string Glyph(Device d){
        string id=(d.Kind+" "+d.Name).ToLowerInvariant();
        if(d.Role=="master"||id.Contains("fritz!box"))return "\uE968";
        if(d.Role=="slave"||id.Contains("repeater"))return "\uE701";
        if(id.Contains("smarthome"))return "\uE80F";
        if(id.Contains("print")||id.Contains("brother"))return "\uE749";
        if(id.Contains("speaker")||id.Contains("audio")||id.Contains("mini"))return "\uE767";
        if(id.Contains("phone")||id.Contains("mobile"))return "\uE8EA";
        if(id.Contains("tablet"))return "\uE70A";
        if(id.Contains("tv")||id.Contains("cast")||id.Contains("display"))return "\uE7F4";
        if(id.Contains("computer")||id.Contains("desktop")||id.Contains("laptop"))return "\uE7F8";
        return "\uE839";
    }
    HashSet<Device> radialDevices=new HashSet<Device>();
    float gridBoundary;
    Device selection;
    public void SelectDevice(Device device){
        selection=device;
        Invalidate();
    }
    static void Caption(Graphics g,string text,Font font,float x,float top, StringFormat format,bool muted=false){
        SizeF size=g.MeasureString(text,font);
        float width=Math.Min(188,(float)Math.Ceiling(size.Width)+8);
        var rect=new RectangleF(x-width/2,top,width,(float)Math.Ceiling(size.Height)+4);
        g.FillRectangle(Brushes.White,rect);
        g.DrawRectangle(Pens.LightSlateGray,rect.X,rect.Y,rect.Width,rect.Height);
        g.DrawString(text,font,muted?Brushes.DimGray:Brushes.DarkSlateGray,rect,format);
    }
    void DrawZones(Graphics g){
        if(selection==null || !radialDevices.Contains(selection))return;
        var state=g.Save();
        if(!float.IsPositiveInfinity(gridBoundary)){
            var clip=g.VisibleClipBounds;
            clip.Height=Math.Max(0,Math.Min(clip.Bottom,gridBoundary)-clip.Top);
            g.SetClip(clip,CombineMode.Intersect);
        }
        var p=selection.Pos;
        Zone(g,p,Link.DistanceForSnr(-30)*layoutScale,Color.FromArgb(252,230,231),Color.FromArgb(195,100,105));
        Zone(g,p,Link.DistanceForSnr(20)*layoutScale,Color.FromArgb(255,239,184),Color.FromArgb(194,153,52));
        Zone(g,p,Link.DistanceForSnr(25)*layoutScale,Color.FromArgb(221,242,225),Color.FromArgb(78,151,100));
        g.Restore(state);
    }
    static void Zone(Graphics g,PointF center,float radius,Color fill,Color edge){
        var box=new RectangleF(center.X-radius,center.Y-radius,radius*2,radius*2);
        using(var brush=new SolidBrush(fill))g.FillEllipse(brush,box);
        using(var pen=new Pen(edge,1.3f))g.DrawEllipse(pen,box);
    }
    protected override void OnPaint(PaintEventArgs ev){
        base.OnPaint(ev);
        var g=ev.Graphics;
        g.SmoothingMode=SmoothingMode.AntiAlias;
        g.TranslateTransform(AutoScrollPosition.X,AutoScrollPosition.Y);
        g.ScaleTransform(zoom,zoom);
        DrawZones(g);
        var links=Data.Links.Where(e=>e.A==selection||e.B==selection).ToList();
        var labeled=new HashSet<Device>{selection};
        using(var solid=new Pen(Color.SlateGray,1.3f))
        using(var wifi=new Pen(Color.Teal,1.4f))
        using(var icon=new Font("Segoe MDL2 Assets",15))
        using(var nameFont=new Font("Segoe UI",7.5f))
        using(var linkFont=new Font("Segoe UI",7f))
        using(var centered=new StringFormat(){Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center,Trimming=StringTrimming.EllipsisCharacter,FormatFlags=StringFormatFlags.NoWrap}){
            wifi.DashPattern=new float[]{2f,1.5f};
            foreach(var e in links){
                wifi.Color=e.A.Inactive||e.B.Inactive?Color.Silver:Color.Teal;
                solid.Color=e.A.Inactive||e.B.Inactive?Color.Silver:Color.SlateGray;
                g.DrawLine(e.Type=="WLAN"?wifi:solid,e.A.Pos,e.B.Pos);
                labeled.Add(e.A);labeled.Add(e.B);
            }
            foreach(var group in links.GroupBy(e=>new PointF((e.A.Pos.X+e.B.Pos.X)/2,(e.A.Pos.Y+e.B.Pos.Y)/2))){
                float top=group.Key.Y;
                foreach(var e in group){Caption(g,e.Label,linkFont,group.Key.X,top,centered,e.A.Inactive||e.B.Inactive);top+=linkFont.GetHeight(g)+7;}
            }
            foreach(var n in Data.Devices){
                var p=n.Pos;
                if(n==selection)g.DrawEllipse(Pens.DarkSlateGray,p.X-20,p.Y-20,40,40);
                g.FillEllipse(n.Inactive?Brushes.Silver:n.Linked?Brushes.Teal:Brushes.SlateGray,p.X-17,p.Y-17,34,34);
                g.DrawString(Glyph(n),icon,Brushes.White,new RectangleF(p.X-17,p.Y-17,34,34),centered);
                if((!n.Linked && n.Role!="master") || labeled.Contains(n))Caption(g,n.Name,nameFont,p.X,p.Y+21,centered,n.Inactive);
            }
        }
    }
    readonly ToolTip radioTip=new ToolTip(); string lastTip="";
    protected override void Dispose(bool disposing){if(disposing)radioTip.Dispose();base.Dispose(disposing);}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);
        float x=(e.X-AutoScrollPosition.X)/zoom,y=(e.Y-AutoScrollPosition.Y)/zoom;
        var hits=new List<string>();foreach(var l in Data.Links.Where(v=>v.Type=="WLAN"&&(v.A==selection||v.B==selection))){
            var a=l.A.Pos;var b=l.B.Pos;float dx=b.X-a.X,dy=b.Y-a.Y,len=dx*dx+dy*dy;if(len==0)continue;
            float t=Math.Max(0,Math.Min(1,((x-a.X)*dx+(y-a.Y)*dy)/len));
            if(Math.Pow(x-a.X-t*dx,2)+Math.Pow(y-a.Y-t*dy,2)<=36/(zoom*zoom))hits.Add(l.A.Name+" ↔ "+l.B.Name+" · "+(l.Radio==""?"Funkdetails unbekannt":l.Radio));}
        string value=string.Join(Environment.NewLine,hits.Distinct());if(value!=lastTip){lastTip=value;radioTip.SetToolTip(this,value);}
    }
    protected override void OnMouseClick(MouseEventArgs e){base.OnMouseClick(e);
        float x=(e.X-AutoScrollPosition.X)/zoom,y=(e.Y-AutoScrollPosition.Y)/zoom;
        var n=Data.Devices.FirstOrDefault(d=>Math.Pow(x-d.Pos.X,2)+Math.Pow(y-d.Pos.Y,2)<=18*18);
        if(n!=null && Selected!=null)Selected(n);
    }
}
partial class Atlas:Form {
    Chart map=new Chart(); TextBox info=new TextBox(){Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Vertical,Dock=DockStyle.Fill};
    TextBox user=new TextBox(),password=new TextBox(){UseSystemPasswordChar=true};
    Button scan=new Button(){Text="Netz erfassen",AutoSize=true};
    Label status=new Label(){AutoSize=true,Text="Bereit"};
    ListBox list=new ListBox(){Dock=DockStyle.Fill};
    public Atlas(){
        Text="HomeCheck · WLAN-Atlas";Width=1280;Height=830;
        var bar=new FlowLayoutPanel(){Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(7)};
        var minus=new Button(){Text="−",Width=35};var plus=new Button(){Text="+",Width=35};
        bar.Controls.AddRange(new Control[]{new Label(){Text="Benutzer (optional)",AutoSize=true},user,new Label(){Text="Kennwort",AutoSize=true},password,scan,minus,plus,status});
        foreach(Control field in bar.Controls){field.Anchor=AnchorStyles.Left;field.Margin=new Padding(4,4,4,4);}
        user.Width=140;password.Width=140;
        var split=new SplitContainer(){Dock=DockStyle.Fill,Width=1200,SplitterDistance=840};
        var side=new SplitContainer(){Dock=DockStyle.Fill,Orientation=Orientation.Horizontal,Height=640,SplitterDistance=240};
        side.Panel1.Controls.Add(list);side.Panel2.Controls.Add(info);split.Panel2.Controls.Add(side);split.Panel1.Controls.Add(map);
        Controls.Add(split);Controls.Add(bar);
        var legend=new Label(){Dock=DockStyle.Bottom,Height=42,Text="SNR: Grün ab 25 dB · Gelb 20 bis unter 25 dB · Rot unter 20 dB. Vergleich für gemessene WLAN-Baumlinien.\r\nHellgrau: laut FRITZ!Box inaktiv. k. A.: kein Messwert."};Controls.Add(legend);
        info.Text="Kennwort der FRITZ!Box eingeben und Netz erfassen. Die Karte zeigt die von der Infrastruktur gemeldeten Geräte.";
        minus.Click+=(s,e)=>map.Zoom(.8f);plus.Click+=(s,e)=>map.Zoom(1.25f);
        list.DrawMode=DrawMode.OwnerDrawFixed;
        list.DrawItem+=(sender,args)=>{
            if(args.Index<0)return;
            Device device=(Device)list.Items[args.Index];
            bool selected=(args.State&DrawItemState.Selected)!=0;
            args.DrawBackground();
            Color color=device.Inactive?(selected?Color.Gainsboro:Color.DimGray):args.ForeColor;
            TextRenderer.DrawText(args.Graphics,device.ToString()+(device.Inactive?"  · inaktiv":""),args.Font,args.Bounds,color,
                TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis);
            args.DrawFocusRectangle();
        };
        list.SelectedIndexChanged+=(s,e)=>{var item=list.SelectedItem as Device;map.SelectDevice(item);if(item!=null)ShowDetails(item);else info.Clear();UpdateSummary(item);ChooseHistory(item);};
        map.Selected=n=>list.SelectedItem=n;
        scan.Click+=async(s,e)=>{
            if(password.Text==""){status.Text="Kennwort erforderlich";return;}
            string u=user.Text.Trim(),pw=password.Text;scan.Enabled=false;status.Text="Lese Router, Repeater und Nachbarcache …";
            try{Inventory data=await Task.Run(()=>Reader.Scan(u,pw));if(IsDisposed)return;
                map.SetData(data);list.Items.Clear();list.DisplayMember="";foreach(var n in data.Devices.OrderBy(n=>n.Name))list.Items.Add(n);
                info.Text=string.Join("\r\n",data.Messages);status.Text=DateTime.Now.ToString("T")+" · "+data.Devices.Count+" Geräte · "+data.Links.Count+" Verbindungen · "+data.Messages.Count+" Hinweise";
                UpdateSummary(null);
                notes.Text=info.Text==""?"Alle abgefragten Quellen ohne gemeldeten Fehler.":info.Text;
            }catch(Exception ex){status.Text="Abfrage fehlgeschlagen";info.Text=ex.Message;notes.Text=ex.Message;summary.Text="Erfassung fehlgeschlagen. Details stehen unter Abfragehinweise.";}
            finally{if(!IsDisposed)scan.Enabled=true;}
        };
        var pages=new TabControl(){Dock=DockStyle.Fill};side.Panel2.Controls.Remove(info);side.Panel2.Controls.Add(pages);
        var details=new TabPage("Gerät");details.Controls.Add(info);pages.TabPages.Add(details);
        var log=new TabPage("Abfragehinweise");log.Controls.Add(notes);pages.TabPages.Add(log);
        var overview=new TabPage("Übersicht"){Padding=new Padding(7)};overview.Controls.Add(summary);pages.TabPages.Add(overview);pages.SelectedTab=overview;InitHistory(bar,pages);summary.Text="Netz erfassen, dann ein Gerät auswählen.";
    }
    readonly RichTextBox summary=new RichTextBox(){Dock=DockStyle.Fill,ReadOnly=true,BackColor=SystemColors.Window,BorderStyle=BorderStyle.None,DetectUrls=false};
    void Heading(string text){
        summary.SelectionStart=summary.TextLength;
        using(var bold=new Font(summary.Font,FontStyle.Bold)){summary.SelectionFont=bold;summary.AppendText(text+"\n");}
        summary.SelectionFont=summary.Font;
    }
    static string FriendlyIssue(string raw){
        var parts=raw.Split(new[]{" · "},StringSplitOptions.None);
        string source=parts.Length>1?parts[0]:raw.Split(':')[0];
        string what=raw.Contains(":WLANConfiguration:")?"WLAN-Daten":raw.Contains(":DeviceInfo:")?"Geräteinformationen":"Daten";
        string reason=raw.Contains("401")||raw.Contains("403")?"Zugriff abgelehnt":raw.Contains("404")?"Dienst nicht gefunden":Regex.IsMatch(raw,"timeout|zeitüberschreitung",RegexOptions.IgnoreCase)?"Zeitlimit erreicht":"Abfrage unvollständig";
        return source+" – "+what+": "+reason;
    }
    void UpdateSummary(Device d){
        summary.Clear();
        summary.SelectionColor=d!=null && d.Inactive?Color.DimGray:SystemColors.WindowText;
        if(d==null){
            Heading("Netzüberblick");
            summary.AppendText(map.Data.Devices.Count+" erfasste Geräte · "+map.Data.Links.Count+" aktive Verbindungen\n");
            summary.AppendText("Wähle ein Gerät für Anschluss und Signalwerte.\n");
        }else{
            Heading(d.Name);
            if(d.Model!="")summary.AppendText("Modell: "+d.Model+"\n");
            if(d.Role=="master"||d.Role=="slave")summary.AppendText("Mesh-Rolle: "+(d.Role=="master"?"Zentrale":"Mesh-Teilnehmer")+"\n");
            if(d.IP!="")summary.AppendText("IP-Adresse: "+d.IP+"\n");
            if(d.Mac!="")summary.AppendText("MAC-Adresse: "+d.Mac+"\n");
            if(d.HostStatus!="")summary.AppendText("Laut Geräteliste: "+d.HostStatus+"\n");
            summary.AppendText("\n");Heading("Direkte Verbindungen");
            var links=map.Data.Links.Where(l=>l.A==d||l.B==d).ToList();
            if(links.Count==0)summary.AppendText("Keine aktive Verbindung gemeldet.\n");
            foreach(var link in links){
                bool first=link.A==d;
                Device peer=first?link.B:link.A;
                summary.SelectionColor=peer.Inactive||d.Inactive?Color.DimGray:SystemColors.WindowText;
                summary.AppendText(peer.Name+" · "+link.Type+(link.Type=="WLAN"?" · "+(link.Radio==""?"Funkdetails unbekannt":link.Radio):"")+"\n");
                if(link.Type=="WLAN"){
                    var local=first?link.Rx:link.Tx;
                    var remote=first?link.Tx:link.Rx;
                    summary.AppendText("   SNR hier: "+FormatDb(local)+" · bei der Gegenstelle: "+FormatDb(remote)+"\n");
                }
            }
        }
        summary.SelectionColor=SystemColors.WindowText;
        summary.AppendText("\n");Heading("Hinweise zur gesamten Erfassung");
        if(map.Data.Messages.Count==0)summary.AppendText("Keine Abfragefehler gemeldet.");
        else summary.AppendText(string.Join("\n",map.Data.Messages.Select(FriendlyIssue).Distinct()));
        summary.Select(0,0);summary.ScrollToCaret();
    }
    static string FormatDb(int? value){return value.HasValue?value.Value+" dB":"unbekannt";}
    TextBox notes=new TextBox(){Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical};
    void ShowDetails(Device d){info.ForeColor=d.Inactive?Color.DimGray:SystemColors.WindowText;info.Text=d.Name+"\r\n"+d.IP+"   "+d.Mac+"\r\n"+d.Kind+"\r\n"+d.Details;}
    [STAThread]static void Main(){Application.EnableVisualStyles();Application.Run(new Atlas());}
}