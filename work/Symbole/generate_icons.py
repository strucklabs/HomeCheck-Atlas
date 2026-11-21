from pathlib import Path
from PIL import Image, ImageDraw, ImageFont
import re,json,hashlib
root=Path(__file__).parent
items=[
('router-wireless','WLAN-Router','Netzwerk'),('router','Router / Modem','Netzwerk'),('access-point','WLAN-Zugangspunkt','Netzwerk'),('wifi-arrow-left-right','WLAN-Repeater','Netzwerk'),('switch','Netzwerk-Switch','Netzwerk'),('hub','Gateway / Hub','Netzwerk'),('server','Server','Netzwerk'),('nas','NAS / Netzwerkspeicher','Netzwerk'),('network-outline','Virtuelles Netzwerk','Netzwerk'),('lan','Netzwerkgerät','Netzwerk'),
('desktop-tower-monitor','Desktop-PC','Computer & Mobilgeräte'),('laptop','Laptop','Computer & Mobilgeräte'),('cellphone','Smartphone','Computer & Mobilgeräte'),('tablet','Tablet','Computer & Mobilgeräte'),('book-open-variant','E-Book-Reader','Computer & Mobilgeräte'),('printer','Drucker','Computer & Mobilgeräte'),
('television','Fernseher','Unterhaltung'),('cast','Streaming-Gerät','Unterhaltung'),('disc-player','Blu-ray / DVD-Player','Unterhaltung'),('gamepad-variant','Spielkonsole','Unterhaltung'),('speaker','Lautsprecher','Unterhaltung'),('speaker-wireless','WLAN-Lautsprecher','Unterhaltung'),('tablet-dashboard','Smart-Display','Unterhaltung'),('set-top-box','Streaming-Box','Unterhaltung'),
('home-automation','Smarthome-Zentrale','Smarthome'),('lightbulb','Lampe','Smarthome'),('led-strip-variant','LED-Streifen','Smarthome'),('thermostat','Thermostat','Smarthome'),('radiator','Heizung','Smarthome'),('phone-classic','Telefon / DECT','Smarthome'),('robot-vacuum','Saugroboter','Smarthome'),('power-socket-de','Steckdose','Smarthome'),('cctv','Kamera','Smarthome'),('motion-sensor','Bewegungsmelder','Smarthome'),('smoke-detector','Rauchmelder','Smarthome'),('help-circle-outline','Unbekanntes Gerät','Sonstige')]
css=(root/'assets/mdi.css').read_text()
font=ImageFont.truetype(str(root/'assets/mdi.ttf'),96)
for key,label,group in items:
 m=re.search(r'\.mdi-'+re.escape(key)+r'::before\s*\{\s*content:\s*"\\([0-9A-Fa-f]+)"',css)
 if not m: raise RuntimeError(key)
 img=Image.new('RGBA',(112,112))
 draw=ImageDraw.Draw(img); glyph=chr(int(m[1],16)); box=draw.textbbox((0,0),glyph,font=font)
 draw.text(((112-box[2]+box[0])/2-box[0],(112-box[3]+box[1])/2-box[1]),glyph,font=font,fill='white')
 img.save(root/'assets'/('icon-'+key+'.png'))
(root/'catalog.json').write_text(json.dumps(items,ensure_ascii=False,indent=2),encoding='utf-8')
rows='\n'.join('        new DeviceIcon("%s", "%s", "%s"),'%v for v in items)
(root/'IconCatalog.cs').write_text('''using System;
class DeviceIcon {
    public readonly string Id, Name, Group;
    public DeviceIcon(string id,string name,string group){Id=id;Name=name;Group=group;}
    public override string ToString(){return Name;}
}
static partial class DeviceIcons {
    public static readonly DeviceIcon[] All=new DeviceIcon[]{
'''+rows+'\n    };\n}\n',encoding='utf-8-sig')
# Offline preview of the actual included glyphs.
w=1200; h=100+6*150+65
img=Image.new('RGB',(w,h),'#f3f6f7');d=ImageDraw.Draw(img)
regular=ImageFont.truetype('C:/Windows/Fonts/segoeui.ttf',17); title=ImageFont.truetype('C:/Windows/Fonts/segoeuib.ttf',27)
d.text((30,20),'HomeCheck · Material Design Icons',font=title,fill='#223741')
d.text((30,58),'36 Symbole · automatische Zuordnung oder eigene Auswahl je Gerät',font=regular,fill='#49616b')
for i,(key,label,group) in enumerate(items):
 x=20+(i%6)*197;y=100+(i//6)*150
 d.rounded_rectangle((x,y,x+184,y+137),radius=10,fill='white')
 d.ellipse((x+66,y+12,x+118,y+64),fill='#008080')
 icon=Image.open(root/'assets'/('icon-'+key+'.png')).resize((42,42),Image.Resampling.LANCZOS)
 img.paste(icon,(x+71,y+17),icon)
 words=label.split();lines=['']
 for word in words:
  if d.textlength((lines[-1]+' '+word).strip(),font=regular)>175:lines.append(word)
  else:lines[-1]=(lines[-1]+' '+word).strip()
 for j,line in enumerate(lines):d.text((x+92,y+77+j*23),line,font=regular,fill='#223741',anchor='mt')
d.text((30,h-38),'Pictogrammers / MDI 7.4.47 · Lizenz und Herkunft im Paket enthalten',font=regular,fill='#49616b')
img.save(root/'Symboluebersicht.png')
print('Generated',len(items),'icons and preview')
