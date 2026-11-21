using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;

class TuyaAnnouncement {
    public string Id="",Version="",Product="";
    static int Word(byte[] a,int n){return (a[n]<<24)|(a[n+1]<<16)|(a[n+2]<<8)|a[n+3];}
    static byte[] Decode(byte[] a){
        using(var md=MD5.Create())using(var aes=Aes.Create()){
            aes.Key=md.ComputeHash(Encoding.ASCII.GetBytes("yGAdlopoPVldABfn"));
            aes.Mode=CipherMode.ECB;aes.Padding=PaddingMode.PKCS7;
            using(var dec=aes.CreateDecryptor())return dec.TransformFinalBlock(a,0,a.Length);
        }
    }
    public static TuyaAnnouncement Parse(byte[] packet){
        if(packet.Length==0||packet.Length>16384)return null;
        if(packet.Length>=50&&Word(packet,0)==0x6699&&Word(packet,packet.Length-4)==0x9966&&Word(packet,14)==packet.Length-22)
            return new TuyaAnnouncement();
        var data=packet;
        if(packet.Length>=24&&Word(packet,0)==0x55aa){
            if(Word(packet,12)!=packet.Length-16||Word(packet,packet.Length-4)!=0xaa55)return null;
            data=packet.Skip(16).Take(packet.Length-24).ToArray();
            if(data.Length>=4&&Word(data,0)==0)data=data.Skip(4).ToArray();
        }
        for(int attempt=0;attempt<2;attempt++){
            try{
                var raw=attempt==0?data:Decode(data);
                string json=Encoding.UTF8.GetString(raw).Trim('\0',' ','\r','\n','\t');
                if(!json.StartsWith("{"))continue;
                var obj=new JavaScriptSerializer().Deserialize<Dictionary<string,object>>(json);
                if(obj==null)continue;
                Func<string,string> get=k=>obj.ContainsKey(k)&&obj[k] is string?(string)obj[k]:"";
                string id=get("gwId");if(id=="")id=get("devId");
                if(id!="")return new TuyaAnnouncement{Id=id,Product=get("productKey"),Version=get("version")};
            }catch(ArgumentException){}catch(InvalidOperationException){}catch(CryptographicException){}
        }
        return null;
    }
}
partial class Atlas {
    void InitTuya(FlowLayoutPanel bar){
        var open=new Button{Text="Tuya-Geräte erkennen …",AutoSize=true};bar.Controls.Add(open);
        open.Click+=(s,e)=>ListenTuya();
    }
    void ListenTuya(){
        using(var dlg=new Form{Text="Tuya-Geräte im Heimnetz",Width=830,Height=325,StartPosition=FormStartPosition.CenterParent})
        using(var tick=new Timer{Interval=200}){
            var table=new ListView{Dock=DockStyle.Fill,View=View.Details,FullRowSelect=true};
            foreach(var title in new[]{"Absender-IP","Name im Atlas","Geräte-ID","Version","Produktkennung","Zuletzt"})table.Columns.Add(title);
            int[] widths={116,160,160,65,145,90};for(int i=0;i<widths.Length;i++)table.Columns[i].Width=widths[i];
            var statusLine=new Label{Dock=DockStyle.Bottom,Height=48,Padding=new Padding(5)};
            dlg.Controls.Add(table);dlg.Controls.Add(statusLine);
            var sockets=new Dictionary<int,UdpClient>();var issues=new List<string>();
            var rows=new Dictionary<string,ListViewItem>();var clock=new Stopwatch();
            Action stop=()=>{tick.Stop();foreach(var sock in sockets.Values)sock.Close();sockets.Clear();};
            dlg.Shown+=(s,e)=>{
                foreach(int port in new[]{6666,6667}){
                    var sock=new UdpClient(AddressFamily.InterNetwork);
                    try{sock.ExclusiveAddressUse=true;sock.Client.Bind(new IPEndPoint(IPAddress.Any,port));sock.Client.Blocking=false;sockets.Add(port,sock);}
                    catch(SocketException ex){sock.Close();issues.Add("UDP "+port+": "+ex.SocketErrorCode);}
                }
                if(sockets.Count==0){statusLine.Text="Empfang nicht verfügbar. "+string.Join(" · ",issues);return;}
                clock.Start();tick.Start();
            };
            tick.Tick+=(s,e)=>{
                foreach(var pair in sockets.ToArray())try{
                    for(int i=0;i<24&&pair.Value.Available>0;i++){
                        var ep=new IPEndPoint(IPAddress.Any,0);var record=TuyaAnnouncement.Parse(pair.Value.Receive(ref ep));if(record==null)continue;
                        string ip=ep.Address.ToString();ListViewItem row;
                        if(!rows.TryGetValue(ip,out row)){
                            var host=map.Data.Devices.FirstOrDefault(d=>d.IP==ip);
                            row=new ListViewItem(new[]{ip,host==null?"—":host.Name,"—","—","—",""});
                            rows.Add(ip,row);table.Items.Add(row);
                        }
                        if(record.Id!="")row.SubItems[2].Text=record.Id;
                        if(record.Version!="")row.SubItems[3].Text=record.Version;
                        if(record.Product!="")row.SubItems[4].Text=record.Product;
                        row.SubItems[5].Text=DateTime.Now.ToString("T");
                    }
                }catch(SocketException ex){if(ex.SocketErrorCode!=SocketError.WouldBlock){issues.Add("UDP "+pair.Key+": "+ex.SocketErrorCode);pair.Value.Close();sockets.Remove(pair.Key);}}
                int left=Math.Max(0,60-(int)clock.Elapsed.TotalSeconds);
                statusLine.Text=(sockets.Count==0?"Empfang beendet.":left>0?"Empfange noch "+left+" s auf UDP "+string.Join(", ",sockets.Keys)+".":rows.Count==0?"In 60 Sekunden keine Tuya-Meldung empfangen.":"Erkennung abgeschlossen. Geräte gefunden: "+rows.Count)+" "+string.Join(" · ",issues);
                if(left==0||sockets.Count==0)stop();
            };
            try{dlg.ShowDialog(this);}finally{stop();}
        }
    }
}
