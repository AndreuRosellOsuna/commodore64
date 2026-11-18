# Builds the release files: the self-contained single-file exe and the Inno Setup installer.
# Usage (Windows, PowerShell 7):  ./build/package.ps1 -Version 1.0.0
# Output: publish/C64Console.exe and installer/output/C64Console-<version>-setup.exe
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string] $Version
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

dotnet publish src/C64Console.App -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:Version=$Version -o publish
if ($LASTEXITCODE) { exit $LASTEXITCODE }

# Inno Setup 6 is on GitHub's Windows images; install it if this machine doesn't have it.
$iscc = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
if (-not (Test-Path $iscc)) {
    choco install innosetup --no-progress -y
    if ($LASTEXITCODE) { exit $LASTEXITCODE }
}

& $iscc "/DAppVersion=$Version" installer/C64Console.iss
if ($LASTEXITCODE) { exit $LASTEXITCODE }

Get-ChildItem publish/C64Console.exe, installer/output/*.exe | Format-Table Name, Length
