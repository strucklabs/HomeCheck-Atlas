using System;
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
    public Reader(string user,string password) {
        if(String.IsNullOrWhiteSpace(user)) user=DiscoverUser();
        auth.Add(router,"Digest",new NetworkCredential(user,password));
    }
    string Request(string path,string action=null,string service=null) {
        Uri uri=new Uri(router,path);
        if(uri.GetLeftPart(UriPartial.Authority)!=router.GetLeftPart(UriPartial.Authority)) throw new Exception("Unerwartete Routeradresse.");
        var req=(HttpWebRequest)WebRequest.Create(uri);
        req.Credentials=auth; req.Proxy=null; req.AllowAutoRedirect=false;
        req.Timeout=12000; req.ReadWriteTimeout=12000;
        if(action!=null) {
            req.Method="POST"; req.ContentType="text/xml; charset=utf-8";
            req.Headers["SOAPAction"]="\""+service+"#"+action+"\"";
            XNamespace env="http://schemas.xmlsoap.org/soap/envelope/";
            var body=new XElement(env+"Envelope",new XElement(env+"Body",new XElement(XName.Get(action,service))));
            var bytes=Encoding.UTF8.GetBytes(body.ToString()); req.ContentLength=bytes.Length;
            using(var stream=req.GetRequestStream()) stream.Write(bytes,0,bytes.Length);
        }
        using(var res=req.GetResponse())
        using(var stream=new StreamReader(res.GetResponseStream())) return stream.ReadToEnd();
    }
    public static string Field(XElement node,string name) {
        return (string)node.Elements().FirstOrDefault(x=>x.Name.LocalName==name)??"";
    }
    public TreeNode[] Read() {
        var desc=XDocument.Parse(Request("/tr64desc.xml"));
        var hosts=desc.Descendants().First(x=>x.Name.LocalName=="service" && Field(x,"serviceType").Contains(":service:Hosts:"));
        Func<string,string> list=delegate(string kind) {
            string action="X_AVM-DE_Get"+kind+"ListPath";
            var doc=XDocument.Parse(Request(Field(hosts,"controlURL"),action,Field(hosts,"serviceType")));
            string path=doc.Descendants().First(x=>x.Name.LocalName=="NewX_AVM-DE_"+kind+"ListPath").Value;
            return Request(path);
        };
        string mesh=list("Mesh");
        var addresses=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        var hostTree=new TreeNode("Geräteliste der FRITZ!Box");
        try {
            foreach(var item in XDocument.Parse(list("Host")).Descendants().Where(x=>x.Name.LocalName=="Item")) {
                string ip=Field(item,"IPAddress"),mac=Field(item,"MACAddress");
                if(mac!="") addresses[mac]=ip;
                var hostRow=hostTree.Nodes.Add(Field(item,"HostName")+"   "+ip+"   "+mac+"   "+(Field(item,"Active")=="1"?"aktiv":"nicht aktiv"));
                hostRow.Tag=new[]{ "Gerät",Field(item,"HostName"),ip,mac,Field(item,"Active")=="1"?"aktiv":"nicht aktiv","","","",""};
            }
        } catch(Exception e) {hostTree.Text="Geräteliste nicht verfügbar · "+e.GetType().Name;}
        return new[]{Graph(mesh,addresses),hostTree};
    }
    static string Get(D obj,string key) {object v;return obj.TryGetValue(key,out v)?Convert.ToString(v):"";}
    static IEnumerable<D> Rows(D obj,string key) {
        object v; var a=obj.TryGetValue(key,out v)?v as System.Collections.IEnumerable:null;
        return a==null?Enumerable.Empty<D>():a.OfType<D>();
    }
    static string Power(D link,string key) {
        int level;
        return int.TryParse(Get(link,key),out level) && level!=255 ? level.ToString() : "";
    }
    static void AddEdge(Dictionary<int,string> neighbors,int target,string medium,string atTarget,string atParent) {
        string label=medium;
        if(medium=="WLAN") {
            label+=" · Empfang hier: "+(atTarget==""?"k. A.":atTarget+" dBm");
            label+=" · Gegenstelle: "+(atParent==""?"k. A.":atParent+" dBm");
        }
        string previous;
        neighbors[target]=neighbors.TryGetValue(target,out previous)?previous+" / "+label:label;
    }
    public static TreeNode Graph(string json,Dictionary<string,string> addresses) {
        var doc=new JavaScriptSerializer(){MaxJsonLength=16000000}.Deserialize<D>(json);
        var nodes=Rows(doc,"nodes").ToList();
        if(nodes.Count==0) throw new FormatException("Keine auswertbaren Mesh-Knoten.");
        var owner=new Dictionary<string,int>();
        var interfaces=new List<D>();
        var names=new List<string>();
        var neighbors=nodes.Select(n=>new Dictionary<int,string>()).ToArray();
        foreach(var n in nodes) {
            var ips=new HashSet<string>();
            foreach(var i in Rows(n,"node_interfaces")) {
                owner[Get(i,"uid")]=names.Count; interfaces.Add(i);
                string ip;if(addresses.TryGetValue(Get(i,"mac_address"),out ip)&&ip!="") ips.Add(ip);
            }
            names.Add((Get(n,"device_name")==""?"Unbenannt":Get(n,"device_name"))+"  "+String.Join(", ",ips));
        }
        var iface=interfaces.ToDictionary(x=>Get(x,"uid"));
        var rows=new List<string[]>();
        var seen=new HashSet<string>();
        foreach(var i in interfaces)
            foreach(var l in Rows(i,"node_links")) {
                if(Get(l,"state")!="CONNECTED") continue;
                int a,b;
                if(!owner.TryGetValue(Get(l,"node_interface_1_uid"),out a)||!owner.TryGetValue(Get(l,"node_interface_2_uid"),out b)||a==b) continue;
                string one=Get(l,"node_interface_1_uid"), two=Get(l,"node_interface_2_uid");
                string key=Get(l,"uid"); if(!seen.Add(key==""?one+"|"+two:key)) continue;
                string medium=Get(l,"type"); if(medium=="")medium=Get(i,"type");
                string first=medium=="WLAN"?Power(l,"rx_rcpi"):"";
                string second=medium=="WLAN"?Power(l,"tx_rcpi"):"";
                string mac=Get(iface[one],"mac_address"),ip;
                addresses.TryGetValue(mac,out ip);
                rows.Add(new[]{"Verbindung",Get(nodes[a],"device_name"),ip??"",mac,"verbunden",names[b].Trim(),medium,first,second});
                AddEdge(neighbors[a],b,medium,second,first);
                AddEdge(neighbors[b],a,medium,first,second);
            }
        var tree=new TreeNode("Gemeldete Mesh-Verbindungen");
        tree.Tag=rows;
        var visited=new HashSet<int>();
        int master=nodes.FindIndex(n=>Get(n,"mesh_role").IndexOf("MASTER",StringComparison.OrdinalIgnoreCase)>=0);
        if(master<0) master=nodes.FindIndex(n=>Get(n,"device_name").Contains("5690"));
        foreach(int start in Enumerable.Range(0,nodes.Count).OrderBy(x=>x==master?0:1)) {
            if(!visited.Add(start)) continue;
            if(neighbors[start].Count==0) rows.Add(new[]{"Mesh-Knoten",names[start].Trim(),"","","keine aktive Zuordnung","","","",""});
            var root=tree.Nodes.Add(names[start]+(neighbors[start].Count==0?" · keine aktive Zuordnung":""));
            var queue=new Queue<Tuple<int,int,TreeNode>>(); queue.Enqueue(Tuple.Create(start,-1,root));
            while(queue.Count>0) {
                var cur=queue.Dequeue();
                foreach(var edge in neighbors[cur.Item1]) {
                    if(edge.Key==cur.Item2) continue;
                    string label=names[edge.Key]+"  "+edge.Value;
                    if(visited.Add(edge.Key)) queue.Enqueue(Tuple.Create(edge.Key,cur.Item1,cur.Item3.Nodes.Add(label)));
                    else cur.Item3.Nodes.Add("Querverbindung · "+label);
                }
            }
        }
        return tree;
    }
}
class MapForm:Form {
    TextBox user=new TextBox(), pass=new TextBox(){UseSystemPasswordChar=true};
    Button read=new Button(){Text="Jetzt abfragen",AutoSize=true};
    CheckBox auto=new CheckBox(){Text="Alle 60 Sekunden",AutoSize=true};
    Label status=new Label(){Dock=DockStyle.Bottom,Height=42};
    TreeView live=new TreeView(){Dock=DockStyle.Fill};
    Button export=new Button(){Text="CSV exportieren …",AutoSize=true,Enabled=false};
    static string Cell(string value) {
        string trimmed=value.TrimStart();
        int numeric;
        if(trimmed.Length>0 && "=+-@".Contains(trimmed[0]) && !int.TryParse(value,out numeric)) value="'"+value;
        return "\""+value.Replace("\"","\"\"")+"\"";
    }
    public static string Csv(TreeNode mesh,TreeNode hosts,string time) {
        var lines=new List<string>{"Zeit;Eintrag;Name;IP;MAC;Status;Gegenstelle;Verbindung;Empfang_Name_dBm;Empfang_Gegenstelle_dBm"};
        var rows=(List<string[]>)mesh.Tag;
        foreach(var row in rows.Concat(hosts.Nodes.Cast<TreeNode>().Select(n=>(string[])n.Tag)))
            lines.Add(string.Join(";",new[]{time}.Concat(row).Select(Cell)));
        if(hosts.Text.StartsWith("Geräteliste nicht verfügbar"))
            lines.Add(string.Join(";",new[]{time,"Abfrage","","","",hosts.Text,"","","",""}.Select(Cell)));
        return string.Join("\r\n",lines)+"\r\n";
    }
    void ExportSnapshot() {
        var content=Csv(live.Nodes[0],live.Nodes[1],last);
        using(var dlg=new SaveFileDialog(){Filter="CSV-Datei (*.csv)|*.csv",FileName="HomeCheck-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".csv"}) {
            if(dlg.ShowDialog(this)!=DialogResult.OK)return;
            string temp=Path.Combine(Path.GetDirectoryName(dlg.FileName),Guid.NewGuid()+".tmp");
            try {
                File.WriteAllText(temp,content,new UTF8Encoding(true));
                if(File.Exists(dlg.FileName)) File.Replace(temp,dlg.FileName,null);
                else File.Move(temp,dlg.FileName);
            } catch(Exception ex){MessageBox.Show(this,ex.Message,"Export fehlgeschlagen");}
            finally {if(File.Exists(temp)) File.Delete(temp);}
        }
    }
    Timer timer=new Timer(){Interval=60000};
    string last="noch keine"; bool busy;
    static void Branch(TreeNode parent,string name,params string[] children) {
        var n=parent.Nodes.Add(name);foreach(string child in children)n.Nodes.Add(child);
    }
    public MapForm() {
        Text="HomeCheck · Netzkarte";Width=1050;Height=730;
        var bar=new FlowLayoutPanel(){Dock=DockStyle.Top,Height=65,AutoSize=true,Padding=new Padding(8)};
        bar.Controls.AddRange(new Control[]{new Label(){Text="Benutzer (optional)",AutoSize=true},user,new Label(){Text="Kennwort",AutoSize=true},pass,read,auto,export});
        var tabs=new TabControl(){Dock=DockStyle.Fill};
        var meshPage=new TabPage("Mesh live");tabs.TabPages.Add(meshPage);meshPage.Controls.Add(live);
        var basis=new TreeView(){Dock=DockStyle.Fill};var known=new TabPage("Bekannter Aufbau");tabs.TabPages.Add(known);known.Controls.Add(basis);
        var router=basis.Nodes.Add("Telekom-Glasfaser → Telekom-Modem → FRITZ!Box 5690 Pro");
        router.Nodes.Add("LAN · Eigener Desktop-PC");
        Branch(router,"WLAN · 450E (.25)","Desktop-PC per LAN","Nest Mini (.53) · zuletzt bestätigt im 2,4-GHz-WLAN");
        var sw=router.Nodes.Add("WLAN · 1750E (.23)").Nodes.Add("LAN · TP-Link TL-SG108E V1 · 7/8 Ports belegt");
        foreach(string n in new[]{"Samsung-TV","Sony-Blu-ray-Player","Steam Link","Wii U","Desktop-PC"})sw.Nodes.Add(n);
        Branch(sw,"Silvercrest-Zentrale","Alle Lampen · Google Home / Lidl-App");
        router.Nodes.Add("WLAN · Repeater 6000 (.54)");
        Branch(router,"FRITZ!Smart Gateway (.43)","Zwei Thermostate · Google Home");
        Branch(basis.Nodes.Add("Weitere WLAN-Geräte"),"Zugangspunkt offen","Nest Audio, Home Mini, zwei Google-Displays","Zwei klassische Chromecasts, Chromecast mit Google TV am Samsung","DEEBOT N8 PRO+, Brother HL-L2350DW (.61)","Zwei Lenovo-Laptops, zwei Lenovo-Tablets, zwei E-Reader und weitere Mobilgeräte");
        basis.ExpandAll();
        Controls.Add(tabs);Controls.Add(bar);Controls.Add(status);
        status.Text="Lokaler Router: fritz.box · TR-064 · Zugangsdaten nur für diese Sitzung.";
        export.Click+=(o,e)=>ExportSnapshot();
        read.Click+=async(o,e)=>await RefreshMap();
        timer.Tick+=async(o,e)=>await RefreshMap();
        auto.CheckedChanged+=async(o,e)=>{timer.Enabled=auto.Checked;if(auto.Checked)await RefreshMap();};
    }
    async Task RefreshMap() {
        if(busy)return;
        if(pass.Text==""){auto.Checked=false;status.Text="Bitte FRITZ!Box-Kennwort eingeben.";return;}
        busy=true;read.Enabled=false;status.Text="Mesh-Daten werden gelesen …";
        try {
            var username=user.Text.Trim(); var password=pass.Text;
            var trees=await Task.Run(()=>new Reader(username,password).Read());
            if(IsDisposed)return;
            last=DateTime.Now.ToString("G");
            live.BeginUpdate();live.Nodes.Clear();live.Nodes.AddRange(trees);trees[0].ExpandAll();live.EndUpdate();export.Enabled=true;
            status.Text="Routerabfrage "+last;
            try {
                var folder=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"HomeCheck");
                Directory.CreateDirectory(folder);
                var lines=new List<string>{"=== "+last+" ==="};
                foreach(var t in trees)Log(t,0,lines);
                File.AppendAllLines(Path.Combine(folder,"Netzkarten-Verlauf.txt"),lines);
            }catch(Exception ex){status.Text+=" · Verlauf nicht gespeichert: "+ex.Message;}
        } catch(Exception ex) {
            if(IsDisposed)return;
            auto.Checked=false;
            status.Text="Abfrage fehlgeschlagen: "+ex.Message+" · Letzte erfolgreiche Abfrage: "+last;
        } finally {busy=false;if(!IsDisposed)read.Enabled=true;}
    }
    static void Log(TreeNode n,int level,List<string> lines){lines.Add(new string(' ',level*2)+n.Text);foreach(TreeNode child in n.Nodes)Log(child,level+1,lines);}
    [STAThread] static void Main(){Application.EnableVisualStyles();Application.Run(new MapForm());}
}