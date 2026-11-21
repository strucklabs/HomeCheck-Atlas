using System;
using System.Net;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Windows.Forms;
using System.Threading.Tasks;

partial class Reader {
    bool configured;
    void Configure(){
        if(configured)return;
        if(String.IsNullOrWhiteSpace(username))username=DiscoverUser();
        auth.Add(router,"Digest",new NetworkCredential(username,password));configured=true;
    }
    XDocument Description(){
        if(!String.IsNullOrWhiteSpace(username))Configure();
        string xml;
        try{xml=Request("/tr64desc.xml");}
        catch(WebException e){var res=e.Response as HttpWebResponse;if(res==null||res.StatusCode!=HttpStatusCode.Unauthorized||configured)throw;
            res.Close();Configure();xml=Request("/tr64desc.xml");}
        Configure();return XDocument.Parse(xml);
    }
    public static string Reason(Exception e){
        var web=e as WebException;
        if(web!=null){var r=web.Response as HttpWebResponse;if(r!=null){int n=(int)r.StatusCode;r.Close();
            return n==404?"HTTP 404 · Pfad nicht gefunden":n==401?"HTTP 401 · Anmeldung abgewiesen":"HTTP "+n;}
            return "Netzwerkfehler · "+web.Status;}
        if(e is System.Xml.XmlException)return "Antwort enthält keine XML-Dienstbeschreibung oder Geräteliste";
        return e.Message;
    }
    string ProbeTr(){
        var doc=Description();var services=doc.Descendants().Where(x=>x.Name.LocalName=="service").ToList();
        var svc=services.FirstOrDefault(x=>Field(x,"serviceType").Contains(":DeviceInfo:"));
        if(svc==null)return "Dienstbeschreibung gelesen · DeviceInfo fehlt";
        var reply=XDocument.Parse(Request(Field(svc,"controlURL"),"GetInfo",Field(svc,"serviceType")));
        var m=reply.Descendants().FirstOrDefault(x=>x.Name.LocalName=="NewModelName");
        return "GetInfo erfolgreich"+(m==null?"":" · "+m.Value);
    }
    string WebPage(string route,string post=null){
        var req=(HttpWebRequest)WebRequest.Create("http://"+router.Host+route);req.Proxy=null;req.AllowAutoRedirect=false;req.Timeout=req.ReadWriteTimeout=7000;
        if(post!=null){req.Method="POST";req.ContentType="application/x-www-form-urlencoded";var bytes=Encoding.UTF8.GetBytes(post);req.ContentLength=bytes.Length;using(var stream=req.GetRequestStream())stream.Write(bytes,0,bytes.Length);}
        using(var r=(HttpWebResponse)req.GetResponse()){if(r.StatusCode!=HttpStatusCode.OK)throw new IOException("HTTP "+(int)r.StatusCode);using(var stream=new StreamReader(r.GetResponseStream()))return stream.ReadToEnd();}
    }
    static byte[] Hex(string text){if(text.Length%2!=0)throw new FormatException("Ungültige Challenge");return Enumerable.Range(0,text.Length/2).Select(i=>Convert.ToByte(text.Substring(i*2,2),16)).ToArray();}
    static string Hex(byte[] data){return BitConverter.ToString(data).Replace("-","").ToLowerInvariant();}
    static byte[] Derive(byte[] key,string salt,string count){
        int n;if(!int.TryParse(count,out n)||n<1||n>1000000)throw new FormatException("Ungültige Iterationszahl");
        using(var hash=new Rfc2898DeriveBytes(key,Hex(salt),n,HashAlgorithmName.SHA256))return hash.GetBytes(32);
    }
    public static string Answer(string challenge,string pw){
        if(challenge.StartsWith("2$")){var p=challenge.Split('$');if(p.Length!=5)throw new FormatException("Unbekannte Challenge");
            return p[4]+"$"+Hex(Derive(Derive(Encoding.UTF8.GetBytes(pw),p[2],p[1]),p[4],p[3]));}
        if(String.IsNullOrWhiteSpace(challenge)||challenge.Contains("$"))throw new FormatException("Unbekannte Challenge");
        using(var md5=MD5.Create())return challenge+"-"+Hex(md5.ComputeHash(Encoding.Unicode.GetBytes(challenge+"-"+new string(pw.Select(c=>c>255?'.':c).ToArray()))));
    }
    string GatewayWeb(){
        var challenge=XDocument.Parse(WebPage("/login_sid.lua?version=2"));var root=challenge.Root;
        int wait;if(int.TryParse(Field(root,"BlockTime"),out wait)&&wait>0)return "Anmeldesperre · "+wait+" Sekunden";
        string u=username;
        if(String.IsNullOrWhiteSpace(u)&&root.Descendants("User").Any())u=SingleUser(challenge);
        var response=Answer(Field(root,"Challenge"),password);
        var login=XDocument.Parse(WebPage("/login_sid.lua?version=2","username="+Uri.EscapeDataString(u??"")+"&response="+Uri.EscapeDataString(response)));
        string sid=Field(login.Root,"SID");if(!System.Text.RegularExpressions.Regex.IsMatch(sid,"^[a-fA-F0-9]{16}$")||sid=="0000000000000000")return "Anmeldung abgewiesen";
        try{var data=XDocument.Parse(WebPage("/webservices/homeautoswitch.lua?switchcmd=getdevicelistinfos&sid="+sid));
            return "Anmeldung erfolgreich · "+(data.Root.Name.LocalName=="devicelist"?"AHA meldet "+data.Root.Elements("device").Count()+" Geräte":"unerwartete AHA-Antwort");}
        catch(Exception e){return "Anmeldung erfolgreich · AHA: "+Reason(e);}
        finally{try{WebPage("/login_sid.lua","logout=1&sid="+sid);}catch(WebException){}catch(IOException){}}
    }
    public string TestAccess(){
        var rows=new List<string>();try{rows.Add("TR-064: "+ProbeTr());}catch(Exception e){rows.Add("TR-064: "+Reason(e));}
        if(router.Host=="192.168.178.43")try{rows.Add("Gateway-Webzugang: "+GatewayWeb());}catch(Exception e){rows.Add("Gateway-Webzugang: "+Reason(e));}
        return string.Join(Environment.NewLine,rows);
    }
}
partial class Atlas {
    readonly Dictionary<string,NetworkCredential> deviceAccess=new Dictionary<string,NetworkCredential>();
    void InitAccess(FlowLayoutPanel bar){
        var open=new Button{Text="Gerätezugänge …",AutoSize=true};bar.Controls.Add(open);open.Click+=(a,b)=>AccessDialog();
    }
    void AccessDialog(){
        string[] ips={"192.168.178.23","192.168.178.25","192.168.178.54","192.168.178.43"};
        using(var dlg=new Form{Text="Gerätezugänge · für diese Programmsitzung",Width=540,Height=345,StartPosition=FormStartPosition.CenterParent}){
            var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(10)};
            var device=new ComboBox{Width=335,DropDownStyle=ComboBoxStyle.DropDownList};
            device.Items.AddRange(new[]{"1750E · "+ips[0],"450E · "+ips[1],"6000 · "+ips[2],"Smart Gateway · "+ips[3]});
            var u=new TextBox{Width=335};var pw=new TextBox{Width=335,UseSystemPasswordChar=true};var result=new TextBox{Width=480,Height=70,Multiline=true,ReadOnly=true};
            var actions=new FlowLayoutPanel{AutoSize=true};var test=new Button{Text="Zugang testen",AutoSize=true};var save=new Button{Text="Für Netz erfassen übernehmen",AutoSize=true};
            var userLabel=new Label{AutoSize=true};
            Func<bool> validUser=()=>{if(device.SelectedIndex!=0||!String.IsNullOrWhiteSpace(u.Text))return true;result.Text="Für deinen 1750E den automatisch angelegten FRITZ!Box-Benutzernamen eingeben.";u.Focus();return false;};
            actions.Controls.Add(test);actions.Controls.Add(save);
            panel.Controls.AddRange(new Control[]{device,userLabel,u,new Label{Text="Gerätekennwort",AutoSize=true},pw,actions,result});dlg.Controls.Add(panel);
            device.SelectedIndexChanged+=(a,b)=>{NetworkCredential c;deviceAccess.TryGetValue(ips[device.SelectedIndex],out c);userLabel.Text=device.SelectedIndex==0?"FRITZ!Box-Benutzername (für deinen 1750E erforderlich)":"Benutzername (falls vom Gerät benötigt)";u.Text=c!=null?c.UserName:device.SelectedIndex==0?user.Text.Trim():"";pw.Text=c==null?"":c.Password;result.Clear();};device.SelectedIndex=0;
            save.Click+=(a,b)=>{string ip=ips[device.SelectedIndex];if(pw.Text==""){deviceAccess.Remove(ip);result.Text="Eigener Zugang entfernt.";}else{if(!validUser())return;deviceAccess[ip]=new NetworkCredential(u.Text.Trim(),pw.Text);result.Text="Zugang für diese Sitzung übernommen."; }};
            test.Click+=async(a,b)=>{if(pw.Text==""){result.Text="Gerätekennwort eingeben.";return;}
                if(!validUser())return;var reader=new Reader(ips[device.SelectedIndex],u.Text.Trim(),pw.Text);panel.Enabled=false;result.Text="Lese Gerät …";
                try{var msg=await Task.Run(()=>reader.TestAccess());if(!dlg.IsDisposed)result.Text=msg;}
                finally{if(!dlg.IsDisposed)panel.Enabled=true;}
            };dlg.ShowDialog(this);
        }
    }
}
