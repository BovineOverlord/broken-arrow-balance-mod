#Requires -Version 5
<#
  BA Balance Mod - one-click installer for Broken Arrow
  --------------------------------------------------------------------------
  What it does:
    1. Finds your Broken Arrow install (Steam libraries, common paths, or asks)
    2. Installs MelonLoader v0.7.3 (IL2CPP) into the game folder if it isn't
       already there - downloaded straight from the official MelonLoader release
    3. Copies the mod into  <game>\Mods\
    3b. Seeds  _BAMod\overrides.json  (the file you edit) if you don't have one
    4. Creates a "Broken Arrow (Modded)" desktop shortcut that launches the
       game WITHOUT EasyAntiCheat (required for mods to load)

  Offline / single-player only. It does not touch or enable online multiplayer.
  Usage:
    Double-click install.bat  (recommended), or from PowerShell:
    powershell -ExecutionPolicy Bypass -File install.ps1 [-GameDir "D:\path\to\Broken Arrow"] [-ReinstallMelon] [-NoShortcut]
#>
[CmdletBinding()]
param(
  [string]$GameDir,
  [switch]$ReinstallMelon,
  [switch]$NoShortcut
)

$ErrorActionPreference = 'Stop'
try { [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12 } catch {}

# ---------- per-mod config ----------
$ModName       = 'BA Balance Mod'
$ModDll        = 'BABalanceMod.dll'
$SeedOverrides = $true
$MelonVersion  = 'v0.7.3'
$MelonUrl      = "https://github.com/LavaGang/MelonLoader/releases/download/$MelonVersion/MelonLoader.x64.zip"

$Here = Split-Path -Parent $MyInvocation.MyCommand.Path
function Info($m){ Write-Host "[*]  $m" -ForegroundColor Cyan }
function Ok($m){   Write-Host "[OK] $m" -ForegroundColor Green }
function Warn($m){ Write-Host "[!]  $m" -ForegroundColor Yellow }
function Die($m){  Write-Host "[X]  $m" -ForegroundColor Red; Write-Host ''; Read-Host 'Press Enter to close'; exit 1 }
function Test-GameDir($d){ return ($d -and (Test-Path (Join-Path $d 'BrokenArrow.exe'))) }

function Find-Game {
  if (Test-GameDir $GameDir) { return (Resolve-Path $GameDir).Path }
  foreach($c in @($Here, (Split-Path $Here -Parent))){ if(Test-GameDir $c){ return $c } }
  try {
    $steam = (Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction Stop).SteamPath
    if ($steam) {
      $vdf = Join-Path $steam 'steamapps\libraryfolders.vdf'
      $libs = @($steam)
      if (Test-Path $vdf) {
        Select-String -Path $vdf -Pattern '"path"\s+"([^"]+)"' | ForEach-Object {
          $libs += ($_.Matches[0].Groups[1].Value -replace '\\\\','\')
        }
      }
      foreach($lib in $libs){
        $cand = Join-Path $lib 'steamapps\common\Broken Arrow'
        if (Test-GameDir $cand){ return $cand }
      }
    }
  } catch {}
  foreach($c in @(
    'C:\Program Files (x86)\Steam\steamapps\common\Broken Arrow',
    'C:\Program Files\Steam\steamapps\common\Broken Arrow',
    'D:\Steam\steamapps\common\Broken Arrow',
    'D:\SteamLibrary\steamapps\common\Broken Arrow',
    'E:\SteamLibrary\steamapps\common\Broken Arrow',
    'D:\Games\Broken Arrow')){
    if (Test-GameDir $c){ return $c }
  }
  Warn "Couldn't find Broken Arrow automatically."
  $inp = Read-Host 'Paste the full path to your Broken Arrow folder (contains BrokenArrow.exe)'
  if (Test-GameDir $inp){ return (Resolve-Path $inp).Path }
  return $null
}

Write-Host ''
Write-Host "=== $ModName installer ===" -ForegroundColor White
$game = Find-Game
if (-not (Test-GameDir $game)) { Die "Broken Arrow not found. Re-run and paste the folder that contains BrokenArrow.exe." }
Ok "Game folder: $game"

# ---------- MelonLoader ----------
$mlDll = Join-Path $game 'MelonLoader\net6\MelonLoader.dll'
if ($ReinstallMelon -or -not (Test-Path $mlDll)) {
  Info "Installing MelonLoader $MelonVersion (IL2CPP) ..."
  $zip = Join-Path $env:TEMP ("MelonLoader.x64." + $MelonVersion + ".zip")
  try { Invoke-WebRequest -Uri $MelonUrl -OutFile $zip -UseBasicParsing }
  catch { Die ("MelonLoader download failed: " + $_.Exception.Message + "`nURL: " + $MelonUrl) }
  Info "Extracting into the game folder ..."
  Expand-Archive -Path $zip -DestinationPath $game -Force
  Remove-Item $zip -ErrorAction SilentlyContinue
  if (Test-Path $mlDll){ Ok "MelonLoader installed." } else { Die "MelonLoader did not extract correctly (missing $mlDll)." }
} else {
  Ok "MelonLoader already present - skipping."
}

# ---------- the mod ----------
$mods = Join-Path $game 'Mods'
New-Item -ItemType Directory -Force -Path $mods | Out-Null
$src = Join-Path $Here ("dist\" + $ModDll)
if (-not (Test-Path $src)) { Die "Can't find $src . Run this from inside the extracted repo folder (it needs the dist\ folder next to it)." }
Copy-Item $src (Join-Path $mods $ModDll) -Force
Ok "Installed $ModDll into Mods\"

if ($SeedOverrides) {
  $bamod = Join-Path $game '_BAMod'
  New-Item -ItemType Directory -Force -Path $bamod | Out-Null
  $ov = Join-Path $bamod 'overrides.json'
  if (-not (Test-Path $ov)) {
    Copy-Item (Join-Path $Here 'dist\overrides.example.json') $ov -Force
    Ok "Created _BAMod\overrides.json  (this is the file you edit to change stats)."
  } else {
    Warn "_BAMod\overrides.json already exists - left your version untouched."
  }
}

# ---------- modded launch shortcut (EAC off) ----------
if (-not $NoShortcut) {
  try {
    $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Broken Arrow (Modded).lnk'
    $ws = New-Object -ComObject WScript.Shell
    $s = $ws.CreateShortcut($lnk)
    $s.TargetPath = (Join-Path $game 'BrokenArrow.exe')
    $s.WorkingDirectory = $game
    $s.IconLocation = (Join-Path $game 'BrokenArrow.exe')
    $s.Description = 'Launch Broken Arrow with mods (EasyAntiCheat off)'
    $s.Save()
    Ok "Created desktop shortcut: 'Broken Arrow (Modded)'."
  } catch { Warn ("Couldn't create desktop shortcut: " + $_.Exception.Message) }
}

Write-Host ''
Ok "$ModName is installed."
Write-Host ''
Write-Host 'HOW TO PLAY MODDED:' -ForegroundColor White
Write-Host '  - Launch with the new "Broken Arrow (Modded)" desktop shortcut, or run'
Write-Host '    BrokenArrow.exe directly. Do NOT use the Steam Play button - it goes'
Write-Host '    through EasyAntiCheat, which blocks mods.'
Write-Host '  - The first modded launch is slower (MelonLoader generates game assemblies).'
Write-Host '  - A black MelonLoader console window opening alongside the game is normal.'
Write-Host '  - Edit  _BAMod\overrides.json  to change unit stats, then relaunch.'
Write-Host '  - After the first launch, reference dumps of every unit/ammo appear in'
Write-Host '    _BAMod\dump\  (ids, names and current values to copy from).'
Write-Host ''
Read-Host 'Press Enter to close'
