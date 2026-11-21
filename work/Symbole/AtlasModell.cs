using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml;
using System.Xml.Linq;
partial class Atlas {
    readonly Button modelButton=new Button{Text="Modell abfragen …",Dock=DockStyle.Bottom,Enabled=false};
    bool modelBusy;
    static string UpnpModel(IPAddress ip){
        using(var udp=new UdpClient(AddressFamily.InterNetwork)){
            var remote=new IPEndPoint(ip,1900);
            var packet=Encoding.ASCII.GetBytes("M-SEARCH * HTTP/1.1\r\nHOST: "+ip+":1900\r\nMAN: \"ssdp:discover\"\r\nMX: 1\r\nST: upnp:rootdevice\r\n\r\n");
            udp.Send(packet,packet.Length,remote);var watch=Stopwatch.StartNew();
            while(watch.ElapsedMilliseconds<2200){
                udp.Client.ReceiveTimeout=Math.Max(1,2200-(int)watch.ElapsedMilliseconds);
                var from=new IPEndPoint(IPAddress.Any,0);var datagram=udp.Receive(ref from);if(!ip.Equals(from.Address))continue;string reply=Encoding.ASCII.GetString(datagram);
                var line=reply.Split('\n').FirstOrDefault(v=>v.StartsWith("LOCATION:",StringComparison.OrdinalIgnoreCase));Uri uri;IPAddress reported;
                if(line==null||!Uri.TryCreate(line.Substring(9).Trim(),UriKind.Absolute,out uri)||uri.Scheme!="http"||!IPAddress.TryParse(uri.Host,out reported)||!reported.Equals(ip)||uri.UserInfo!="")continue;
                var req=(HttpWebRequest)WebRequest.Create(uri);req.AllowAutoRedirect=false;req.Proxy=null;req.Timeout=req.ReadWriteTimeout=3500;
                using(var res=req.GetResponse())using(var xr=XmlReader.Create(res.GetResponseStream(),new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=512000})){
                    var root=XDocument.Load(xr).Root;var device=root.Elements().FirstOrDefault(x=>x.Name.LocalName=="device");
                    var m=device==null?null:device.Elements().FirstOrDefault(x=>x.Name.LocalName=="modelName");return m==null?"":m.Value.Trim();
                }
            }return "";
        }
    }
    async Task QueryModel(){
        var d=list.SelectedItem as Device;if(d==null||modelBusy)return;var raw=observed;
        string u=user.Text.Trim(),pw=password.Text;modelBusy=true;modelButton.Enabled=false;status.Text="Modellabfrage läuft …";
        try{string model=await Task.Run(()=>{
            string host=Reader.Address(Reader.CurrentHosts(u,pw),d);if(host==null)throw new IOException("Aktuelle Geräteadresse unbestätigt.");
            var ip=Dns.GetHostAddresses(host).First(x=>x.AddressFamily==AddressFamily.InterNetwork);
            try{return UpnpModel(ip);}catch(SocketException ex){if(ex.SocketErrorCode==SocketError.TimedOut||ex.SocketErrorCode==SocketError.ConnectionReset)return "";throw;}
        });
        if(IsDisposed||raw!=observed)return;d.ReportModel("UPnP-Gerätebeschreibung",model);
        status.Text=d.Name+(model==""?" · kein Modell per UPnP gemeldet":" · "+model);
        }catch(Exception ex){if(!IsDisposed)status.Text="Modellabfrage · "+Reader.Reason(ex);}
        finally{modelBusy=false;if(!IsDisposed){map.Invalidate();list.Invalidate();UpdateSummary(list.SelectedItem as Device);if(list.SelectedItem==d)ShowDetails(d);}}
    }
}
