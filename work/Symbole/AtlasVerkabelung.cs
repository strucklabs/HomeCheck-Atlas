using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Web.Script.Serialization;

class CableSettings {
    public Dictionary<string,string> Parents=new Dictionary<string,string>();
    public Dictionary<string,string> Names=new Dictionary<string,string>();
    public static string Id(Device d){return d==null?"":d.Key;}
    public static Dictionary<Device,int> Depth(Inventory inv){
        var result=new Dictionary<Device,int>();var q=new Queue<Device>();
        foreach(var root in inv.Devices.Where(x=>x.Role=="master")){result[root]=0;q.Enqueue(root);}
        while(q.Count>0){var node=q.Dequeue();
            foreach(var edge in inv.Links.Where(x=>x.A==node||x.B==node)){
                var peer=edge.A==node?edge.B:edge.A;if(result.ContainsKey(peer))continue;
                result[peer]=result[node]+1;q.Enqueue(peer);
            }
        }return result;
    }
    static bool Incoming(Device child,Device peer,Dictionary<Device,int> depth){
        int c,p;return !depth.TryGetValue(child,out c)||!depth.TryGetValue(peer,out p)||p<=c;
    }
    public Inventory View(Inventory raw){
        var view=new Inventory();view.Devices.AddRange(raw.Devices);view.Messages.AddRange(raw.Messages);view.Macs=raw.Macs;
        var nodes=raw.Devices.Where(x=>Id(x)!="").GroupBy(Id).Where(g=>g.Count()==1).ToDictionary(g=>g.Key,g=>g.First());
        foreach(string key in Parents.Keys.Concat(Parents.Values).Concat(Names.Keys.Where(x=>x.StartsWith("manual:"))).Distinct()){
            if(nodes.ContainsKey(key)||raw.Devices.Any(x=>Id(x)==key))continue;
            string name;if(!Names.TryGetValue(key,out name))continue;
            bool manual=key.StartsWith("manual:");
            var d=new Device(name){MeshId=key,Kind=manual?"Switch · manuell angelegt":"gespeichertes Gerät",HostStatus=manual?"":"inaktiv"};
            d.Note("Verkabelung",manual?"Manuelle Zwischenstation ohne IP-Adresse.":"In dieser Erfassung nicht gefunden.");nodes[key]=d;view.Devices.Add(d);
        }
        var depth=Depth(raw);
        foreach(var e in raw.Links){
            if(Parents.ContainsKey(Id(e.A))&&Incoming(e.A,e.B,depth)||Parents.ContainsKey(Id(e.B))&&Incoming(e.B,e.A,depth))continue;
            view.Links.Add(e);
        }
        foreach(var pair in Parents){
            Device child,parent;if(!nodes.TryGetValue(pair.Key,out child)||!nodes.TryGetValue(pair.Value,out parent))continue;
            if(child.Role=="master"||child==parent)throw new IOException("Ungültige Zuordnung der Zentrale oder Selbstverbindung.");
            view.Links.RemoveAll(e=>e.A==parent && e.B==child || e.A==child && e.B==parent);
            view.Links.Add(new Link{A=parent,B=child,Type="LAN",Manual=true});
        }
        return view;
    }
    public void Validate(Inventory raw){
        foreach(var k in Parents.Keys){var seen=new HashSet<string>();string id=k;
            while(Parents.ContainsKey(id)){if(!seen.Add(id))throw new IOException("Diese Verkabelung bildet einen Kreis.");id=Parents[id];}}
        var view=View(raw);var before=Depth(raw);var after=Depth(view);
        foreach(var e in view.Links.Where(x=>x.Manual)){int a,b;if(before.TryGetValue(e.A,out a)&&before.TryGetValue(e.B,out b)&&a>b&&!after.ContainsKey(e.B))throw new IOException("Der übergeordnete Anschluss liegt hinter diesem Gerät. Bitte zuerst dessen Anschluss ändern.");}
        foreach(var cable in view.Links.Where(x=>x.Manual)){
            var seen=new HashSet<Device>{cable.A};var q=new Queue<Device>();q.Enqueue(cable.A);
            while(q.Count>0){var d=q.Dequeue();foreach(var edge in view.Links.Where(x=>x!=cable&&(x.A==d||x.B==d))){
                var other=edge.A==d?edge.B:edge.A;if(other==cable.B)throw new IOException("Über diese Verbindung entstünde ein zweiter Pfad. Bitte den übergeordneten Anschluss prüfen.");
                if(seen.Add(other))q.Enqueue(other);
            }}
        }
    }
}
partial class Atlas {
    readonly Button wiringButton=new Button{Text="Verkabelung bearbeiten …",Dock=DockStyle.Bottom,Enabled=false};
    Inventory observed;
    CableSettings wiring=new CableSettings();
    string cablePath,cableVersion;
    static string ReadCables(string path){return File.Exists(path)?File.ReadAllText(path):"";}
    Inventory LoadWiring(Inventory data){
        observed=data;wiringButton.Enabled=false;cablePath=null;wiring=new CableSettings();
        var root=data.Devices.FirstOrDefault(x=>x.Role=="master");if(root==null||root.Key=="")return data;
        string key=BitConverter.ToString(System.Text.Encoding.UTF8.GetBytes(root.Key)).Replace("-","");
        cablePath=Path.Combine(Path.GetDirectoryName(archive),"Verkabelung",key+".json");
        try{cableVersion=ReadCables(cablePath);if(cableVersion!="")wiring=json.Deserialize<CableSettings>(cableVersion);
            var result=wiring.View(data);wiringButton.Enabled=true;return result;
        }catch(Exception e){data.Messages.Add("Verkabelung konnte nicht geladen werden · "+e.Message);return data;}
    }
    void SaveWiring(CableSettings next){
        next.Validate(observed);string text=json.Serialize(next);Directory.CreateDirectory(Path.GetDirectoryName(cablePath));
        using(var guard=new FileStream(cablePath+".lock",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None)){
            if(ReadCables(cablePath)!=cableVersion)throw new IOException("Die Verkabelung wurde in einem anderen Fenster geändert. Bitte das Netz erneut erfassen.");
            string tmp=cablePath+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(tmp,text);if(File.Exists(cablePath))File.Replace(tmp,cablePath,null);else File.Move(tmp,cablePath);}
            finally{if(File.Exists(tmp))File.Delete(tmp);}
        }wiring=next;cableVersion=text;
    }
    void RefreshCables(string key){
        var data=wiring.View(observed);map.SetData(data);list.Items.Clear();
        foreach(var d in data.Devices.OrderBy(n=>n.Name))list.Items.Add(d);
        list.SelectedItem=data.Devices.FirstOrDefault(n=>n.Key==key);UpdateSummary(list.SelectedItem as Device);
        status.Text="Verkabelung gespeichert · "+data.Devices.Count+" Geräte · "+data.Links.Count+" Verbindungen";
    }

    void EditWiring(){
        if(observed==null||cablePath==null)return;
        var items=map.Data.Devices.Where(x=>x.Key!="").GroupBy(x=>x.Key).Where(g=>g.Count()==1).Select(g=>g.First()).OrderBy(x=>x.Name).ToArray();
        using(var form=new Form{Text="Verkabelung bearbeiten",Size=new Size(575,580),MinimumSize=new Size(490,480),StartPosition=FormStartPosition.CenterParent}){
            var panel=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=10,Padding=new Padding(12)};
            var subject=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
            subject.Items.AddRange(items);subject.Items.Add("＋ Zwischenstation ohne IP anlegen");
            var name=new TextBox{Dock=DockStyle.Fill};
            var parent=new ComboBox{Dock=DockStyle.Fill,DropDownStyle=ComboBoxStyle.DropDownList};
            var children=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true,HorizontalScrollbar=true};
            var buttons=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoSize=true,WrapContents=true};
            var save=new Button{Text="Speichern",AutoSize=true};var del=new Button{Text="Station entfernen",AutoSize=true};
            var cancel=new Button{Text="Abbrechen",DialogResult=DialogResult.Cancel,AutoSize=true};
            buttons.Controls.AddRange(new Control[]{save,cancel,del});form.CancelButton=cancel;
            Control[] rows={new Label{Text="Gerät oder neue Zwischenstation",AutoSize=true},subject,new Label{Text="Name der Zwischenstation",AutoSize=true},name,
                new Label{Text="Übergeordnetes Gerät",AutoSize=true},parent,new Label{Text="Manuell zugeordnete Geräte am Ausgang (Mehrfachauswahl)",AutoSize=true},children,
                new Label{Text="Abwählen oder „Automatisch“ stellt die erfasste Verbindung wieder her.",AutoSize=true},buttons};
            for(int j=0;j<rows.Length;j++){panel.RowStyles.Add(j==7?new RowStyle(SizeType.Percent,100):new RowStyle(SizeType.AutoSize));panel.Controls.Add(rows[j],0,j);}
            form.Controls.Add(panel);string newKey="manual:"+Guid.NewGuid().ToString("N");bool loading=false;
            subject.SelectedIndexChanged+=(o,e)=>{
                loading=true;var focus=subject.SelectedItem as Device;var id=focus==null?newKey:focus.Key;
                name.Enabled=focus==null||id.StartsWith("manual:");name.Text=focus==null?"":focus.Name;del.Visible=focus!=null&&id.StartsWith("manual:");
                parent.Items.Clear();parent.Items.Add("Automatisch (laut Erfassung)");parent.SelectedIndex=0;parent.Enabled=focus==null||focus.Role!="master";
                children.Items.Clear();string prior;wiring.Parents.TryGetValue(id,out prior);
                foreach(var v in items.Where(v=>v.Key!=id)){
                    parent.Items.Add(v);if(v.Key==prior)parent.SelectedItem=v;
                    if(v.Role!="master"){string par;children.Items.Add(v,wiring.Parents.TryGetValue(v.Key,out par)&&par==id);}
                }loading=false;
            };
            name.TextChanged+=(o,e)=>{if(!loading)subject.Enabled=false;};
            parent.SelectedIndexChanged+=(o,e)=>{if(!loading)subject.Enabled=false;};
            children.ItemCheck+=(o,e)=>{if(!loading)subject.Enabled=false;};
            var selected=list.SelectedItem as Device;subject.SelectedIndex=Array.IndexOf(items,selected);if(subject.SelectedIndex<0)subject.SelectedIndex=0;
            Action<bool> commit=remove=>{
                var focus=subject.SelectedItem as Device;string id=focus==null?newKey:focus.Key;
                var next=json.Deserialize<CableSettings>(json.Serialize(wiring));
                if(remove){
                    if(MessageBox.Show(form,"Zwischenstation und ihre manuellen Verbindungen entfernen?","Verkabelung",MessageBoxButtons.YesNo)!=DialogResult.Yes)return;
                    next.Names.Remove(id);next.Parents.Remove(id);foreach(var k in next.Parents.Where(entry=>entry.Value==id).Select(entry=>entry.Key).ToList())next.Parents.Remove(k);
                }else{
                    if(name.Enabled&&name.Text.Trim()==""){MessageBox.Show(form,"Bitte der Zwischenstation einen Namen geben.");return;}
                    next.Names[id]=name.Text.Trim();var up=parent.SelectedItem as Device;
                    if(up==null)next.Parents.Remove(id);else{next.Parents[id]=up.Key;next.Names[up.Key]=up.Name;}
                    foreach(Device child in children.Items){string prev;if(next.Parents.TryGetValue(child.Key,out prev)&&prev==id)next.Parents.Remove(child.Key);}
                    foreach(Device child in children.CheckedItems){next.Parents[child.Key]=id;next.Names[child.Key]=child.Name;}
                }
                try{SaveWiring(next);RefreshCables(id);form.Close();}
                catch(Exception ex){MessageBox.Show(form,ex.Message,"Verkabelung");}
            };
            save.Click+=(o,e)=>commit(false);del.Click+=(o,e)=>commit(true);form.ShowDialog(this);
        }
    }
}
