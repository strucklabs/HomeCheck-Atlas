using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using System.Security.Cryptography;
using System.Text;

static partial class DeviceIcons {
    static readonly Dictionary<string,Bitmap> Images=new Dictionary<string,Bitmap>();
    public static DeviceIcon Find(string id){return All.FirstOrDefault(x=>x.Id==id);}
    public static string Identity(Device d){
        if(d==null)return "";
        if(!String.IsNullOrWhiteSpace(d.Mac))return "mac:"+d.Mac.Replace('-',':').ToUpperInvariant();
        if(!String.IsNullOrWhiteSpace(d.Ain))return "ain:"+d.Ain.Replace(" ","");
        return String.IsNullOrWhiteSpace(d.MeshId)?"":"mesh:"+d.MeshId;
    }
    public static string Automatic(Device d){
        string s=(d.Model+" "+d.Kind+" "+d.ReportedName+" "+d.Name).ToLowerInvariant();
        Func<string[],bool> has=words=>words.Any(s.Contains);
        if(d.Role=="master")return "router-wireless";
        if(d.Role=="slave"||has(new[]{"repeater"}))return "wifi-arrow-left-right";
        if(d.Gateway||has(new[]{"gateway","hub","zentrale"}))return "home-automation";
        if(has(new[]{"thermostat","fritz!dect 301","fritz!dect 302"}))return "thermostat";
        if(has(new[]{"dect","telephone","telefon","fritz!fon"}))return "phone-classic";
        if(has(new[]{"switch"}))return "switch";
        if(has(new[]{"router","fritz!box"}))return "router-wireless";
        if(has(new[]{"access point","access-point"}))return "access-point";
        if(has(new[]{"robot","deebot","roborock","saug"}))return "robot-vacuum";
        if(has(new[]{"led-strip","led strip","led-streifen"}))return "led-strip-variant";
        if(has(new[]{"light","lampe","bulb","leuchte"}))return "lightbulb";
        if(has(new[]{"steckdose","smart plug"}))return "power-socket-de";
        if(has(new[]{"camera","kamera","webcam"}))return "cctv";
        if(has(new[]{"printer","drucker","hl-l2350"}))return "printer";
        if(has(new[]{"chromecast","steam-link","steam link"}))return "cast";
        if(has(new[]{"blu-ray","bluray","dvd"}))return "disc-player";
        if(has(new[]{"playstation","xbox","wii","console","konsole"}))return "gamepad-variant";
        if(has(new[]{"nest hub","smart-display","smart display","fuchsia"}))return "tablet-dashboard";
        if(has(new[]{"nest-mini","nest mini","home-mini","home mini","nest audio","google audio"}))return "speaker-wireless";
        if(has(new[]{"speaker","lautsprecher","audio"}))return "speaker";
        if(has(new[]{"television","smarttv","smart-tv","fernseher"})||s.Split(' ','-','_').Contains("tv"))return "television";
        if(has(new[]{"kindle","ebook","e-book","tolino"}))return "book-open-variant";
        if(has(new[]{"tablet","ipad"}))return "tablet";
        if(has(new[]{"smartphone","iphone","mobile","cellphone"}))return "cellphone";
        if(has(new[]{"laptop","notebook","thinkpad","macbook"}))return "laptop";
        if(has(new[]{"desktop","computer","workstation"}))return "desktop-tower-monitor";
        if(has(new[]{"synology","qnap","nas"}))return "nas";
        if(has(new[]{"server"}))return "server";
        if(has(new[]{"virtual","virtuell","vmware"}))return "network-outline";
        if(has(new[]{"smarthome","smart-home"}))return "home-automation";
        return "help-circle-outline";
    }
    public static void Draw(Graphics g,string id,RectangleF bounds,Color color){
        if(Find(id)==null)id="help-circle-outline";
        Bitmap bitmap;
        if(!Images.TryGetValue(id,out bitmap)){
            using(var stream=typeof(DeviceIcons).Assembly.GetManifestResourceStream("HomeCheck.Icons."+id+".png"))
            using(var original=new Bitmap(stream))bitmap=new Bitmap(original);
            Images[id]=bitmap;
        }
        using(var attrs=new ImageAttributes()){
            var matrix=new ColorMatrix(new float[][]{
                new[]{0f,0f,0f,0f,0f},new[]{0f,0f,0f,0f,0f},new[]{0f,0f,0f,0f,0f},
                new[]{0f,0f,0f,color.A/255f,0f},new[]{color.R/255f,color.G/255f,color.B/255f,0f,1f}});
            attrs.SetColorMatrix(matrix);
            g.DrawImage(bitmap,Rectangle.Round(bounds),0,0,bitmap.Width,bitmap.Height,GraphicsUnit.Pixel,attrs);
        }
    }
}

