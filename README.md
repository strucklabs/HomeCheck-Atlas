# HomeCheck Atlas

Portable Windows-Anwendung mit deutscher Oberfläche zur Darstellung und Diagnose eines Heimnetzes mit FRITZ!Box, Repeatern und weiteren Netzwerkgeräten.

**Status:** Nutzbarer Entwicklungsstand. Weitere Erweiterungen sind vorgesehen; das Projekt ist noch nicht abgeschlossen.

## Funktionen

- Netzwerkkarte mit Geräteübersicht, Verbindungen und auswählbaren Symbolen.
- Abfragen der FRITZ!-Infrastruktur über TR-064 sowie ergänzende lokale Netzwerkdaten.
- Gerätezugänge für die laufende Programmsitzung.
- WLAN-Messwerte, zeitlicher Verlauf und markierbare Störungen mit Notizen.
- Ping-Messungen und grafische Darstellung der ausgewählten Netzwerkstrecke.
- Manuell ergänzbare Verkabelung und lokale Symbolzuordnungen.
- Ergänzende Erkennung von DECT-, AHA- und Tuya-Geräten, soweit die Geräte entsprechende Daten bereitstellen.

## Starten

Die aktuelle bereitgestellte Ausgabe ist `outputs/HomeCheck-Atlas-Streckengrafik.exe`.
Die EXE kann ohne Installation gestartet werden. Benötigt werden Windows und eine passende installierte .NET-Framework-Laufzeit. Die App ist eine Windows-Forms-Anwendung; Python und ein Browser werden zum Starten nicht benötigt.

FRITZ!Box-Zugangsdaten im Hauptfenster eingeben und **Netz erfassen** wählen. Zusätzliche Gerätezugänge lassen sich über **Gerätezugänge** für die laufende Sitzung hinterlegen. Die verfügbaren Daten hängen von Modell, Firmware und Zugriffsrechten ab.

## Quellcode und Build

Der aktuelle zusammengehörige Quellstand liegt in `work/Symbole/`. Das Buildskript kompiliert dessen `Atlas*.cs` und `IconCatalog.cs` und bettet Symbole sowie Lizenztexte ein:

```powershell
& .\work\Symbole\build.ps1
```

Das Skript erwartet den .NET-Framework-C#-Compiler unter `C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. Es erzeugt aus historischen Gründen `work/Symbole/HomeCheck-Atlas-Symbole.exe`; diese Datei enthält auch die aktuelle Streckengrafik. Die bereitgestellte Ausgabe unter `outputs/` wird dadurch nicht automatisch ersetzt.

Die Python-Skripte dienen zur Beschaffung und Erzeugung der Symbolressourcen. Für das Erzeugen der Grafiken wird Pillow benötigt; für einen normalen Build sind die Ressourcen bereits enthalten.

| Pfad | Inhalt |
| --- | --- |
| `work/Symbole/` | Aktuelle Anwendung, Buildskript, Symbolressourcen und Tests |
| `outputs/` | Bereitgestellte EXEs, ZIP-Paket und Vorschauen; enthält auch ältere Varianten |
| `work/LokaleNetze/`, `work/Verkabelung/`, `work/DHCP/` | Frühere Entwicklungsstände |
| `work/*.cs`, `work/*.ps1` | Frühere Stände, Diagnosewerkzeuge und Tests |
| `work/test/` | Testquellen; echte Messprotokolle gehören nicht in Git |

Die früheren Quellstände nicht zusammen mit dem aktuellen Verzeichnis kompilieren. Die Testquellen `IconTests.cs` und `RouteDiagramTest.cs` dokumentieren Prüfungen mit synthetischen Geräten; sie sind keine Live-Netzwerkprüfung.

## Datenschutz

Zugangsdaten werden im aktuellen App-Code nur für die Programmsitzung gehalten. Es ist keine dauerhafte Passwortspeicherung implementiert.

Die App verarbeitet personenbezogene oder einem Haushalt zuordenbare Daten, etwa Geräte- und Netzwerknamen, IP-/MAC-Adressen, Gerätekennungen, Verbindungen und Zeitpunkte. Aufzeichnungen und Einstellungen werden lokal unter `%LOCALAPPDATA%\HomeCheck` gespeichert:

- `Meshverlauf`: Verlauf, Störungsmarken, Momentaufnahmen und Laufzeitmessungen.
- `Symbole`: Gerätekennungen und manuelle Symbolzuordnungen.
- `Verkabelung`: Manuelle Verbindungen und Gerätekennungen.

Diese Dateien sind nicht durch die Anwendung verschlüsselt. **Aufzeichnungen löschen** entfernt die Verlaufsaufzeichnungen, aber nicht die Symbol- und Verkabelungseinstellungen. Berichte, Rohdaten und Screenshots vor einer Weitergabe auf persönliche Angaben prüfen. Ein automatischer Anonymisierungsexport ist derzeit nicht implementiert.

Echte Diagnoseprotokolle, Zugangsdaten und private Laufzeitdaten dürfen nicht eingecheckt werden. Die `.gitignore` schließt typische Dateien aus, ersetzt aber keine Prüfung neuer Dateien. Tests und Vorschauen sollen ausschließlich synthetische Daten verwenden. Eine separat aufbewahrte lokale Aufzeichnung wird durch das Entfernen aus Git nicht gelöscht.

## Grenzen

Die dargestellte Topologie beruht auf gemeldeten und manuell ergänzten Verbindungen. Sie ist kein Traceroute. Ping-Messungen erfolgen jeweils vom ausführenden PC; die Grafik misst keine einzelnen Zwischenstrecken isoliert. Fehlende oder uneindeutige Geräteinformationen begrenzen die Aussagekraft. Signalwerte hängen von den tatsächlich verfügbaren Geräteangaben ab.

## Lizenzen

Die verwendeten Material Design Icons stammen von Pictogrammers, Version 7.4.47. Die zugehörigen Lizenztexte liegen in `work/Symbole/assets/LICENSE-MDI.txt` und `work/Symbole/assets/LICENSE-Apache-2.0.txt` und sind in der aktuellen EXE eingebettet.

Für den eigenen Anwendungscode wurde bisher keine separate Projektlizenz festgelegt. Die Lizenz der Symbole ist keine Lizenz für den gesamten Anwendungscode.
