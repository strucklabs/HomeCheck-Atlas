using System;
using System.IO;
using System.Diagnostics;
using System.Reflection;
class HomeCheck {
    static int Main() {
        string dir = Path.Combine(Path.GetTempPath(), "HomeCheck-" + Guid.NewGuid().ToString("N"));
        try {
            Directory.CreateDirectory(dir);
            foreach (string name in new[] {"HomeCheck.ps1", "Menue.ps1"}) {
                using (var src = Assembly.GetExecutingAssembly().GetManifestResourceStream(name))
                using (var dst = File.Create(Path.Combine(dir, name))) src.CopyTo(dst);
            }
            string ps = Path.Combine(Environment.SystemDirectory, @"WindowsPowerShell\v1.0\powershell.exe");
            var info = new ProcessStartInfo(ps, "-NoProfile -ExecutionPolicy RemoteSigned -File \"" + Path.Combine(dir, "Menue.ps1") + "\"");
            info.UseShellExecute = false;
            using (var process = Process.Start(info)) {
                process.WaitForExit();
                return process.ExitCode;
            }
        } catch (Exception e) {
            Console.Error.WriteLine(e.Message);
            return 1;
        } finally {
            foreach (string name in new[] {"Menue.ps1", "HomeCheck.ps1"}) {
                try { File.Delete(Path.Combine(dir, name)); } catch (IOException) {}
            }
            try { Directory.Delete(dir); } catch (IOException) {}
            if (!Console.IsInputRedirected) { Console.WriteLine("Enter zum Schliessen."); Console.ReadLine(); }
        }
    }
}
