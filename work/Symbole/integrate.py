from pathlib import Path
p=Path('work/Symbole/AtlasStreckeBasis.cs')
s=p.read_text(encoding='utf-8-sig')
def replace(a,b):
 global s
 assert a in s,a
 s=s.replace(a,b)
a=s.index('    static string Glyph(Device d){');b=s.index('    HashSet<Device> radialDevices',a)
s=s[:a]+'    public Func<Device,string> IconFor=DeviceIcons.Automatic;\n'+s[b:]
replace('        using(var icon=new Font("Segoe MDL2 Assets",15))\n','')
replace('g.DrawString(Glyph(n),icon,Brushes.White,new RectangleF(p.X-17,p.Y-17,34,34),centered);','DeviceIcons.Draw(g,IconFor(n),new RectangleF(p.X-14,p.Y-14,28,28),Color.White);')
replace('list.DrawMode=DrawMode.OwnerDrawFixed;','list.DrawMode=DrawMode.OwnerDrawFixed;list.ItemHeight=Math.Max(26,list.Font.Height+8);')
replace('TextRenderer.DrawText(args.Graphics,device.ToString()', 'DeviceIcons.Draw(args.Graphics,IconFor(device),new RectangleF(args.Bounds.X+3,args.Bounds.Y+3,20,20),color);\n            var textBounds=new Rectangle(args.Bounds.X+28,args.Bounds.Y,Math.Max(0,args.Bounds.Width-28),args.Bounds.Height);\n            TextRenderer.DrawText(args.Graphics,device.ToString()')
replace('args.Font,args.Bounds,color,','args.Font,textBounds,color,')
replace('data=LoadWiring(data);map.SetData(data);','data=LoadWiring(data);LoadIcons(data);map.SetData(data);')
replace('InitLocal(pages);InitHistory','InitIcons(overview);InitLocal(pages);InitHistory')
replace('void UpdateSummary(Device d){routeButton.Enabled=d!=null;', 'void UpdateSummary(Device d){iconButton.Enabled=d!=null&&CanChangeIcon(d);routeButton.Enabled=d!=null;')
replace('Heading(d.Name);','Heading(d.Name);\n            summary.AppendText("Symbol: "+DeviceIcons.Find(IconFor(d)).Name+" ("+(iconPreferences!=null&&iconPreferences.Choice(d)!=null?"manuell":"automatisch")+")\\n");\n            if(!CanChangeIcon(d))summary.AppendText("Eigene Symbolwahl erst mit eindeutiger Geräte- und Netzwerkkennung möglich.\\n");')
p.write_text(s,encoding='utf-8-sig')
