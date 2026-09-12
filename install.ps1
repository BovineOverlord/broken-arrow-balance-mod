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
    4. Writes steam_appid.txt + a Steam-aware launcher, and creates a
       "Broken Arrow (Modded)" desktop shortcut that launches the game WITHOUT
       EasyAntiCheat (required for mods) but WITH a Steam context (so it doesn't
       hang at "Loading Hangar")

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
function Test-GameDir($d){ if (-not $d) { return $false }; return [bool](Test-Path -LiteralPath ("$d\BrokenArrow.exe") -ErrorAction SilentlyContinue) }

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

# ---------- Steam context + modded launcher (fixes the "Loading Hangar" hang) ----------
# Launching BrokenArrow.exe directly gives the game no Steam context, so on legit Steam copies
# its online init blocks forever at the "Loading Hangar" screen (freeze/crash). steam_appid.txt
# supplies the app id for a non-Steam launch, and the launcher below makes sure the Steam client
# is running first - so the game boots with EasyAntiCheat off instead of hanging.
try {
  Set-Content -Path (Join-Path $game 'steam_appid.txt') -Value '1604270' -Encoding ascii -NoNewline
  Ok "Wrote steam_appid.txt (Broken Arrow app id 1604270)."
} catch { Warn ("Couldn't write steam_appid.txt: " + $_.Exception.Message) }

$launcher = Join-Path $game 'Launch Broken Arrow (Modded).bat'
$launcherBody = @'
@echo off
setlocal EnableExtensions
title Broken Arrow (Modded) Launcher
cd /d "%~dp0"
if not exist "%~dp0steam_appid.txt" ( >"%~dp0steam_appid.txt" echo 1604270 )
REM Make sure Steam is running so the game can get its auth ticket (no ticket = Loading Hangar hang).
tasklist /FI "IMAGENAME eq steam.exe" 2>nul | find /I "steam.exe" >nul
if errorlevel 1 (
    echo Steam is not running - starting it, please sign in if prompted...
    start "" "steam://open/main"
    set /a _t=0
    :w
    timeout /t 3 /nobreak >nul
    tasklist /FI "IMAGENAME eq steam.exe" 2>nul | find /I "steam.exe" >nul
    if not errorlevel 1 goto up
    set /a _t+=1
    if %_t% LSS 10 goto w
    :up
    timeout /t 5 /nobreak >nul
)
REM Launch the raw exe (no EACLauncher) so EasyAntiCheat stays off and MelonLoader loads.
start "" "%~dp0BrokenArrow.exe"
endlocal
'@
try {
  Set-Content -Path $launcher -Value $launcherBody -Encoding ascii
  Ok "Created 'Launch Broken Arrow (Modded).bat'."
} catch { Warn ("Couldn't write launcher: " + $_.Exception.Message) }

# ---------- modded launch shortcut (EAC off, Steam context on) ----------
if (-not $NoShortcut) {
  try {
    $lnk = Join-Path ([Environment]::GetFolderPath('Desktop')) 'Broken Arrow (Modded).lnk'
    $ws = New-Object -ComObject WScript.Shell
    $s = $ws.CreateShortcut($lnk)
    $s.TargetPath = $launcher
    $s.WorkingDirectory = $game
    $s.WindowStyle = 7   # minimized - hide the brief launcher console
    $s.IconLocation = (Join-Path $game 'BrokenArrow.exe')
    $s.Description = 'Launch Broken Arrow with mods (EasyAntiCheat off, Steam context on)'
    $s.Save()
    Ok "Created desktop shortcut: 'Broken Arrow (Modded)'."
  } catch { Warn ("Couldn't create desktop shortcut: " + $_.Exception.Message) }
}

Write-Host ''
Ok "$ModName is installed."
Write-Host ''
Write-Host 'DO NOT GET BANNED:' -ForegroundColor Red
Write-Host '  - Modified stats are server-checked online. Playing ONLINE with mods active'
Write-Host '    (Skirmish is online by default) can get your account BANNED.'
Write-Host '  - The modded shortcut runs with anti-cheat OFF, so you are never online with'
Write-Host '    mods. NEVER use the Steam Play button or EACLauncher.exe while the mod is in'
Write-Host '    Mods\. To play real online multiplayer, remove Mods\BABalanceMod.dll first.'
Write-Host ''
Write-Host 'HOW TO PLAY MODDED:' -ForegroundColor White
Write-Host '  - Make sure Steam is running and signed in, then launch with the new'
Write-Host '    "Broken Arrow (Modded)" desktop shortcut. (Steam must be running or the game'
Write-Host '    hangs at "Loading Hangar" - the shortcut/launcher handles the rest.)'
Write-Host '  - The first modded launch is slower (MelonLoader generates game assemblies).'
Write-Host '  - A black MelonLoader console window opening alongside the game is normal.'
Write-Host '  - Edit  _BAMod\overrides.json  to change unit stats, then relaunch.'
Write-Host '  - After the first launch, reference dumps of every unit/ammo appear in'
Write-Host '    _BAMod\dump\  (ids, names and current values to copy from).'
Write-Host ''
Read-Host 'Press Enter to close'
