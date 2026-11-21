param(
    [ValidateRange(1,100000)][int]$Runden = 1,
    [ValidateRange(1,3600)][int]$Pause = 15,
    [string]$Geraet,
    [ValidateRange(0,65535)][int]$Port = 0,
    [switch]$NurLokal,
    [string]$Ausgabe = ([Environment]::GetFolderPath('MyDocuments') + '\HomeCheck')
)
$ErrorActionPreference = 'Stop'
if ($Port -and !$Geraet) { throw 'Bitte zum Port auch -Geraet angeben.' }
$ordner = Join-Path $Ausgabe ((Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,6))
New-Item -ItemType Directory -Path $ordner -Force | Out-Null
$csv = Join-Path $ordner 'Messungen.csv'
function Probe($typ,$ziel,$dienst=0) {
    $uhr = [Diagnostics.Stopwatch]::StartNew()
    $ok = $false
    try {
        switch ($typ) {
            'Ping' {
                $client = New-Object Net.NetworkInformation.Ping
                try {
                    $reply = $client.Send($ziel,1500)
                    $detail = [string]$reply.Status
                    $ok = $reply.Status -eq 'Success'
                } finally { $client.Dispose() }
            }
            'TCP' {
                $client = New-Object Net.Sockets.TcpClient
                try {
                    $task = $client.ConnectAsync([string]$ziel,[int]$dienst)
                    $ok = $task.Wait(2000) -and $client.Connected
                    $detail = if ($ok) { 'Verbunden' } else { 'Zeitlimit' }
                } finally { $client.Dispose() }
            }
            'DNS' {
                $antwort = @(Resolve-DnsName -Name $ziel -Type A -DnsOnly -QuickTimeout -ErrorAction Stop | Where-Object IPAddress)
                $ok = $antwort.Count -gt 0
                $detail = $antwort.IPAddress -join ', '
            }
        }
    } catch { $detail = $_.Exception.GetBaseException().Message }
    [pscustomobject]@{Zeit=(Get-Date -Format o); Runde=$i; Test=$typ; Ziel=$ziel; Port=$dienst; OK=$ok; Dauer_ms=$uhr.ElapsedMilliseconds; Detail=$detail}
}
function Gateway {
    $wege = foreach ($route in Get-NetRoute -AddressFamily IPv4 -DestinationPrefix '0.0.0.0/0' -ErrorAction Stop) {
        if ($route.NextHop -eq '0.0.0.0') { continue }
        $nic = Get-NetIPInterface -AddressFamily IPv4 -InterfaceIndex $route.InterfaceIndex
        if ($nic.ConnectionState -eq 'Connected') {
            [pscustomobject]@{Adresse=$route.NextHop; Metrik=([int]$route.RouteMetric + [int]$nic.InterfaceMetric); Adapter=$nic.InterfaceAlias}
        }
    }
    $wege | Sort-Object Metrik | Select-Object -First 1
}
Write-Host "Ergebnisse: $ordner"
foreach ($cmd in 'ipconfig /all','netsh wlan show interfaces','route print -4') {
    ">>> $cmd" | Out-File "$ordner\Netzstatus.txt" -Append
    cmd.exe /d /c $cmd | Out-File "$ordner\Netzstatus.txt" -Append
}
try {
    Get-WinEvent -FilterHashtable @{LogName='Microsoft-Windows-WLAN-AutoConfig/Operational';StartTime=(Get-Date).AddHours(-24)} -MaxEvents 80 |
        Select-Object TimeCreated,Id,Message | Format-List | Out-File "$ordner\WLAN-Verlauf.txt"
} catch { $_.Exception.Message | Out-File "$ordner\WLAN-Verlauf.txt" }
for ($i=1; $i -le $Runden; $i++) {
    $weg = $null
    try { $weg = Gateway } catch { Write-Warning $_.Exception.Message }
    $messung = @(
        if ($weg) { Probe Ping $weg.Adresse }
        if (!$NurLokal) { Probe TCP '1.1.1.1' 443; Probe TCP '8.8.8.8' 443; Probe DNS 'example.org' }
        if ($Geraet) { Probe Ping $Geraet; if ($Port) { Probe TCP $Geraet $Port } }
    )
    if ($messung.Count) { $messung | Export-Csv $csv -Delimiter ';' -NoTypeInformation -Append -Encoding UTF8 }
    $hinweise = @(
        if (!$weg) { 'Kein messbares IPv4-Standardgateway. Adapter und Routing im Netzstatus ansehen.' }
        elseif (!$messung[0].OK) { "Keine Ping-Antwort vom Gateway ($($weg.Adapter)). ICMP-Filter und lokale Verbindung kommen infrage." }
        if (!$NurLokal) {
            $extern = @($messung | Where-Object { $_.Test -eq 'TCP' -and $_.Ziel -in '1.1.1.1','8.8.8.8' } | Select-Object -First 2)
            $erreichbar = @($extern | Where-Object OK).Count
            $dns = $messung | Where-Object Test -eq DNS
            if ($erreichbar -eq 0) { 'Beide externen TCP-Ziele unerreichbar. Bei antwortendem Gateway Router-WAN, VPN und Filter pruefen.' }
            if ($erreichbar -eq 1) { 'Nur eines der externen Ziele antwortet. Ziel oder Pfad gesondert pruefen.' }
            if (!$dns.OK -and $erreichbar) { 'IP-Verbindung funktioniert, Namensaufloesung auffaellig. DNS-Server im Netzstatus pruefen.' }
            elseif (!$dns.OK) { 'DNS-Abfrage ebenfalls fehlgeschlagen.' }
        }
        if ($Geraet -and !$messung[-1].OK) {
            if ($Port) { 'Geraetedienst unerreichbar. Adresse, Port, Hub und Dienststatus pruefen.' }
            else { 'Geraet antwortet nicht auf Ping. Bei Schlafmodus oder ICMP-Filter besser einen bekannten Dienstport testen.' }
        }
    )
    $status = if ($hinweise.Count) { $hinweise -join ' ' } else { 'Alle ausgefuehrten Tests erfolgreich.' }
    $zeile = "$(Get-Date -Format s) | Runde $i | $status"
    Write-Host $zeile
    $zeile | Out-File "$ordner\Verlauf.txt" -Append -Encoding utf8
    if ($i -lt $Runden) { Start-Sleep -Seconds $Pause }
}