class IconPreferences {
    readonly string path;
    Dictionary<string,string> choices=new Dictionary<string,string>();
    public IconPreferences(string file){path=file;Reload();}
    Dictionary<string,string> Read(){
        return File.Exists(path)?new JavaScriptSerializer().Deserialize<Dictionary<string,string>>(File.ReadAllText(path))??new Dictionary<string,string>():new Dictionary<string,string>();
    }
    public void Reload(){choices=Read();}
    public string Choice(Device d){string value;return choices.TryGetValue(DeviceIcons.Identity(d),out value)&&DeviceIcons.Find(value)!=null?value:null;}
    public string Resolve(Device d){return Choice(d)??DeviceIcons.Automatic(d);}
    public void Save(Device d,string icon){
        var key=DeviceIcons.Identity(d);
        if(key=="")throw new IOException("Für dieses Gerät fehlt eine stabile Kennung. Eine dauerhafte Symbolzuordnung ist noch nicht möglich.");
        if(icon!=null&&DeviceIcons.Find(icon)==null)throw new IOException("Unbekanntes Symbol.");
        Directory.CreateDirectory(Path.GetDirectoryName(path));
        using(var guard=new FileStream(path+".lock",FileMode.OpenOrCreate,FileAccess.Write,FileShare.None)){
            var next=Read();if(icon==null)next.Remove(key);else next[key]=icon;
            var tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{
                File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(next),Encoding.UTF8);
                if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);
                choices=next;
            }finally{if(File.Exists(tmp))File.Delete(tmp);}
        }
    }
}

