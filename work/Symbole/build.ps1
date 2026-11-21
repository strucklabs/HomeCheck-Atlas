$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
try {
    $sources=Get-ChildItem Atlas*.cs,IconCatalog.cs | ForEach-Object FullName
    $resources=Get-ChildItem assets\icon-*.png | ForEach-Object { '/resource:'+$_.FullName+',HomeCheck.Icons.'+$_.BaseName.Substring(5)+'.png' }
    $resources+='/resource:'+(Join-Path $PSScriptRoot 'assets\LICENSE-MDI.txt')+',HomeCheck.Icons.LICENSE.txt'
    $resources+='/resource:'+(Join-Path $PSScriptRoot 'assets\LICENSE-Apache-2.0.txt')+',HomeCheck.Icons.Apache.txt'
    & C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:winexe /out:HomeCheck-Atlas-Symbole.exe /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Web.Extensions.dll /r:System.Xml.Linq.dll $resources $sources
    if($LASTEXITCODE -ne 0){throw 'Build fehlgeschlagen'}
} finally { Pop-Location }
