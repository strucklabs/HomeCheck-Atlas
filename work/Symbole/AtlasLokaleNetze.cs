using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Web.Script.Serialization;

class PcAdapter {
    public int Id{get;set;} public bool Virtual{get;set;} public bool Physical{get;set;}
    public string Name{get;set;} public string Mac{get;set;}
}
class PcAddress {
    public int Id{get;set;} public string Name{get;set;} public string IPAddress{get;set;}
    public string State{get;set;} public string LinkLayerAddress{get;set;} public int PrefixLength{get;set;}
}
class LocalSnapshot {
    public PcAdapter[] Adapters{get;set;} public PcAddress[] Own{get;set;} public PcAddress[] Neighbors{get;set;}
    const string Command=@"$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding($false)
$adapters=@(Get-NetAdapter -IncludeHidden | Select-Object @{n='Id';e={[int]$_.ifIndex}},@{n='Name';e={$_.Name}},@{n='Virtual';e={[bool]$_.Virtual}},@{n='Physical';e={[bool]$_.HardwareInterface}},@{n='Mac';e={$_.MacAddress}})
$own=@(Get-NetIPAddress | Select-Object @{n='Id';e={[int]$_.InterfaceIndex}},@{n='Name';e={$_.InterfaceAlias}},IPAddress,PrefixLength,@{n='State';e={[string]$_.AddressState}})
$neighbors=@(Get-NetNeighbor | Select-Object @{n='Id';e={[int]$_.InterfaceIndex}},@{n='Name';e={$_.InterfaceAlias}},IPAddress,LinkLayerAddress,@{n='State';e={[string]$_.State}})
[pscustomobject]@{Adapters=$adapters;Own=$own;Neighbors=$neighbors} | ConvertTo-Json -Depth 4 -Compress";
    public static LocalSnapshot Read(){
        string powershell=Path.Combine(Environment.SystemDirectory,@"WindowsPowerShell\v1.0\powershell.exe");
        var start=new ProcessStartInfo(powershell,"-NoLogo -NoProfile -NonInteractive -EncodedCommand "+Convert.ToBase64String(Encoding.Unicode.GetBytes(Command))){
            UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8};
        using(var proc=Process.Start(start)){
            var output=proc.StandardOutput.ReadToEndAsync();var errors=proc.StandardError.ReadToEndAsync();
            if(!proc.WaitForExit(22000)){proc.Kill();throw new IOException("Zeitlimit beim Lesen der Windows-Netzwerkdaten.");}
            if(proc.ExitCode!=0)throw new IOException("Windows-Netzwerkdaten: "+errors.Result.Trim());
            return new JavaScriptSerializer{MaxJsonLength=16000000}.Deserialize<LocalSnapshot>(output.Result);
        }
    }
    public PcAdapter Adapter(int id){return Adapters.FirstOrDefault(a=>a.Id==id);}
    public static string Mac(string value){return (value??"").Replace('-',':').ToUpperInvariant();}
    public IEnumerable<PcAddress> UnicastNeighbors(){
        var own=new HashSet<string>(Own.Select(a=>a.Id+"|"+a.IPAddress),StringComparer.OrdinalIgnoreCase);
        foreach(var n in Neighbors){
            IPAddress ip;var mac=Mac(n.LinkLayerAddress);byte first;
            if(!IPAddress.TryParse(n.IPAddress,out ip)||IPAddress.IsLoopback(ip)||ip.IsIPv6Multicast||
                mac.Length!=17||mac=="00:00:00:00:00:00"||!byte.TryParse(mac.Substring(0,2),System.Globalization.NumberStyles.HexNumber,null,out first)||(first&1)!=0||
                own.Contains(n.Id+"|"+n.IPAddress))continue;
            if(ip.AddressFamily==AddressFamily.InterNetwork && (ip.GetAddressBytes()[0]>=224||ip.Equals(IPAddress.Any)))continue;
            yield return n;
        }
    }
    public void ImportHomeNeighbors(Inventory inventory){
        foreach(var n in UnicastNeighbors()){
            var adapter=Adapter(n.Id);IPAddress ip=IPAddress.Parse(n.IPAddress);
            if(adapter==null||adapter.Virtual||!adapter.Physical||ip.AddressFamily!=AddressFamily.InterNetwork)continue;
            var mac=Mac(n.LinkLayerAddress);var device=inventory.ByMac(mac)??inventory.Host(mac,n.IPAddress,"");
            device.Note("Windows-Nachbarcache · "+adapter.Name,n.IPAddress+" · "+mac+" ("+n.State+")");
        }
    }
}
partial class Atlas {
    readonly ListView pcTable=new ListView{View=View.Details,Dock=DockStyle.Fill,FullRowSelect=true,ShowGroups=true,HideSelection=false};
    readonly Label pcStatus=new Label{AutoSize=true,Anchor=AnchorStyles.Left};
    readonly Button pcRefresh=new Button{Text="Lokal aktualisieren",AutoSize=true};
    bool pcRead;int pcVersion;
    static string AddressState(string state){
        switch((state??"").ToLowerInvariant()){
            case "stale":return "Gespeichert";case "reachable":return "Erreichbar";case "permanent":return "Statisch";
            case "preferred":return "Zugewiesen";case "tentative":return "In Prüfung";case "deprecated":return "Veraltet";
            case "unreachable":return "Unerreichbar";default:return state;
        }
    }
    void ShowLocal(LocalSnapshot data){
        pcVersion++;pcRead=data!=null;pcTable.BeginUpdate();
        try{pcTable.Items.Clear();pcTable.Groups.Clear();
            if(data==null){pcStatus.Text="Lokale Daten fehlen. Erneut abrufen möglich.";return;}
            var neighbors=data.UnicastNeighbors().Where(n=>{var a=data.Adapter(n.Id);return a==null||a.Virtual||!a.Physical;}).ToList();
            foreach(var g in data.Own.Concat(neighbors).GroupBy(x=>x.Id).OrderBy(g=>g.First().Name)){
                var a=data.Adapter(g.Key);string name=a==null?g.First().Name:a.Name;
                var group=new ListViewGroup(name+(a==null?"":a.Virtual?" · virtuell":a.Physical?" · physisch":""));pcTable.Groups.Add(group);
                foreach(var row in g){
                    bool own=data.Own.Contains(row);var label=own?"Eigene Adresse":"Nachbar im Cache";
                    var item=new ListViewItem(new[]{label,row.IPAddress+(own?"/"+row.PrefixLength:""),AddressState(row.State),own?(a==null?"":a.Mac):row.LinkLayerAddress},group);
                    pcTable.Items.Add(item);
                }
            }
            pcStatus.Text=data.Own.Length+" eigene Adressen · "+neighbors.Count+" Nachbareinträge";
        }finally{pcTable.EndUpdate();}
    }
    async Task ReadLocalTab(){
        if(!pcRefresh.Enabled)return;int version=pcVersion;pcRefresh.Enabled=false;pcStatus.Text="Lese Windows-Netzwerkdaten …";
        try{var data=await Task.Run(()=>LocalSnapshot.Read());if(!IsDisposed&&version==pcVersion)ShowLocal(data);}
        catch(Exception ex){if(!IsDisposed&&version==pcVersion)pcStatus.Text=ex.Message;}
        finally{if(!IsDisposed)pcRefresh.Enabled=true;}
    }
    void InitLocal(TabControl pages){
        var page=new TabPage("Dieser PC");pcTable.Columns.Add("Eintrag",135);pcTable.Columns.Add("Adresse / Präfix",260);
        pcTable.Columns.Add("Status",105);pcTable.Columns.Add("MAC-Adresse",140);
        var bar=new FlowLayoutPanel{Dock=DockStyle.Top,AutoSize=true};bar.Controls.Add(pcRefresh);bar.Controls.Add(pcStatus);
        page.Controls.Add(pcTable);page.Controls.Add(bar);pages.TabPages.Add(page);
        pcRefresh.Click+=async(o,e)=>await ReadLocalTab();pages.SelectedIndexChanged+=async(o,e)=>{if(pages.SelectedTab==page&&!pcRead)await ReadLocalTab();};
    }
}