sealed class IconPicker:Form {
    public string Choice;
    readonly ImageList images=new ImageList{ImageSize=new Size(48,48),ColorDepth=ColorDepth.Depth32Bit};
    readonly ListView grid=new ListView{Dock=DockStyle.Fill,View=View.LargeIcon,MultiSelect=false,HideSelection=false,ShowItemToolTips=true};
    public IconPicker(Device device,string current){
        Text="Symbol auswählen – "+device.Name;Size=new Size(790,640);MinimumSize=new Size(570,440);
        StartPosition=FormStartPosition.CenterParent;AutoScaleMode=AutoScaleMode.Font;Font=SystemFonts.MessageBoxFont;
        var search=new TextBox{Dock=DockStyle.Fill};
        var caption=new Label{Text="Suchen:",AutoSize=true,Anchor=AnchorStyles.Left};
        var header=new TableLayoutPanel{Dock=DockStyle.Top,AutoSize=true,Padding=new Padding(10),ColumnCount=2};
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
        header.Controls.Add(caption,0,0);header.Controls.Add(search,1,0);
        var note=new Label{Dock=DockStyle.Bottom,Height=44,Padding=new Padding(10,4,10,0)};
        var footer=new FlowLayoutPanel{Dock=DockStyle.Bottom,AutoSize=true,Padding=new Padding(8),FlowDirection=FlowDirection.RightToLeft};
        var cancel=new Button{Text="Abbrechen",AutoSize=true,DialogResult=DialogResult.Cancel};
        var apply=new Button{Text="Übernehmen",AutoSize=true,Enabled=false};
        var auto=new Button{Text="Automatisch erkennen",AutoSize=true};
        footer.Controls.AddRange(new Control[]{cancel,apply,auto});CancelButton=cancel;AcceptButton=apply;
        Controls.Add(grid);Controls.Add(note);Controls.Add(footer);Controls.Add(header);grid.LargeImageList=images;
        var imageHandle=images.Handle;
        foreach(var item in DeviceIcons.All){
            using(var bmp=new Bitmap(48,48)){
                using(var g=Graphics.FromImage(bmp)){
                    g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);g.FillEllipse(Brushes.Teal,1,1,46,46);
                    DeviceIcons.Draw(g,item.Id,new RectangleF(5,5,38,38),Color.White);
                }images.Images.Add(item.Id,bmp);
            }
        }
        string selected=current??DeviceIcons.Automatic(device);
        Action fill=()=>{
            grid.BeginUpdate();grid.Items.Clear();grid.Groups.Clear();
            foreach(var group in DeviceIcons.All.Where(x=>(x.Name+" "+x.Group+" "+x.Id).IndexOf(search.Text.Trim(),StringComparison.OrdinalIgnoreCase)>=0).GroupBy(x=>x.Group)){
                var heading=new ListViewGroup(group.Key);grid.Groups.Add(heading);
                foreach(var icon in group){var item=new ListViewItem(icon.Name,icon.Id,heading){Tag=icon.Id,ToolTipText=icon.Name};grid.Items.Add(item);if(icon.Id==selected)item.Selected=true;}
            }
            grid.EndUpdate();apply.Enabled=grid.SelectedItems.Count==1;
        };
        note.Text=current==null?"Aktuell automatisch: "+DeviceIcons.Find(selected).Name:"Aktuell manuell: "+DeviceIcons.Find(selected).Name;
        grid.SelectedIndexChanged+=(o,e)=>{apply.Enabled=grid.SelectedItems.Count==1;if(apply.Enabled){selected=(string)grid.SelectedItems[0].Tag;note.Text="Auswahl: "+DeviceIcons.Find(selected).Name+" · ändert nur das Symbol.";}};
        search.TextChanged+=(o,e)=>fill();
        apply.Click+=(o,e)=>{if(!apply.Enabled)return;Choice=selected;DialogResult=DialogResult.OK;};
        grid.DoubleClick+=(o,e)=>apply.PerformClick();
        auto.Click+=(o,e)=>{Choice=null;DialogResult=DialogResult.OK;};
        fill();
    }
    protected override void Dispose(bool disposing){if(disposing)images.Dispose();base.Dispose(disposing);}
}

partial class Atlas {
    readonly Button iconButton=new Button{Text="Symbol ändern …",Dock=DockStyle.Bottom,Enabled=false};
    IconPreferences iconPreferences;
    void InitIcons(Control overview){
        overview.Controls.Add(iconButton);iconButton.BringToFront();
        map.IconFor=IconFor;iconButton.Click+=(o,e)=>EditIcon();
    }
    string IconFor(Device d){return iconPreferences==null?DeviceIcons.Automatic(d):iconPreferences.Resolve(d);}
    void LoadIcons(Inventory data){
        iconPreferences=null;
        var root=data.Devices.FirstOrDefault(x=>x.Role=="master");string key=DeviceIcons.Identity(root);
        if(key=="")return;
        try{
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(key))).Replace("-","");
            iconPreferences=new IconPreferences(Path.Combine(Path.GetDirectoryName(archive),"Symbole",hash+".json"));
        }catch(Exception e){data.Messages.Add("Symbole konnten nicht geladen werden: "+e.Message);}
    }
    bool CanChangeIcon(Device d){
        string key=DeviceIcons.Identity(d);
        return iconPreferences!=null&&key!=""&&map.Data.Devices.Count(x=>DeviceIcons.Identity(x)==key)==1;
    }
    void EditIcon(){
        var device=list.SelectedItem as Device;if(device==null||!CanChangeIcon(device))return;
        using(var picker=new IconPicker(device,iconPreferences.Choice(device))){
            if(picker.ShowDialog(this)!=DialogResult.OK)return;
            try{
                iconPreferences.Save(device,picker.Choice);map.Invalidate();list.Invalidate();UpdateSummary(device);
                status.Text="Symbol gespeichert: "+device.Name+" · "+(picker.Choice==null?"automatisch":"manuell");
            }catch(Exception e){MessageBox.Show(this,e.Message,"Symbol konnte nicht gespeichert werden",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        }
    }
}
