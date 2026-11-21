try {
    Write-Host 'HomeCheck | 1 Einzelmessung   2 Langzeitmessung (240 Runden)'
    $wahl = Read-Host 'Auswahl [1]'
    if ($wahl -notin '','1','2') { throw 'Bitte 1 oder 2 eingeben.' }
    $anzahl = if ($wahl -eq '2') { 240 } else { 1 }
    $ziel = (Read-Host 'IP oder Name eines eigenen Geraets (optional)').Trim()
    $optionen = @{Runden=$anzahl}
    if ($ziel) {
        $optionen.Geraet = $ziel
        $portText = Read-Host 'Bekannter TCP-Dienstport (optional)'
        if ($portText) { $optionen.Port = [int]$portText }
    }
    & "$PSScriptRoot\HomeCheck.ps1" @optionen
} catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }
