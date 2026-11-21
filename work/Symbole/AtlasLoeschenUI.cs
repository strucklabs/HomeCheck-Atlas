using System;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Collections.Generic;
using System.Windows.Forms;

partial class Atlas {
    long archiveGeneration;
    void ConfirmErase(){
        string prompt="Alle lokal gespeicherten Aufzeichnungen für alle Tage endgültig löschen?"+
            Environment.NewLine+Environment.NewLine+"Dazu gehören Mesh- und SNR-Verlauf, Kanalauslastung, Laufzeitmessungen sowie Störungsmarken mit Notizen und Momentaufnahmen."+
            Environment.NewLine+Environment.NewLine+"Laufende Aufzeichnungen dieser Programmsitzung werden beendet.";
        if(MessageBox.Show(this,prompt,"Alle Aufzeichnungen löschen",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2)!=DialogResult.Yes)return;
        EraseRecordings();
    }
    void EraseRecordings(){
        archiveGeneration++;timer.Stop();StopLatency();session=null;latestMesh=null;
        monitor.Text="Aufzeichnung starten";monitorState.Text="Aufzeichnung aus";
        var errors=new List<string>();int removed=0;
        if(Directory.Exists(archive)){
            try{
                if((File.GetAttributes(archive)&FileAttributes.ReparsePoint)!=0)throw new IOException("Archivordner ist eine Verknüpfung.");
                foreach(string folder in new[]{archive,Path.Combine(archive,"Laufzeit")}){
                    if(!Directory.Exists(folder))continue;
                    try{
                        if((File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("Ordner ist eine Verknüpfung.");
                        foreach(var file in Directory.GetFiles(folder,"*.jsonl",SearchOption.TopDirectoryOnly)){
                            DateTime date;if(!Path.GetExtension(file).Equals(".jsonl",StringComparison.OrdinalIgnoreCase)||
                                !DateTime.TryParseExact(Path.GetFileNameWithoutExtension(file),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out date))continue;
                            try{File.Delete(file);removed++;}catch(IOException ex){errors.Add(ex.Message);}catch(UnauthorizedAccessException ex){errors.Add(ex.Message);}
                        }
                    }catch(IOException ex){errors.Add(ex.Message);}catch(UnauthorizedAccessException ex){errors.Add(ex.Message);}
                }
            }catch(IOException ex){errors.Add(ex.Message);}catch(UnauthorizedAccessException ex){errors.Add(ex.Message);}
        }
        LoadDay();
        status.Text=errors.Count==0?(removed==0?"Keine gespeicherten Aufzeichnungen vorhanden.":"Alle Aufzeichnungen gelöscht."):"Aufzeichnungen teilweise nicht gelöscht.";
        if(errors.Count>0)MessageBox.Show(this,string.Join(Environment.NewLine,errors.Distinct()),"Löschen unvollständig",MessageBoxButtons.OK,MessageBoxIcon.Warning);
    }
}