# Builds Stickers.exe with the .NET Framework compiler that ships with Windows - nothing to install.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$fw = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"
$wpf = "$fw\WPF"
$exe = "$root\Stickers.exe"

if (-not (Test-Path "$root\assets\stickers.ico")) { & "$root\tools\make-icon.ps1" }

# If the app is running, ask it to exit gracefully (it saves first).
if (Get-Process Stickers -ErrorAction SilentlyContinue) {
    & $exe --exit
    for ($i = 0; $i -lt 50 -and (Get-Process Stickers -ErrorAction SilentlyContinue); $i++) { Start-Sleep -Milliseconds 100 }
}

$refs = @('System.dll', 'System.Core.dll', 'System.Xaml.dll', 'System.Xml.dll', 'System.Drawing.dll',
          'System.Windows.Forms.dll', 'System.Web.Extensions.dll',
          "$wpf\WindowsBase.dll", "$wpf\PresentationCore.dll", "$wpf\PresentationFramework.dll") | ForEach-Object { "/r:$_" }
$res = @(Get-ChildItem "$root\fonts\*.ttf" | ForEach-Object { "/resource:$($_.FullName),Stickers.Fonts.$($_.Name)" })
$res += "/resource:$root\assets\stickers.ico,Stickers.icon.ico"

& "$fw\csc.exe" /nologo /target:winexe /optimize+ /codepage:65001 "/out:$exe" "/win32icon:$root\assets\stickers.ico" $refs $res "$root\src\*.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }
"Done: $exe ($([Math]::Round((Get-Item $exe).Length / 1KB)) KB)"
