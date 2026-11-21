using System;
class DeviceIcon {
    public readonly string Id, Name, Group;
    public DeviceIcon(string id,string name,string group){Id=id;Name=name;Group=group;}
    public override string ToString(){return Name;}
}
static partial class DeviceIcons {
    public static readonly DeviceIcon[] All=new DeviceIcon[]{
        new DeviceIcon("router-wireless", "WLAN-Router", "Netzwerk"),
        new DeviceIcon("router", "Router / Modem", "Netzwerk"),
        new DeviceIcon("access-point", "WLAN-Zugangspunkt", "Netzwerk"),
        new DeviceIcon("wifi-arrow-left-right", "WLAN-Repeater", "Netzwerk"),
        new DeviceIcon("switch", "Netzwerk-Switch", "Netzwerk"),
        new DeviceIcon("hub", "Gateway / Hub", "Netzwerk"),
        new DeviceIcon("server", "Server", "Netzwerk"),
        new DeviceIcon("nas", "NAS / Netzwerkspeicher", "Netzwerk"),
        new DeviceIcon("network-outline", "Virtuelles Netzwerk", "Netzwerk"),
        new DeviceIcon("lan", "Netzwerkgerät", "Netzwerk"),
        new DeviceIcon("desktop-tower-monitor", "Desktop-PC", "Computer & Mobilgeräte"),
        new DeviceIcon("laptop", "Laptop", "Computer & Mobilgeräte"),
        new DeviceIcon("cellphone", "Smartphone", "Computer & Mobilgeräte"),
        new DeviceIcon("tablet", "Tablet", "Computer & Mobilgeräte"),
        new DeviceIcon("book-open-variant", "E-Book-Reader", "Computer & Mobilgeräte"),
        new DeviceIcon("printer", "Drucker", "Computer & Mobilgeräte"),
        new DeviceIcon("television", "Fernseher", "Unterhaltung"),
        new DeviceIcon("cast", "Streaming-Gerät", "Unterhaltung"),
        new DeviceIcon("disc-player", "Blu-ray / DVD-Player", "Unterhaltung"),
        new DeviceIcon("gamepad-variant", "Spielkonsole", "Unterhaltung"),
        new DeviceIcon("speaker", "Lautsprecher", "Unterhaltung"),
        new DeviceIcon("speaker-wireless", "WLAN-Lautsprecher", "Unterhaltung"),
        new DeviceIcon("tablet-dashboard", "Smart-Display", "Unterhaltung"),
        new DeviceIcon("set-top-box", "Streaming-Box", "Unterhaltung"),
        new DeviceIcon("home-automation", "Smarthome-Zentrale", "Smarthome"),
        new DeviceIcon("lightbulb", "Lampe", "Smarthome"),
        new DeviceIcon("led-strip-variant", "LED-Streifen", "Smarthome"),
        new DeviceIcon("thermostat", "Thermostat", "Smarthome"),
        new DeviceIcon("radiator", "Heizung", "Smarthome"),
        new DeviceIcon("phone-classic", "Telefon / DECT", "Smarthome"),
        new DeviceIcon("robot-vacuum", "Saugroboter", "Smarthome"),
        new DeviceIcon("power-socket-de", "Steckdose", "Smarthome"),
        new DeviceIcon("cctv", "Kamera", "Smarthome"),
        new DeviceIcon("motion-sensor", "Bewegungsmelder", "Smarthome"),
        new DeviceIcon("smoke-detector", "Rauchmelder", "Smarthome"),
        new DeviceIcon("help-circle-outline", "Unbekanntes Gerät", "Sonstige"),
    };
}
