param()
$ErrorActionPreference = 'Stop'
$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$Csproj = Join-Path $ProjectRoot 'YMusicMini.csproj'
$Iss = Join-Path $ProjectRoot 'installer\YMusicMini.iss'
$PublishPath = Join-Path $ProjectRoot 'publish\win-x64'
$InstallerPath = Join-Path $ProjectRoot 'artifacts\installers\YMusicMini-Setup-0.6.12.exe'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '.NET 10 SDK not found. Install the .NET Desktop Development workload in Visual Studio.'
}
Write-Host 'Publishing YMusic Mini: Release, win-x64, self-contained...'
& dotnet publish $Csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -p:PublishTrimmed=false -o $PublishPath
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }
if (-not (Test-Path (Join-Path $PublishPath 'YMusicMini.exe'))) {
    throw 'YMusicMini.exe missing from publish output.'
}

$CompilerCandidates = @(
    "${env:ProgramFiles(x86)}\Inno Setup 7\ISCC.exe",
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
)
$Compiler = $CompilerCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $Compiler) { throw 'Inno Setup Compiler (ISCC.exe) not found. Install Inno Setup from https://jrsoftware.org/isinfo.php' }
Write-Host "Compiling installer with: $Compiler"
& $Compiler $Iss
if ($LASTEXITCODE -ne 0) { throw "Inno Setup compile failed with exit code $LASTEXITCODE" }
if (-not (Test-Path $InstallerPath)) { throw "Installer file not found at $InstallerPath" }
Write-Host "SUCCESS: $InstallerPath"
Get-FileHash $InstallerPath -Algorithm SHA256 | Format-List
