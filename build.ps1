# Builds Pastel.exe using the in-box .NET Framework 4.8 compiler (no SDK needed)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$src  = Join-Path $root 'src'
$fw   = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319"

if (-not (Test-Path (Join-Path $src 'pastel.ico'))) {
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $src 'gen-icon.ps1')
}

$args = @(
    '/nologo', '/target:winexe', '/platform:anycpu', '/optimize+',
    "/out:$root\Pastel.exe",
    "/win32icon:$src\pastel.ico",
    "/win32manifest:$src\app.manifest",
    "/resource:$root\assets\pastel-logo.png,Pastel.Logo.png",
    "/lib:$fw\WPF",
    '/r:System.Core.dll',
    '/r:PresentationFramework.dll',
    '/r:PresentationCore.dll',
    '/r:WindowsBase.dll',
    '/r:System.Xaml.dll',
    '/r:System.Windows.Forms.dll',
    '/r:System.Drawing.dll',
    '/r:System.Web.Extensions.dll',
    "$src\Pastel.cs"
)
& "$fw\csc.exe" @args

if ($LASTEXITCODE -eq 0) { Write-Host "Build OK -> $root\Pastel.exe" } else { Write-Host "Build FAILED"; exit 1 }
