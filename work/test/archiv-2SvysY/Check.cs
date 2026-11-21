using System;using System.IO;using System.Reflection;using System.Windows.Forms;using System.Runtime.InteropServices;
class ArchiveTest{
[DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
[DllImport("user32.dll")]static extern bool PostMessage(IntPtr h,int msg,IntPtr w,IntPtr l);
[STAThread]static void Main(string[] args){var archive=Path.Combine(args[0],"fixture");Directory.CreateDirectory(Path.Combine(archive,"Laufzeit"));var first=Path.Combine(archive,"2024-05-17.jsonl");var second=Path.Combine(archive,"Laufzeit","2024-05-18.jsonl");var keep=Path.Combine(archive,"andere.jsonl");
foreach(var file in new[]{first,second,keep})File.WriteAllText(file,"fixture");
using(var ui=new Atlas()){ui.Show();var flags=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(Atlas);type.GetField("archive",flags).SetValue(ui,archive);
foreach(int answer in new[]{7,6})using(var timer=new Timer{Interval=50}){timer.Tick+=(s,e)=>{var hwnd=FindWindow("#32770","Alle Aufzeichnungen löschen");if(hwnd!=IntPtr.Zero){PostMessage(hwnd,273,(IntPtr)answer,IntPtr.Zero);timer.Stop();}};timer.Start();type.GetMethod("ConfirmErase",flags).Invoke(ui,null);if(File.Exists(first)!=(answer==7)||File.Exists(second)!=(answer==7)||!File.Exists(keep))throw new Exception("Archivzustand");}
}Console.WriteLine("OK");}}