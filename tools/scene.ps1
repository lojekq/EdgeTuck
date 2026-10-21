# Edge Tuck scene pictures on DISPLAY2 (plan 04 §10): docs/demo.gif, docs/hero-dark.png, docs/tabs-closeup-light.png.
#   pwsh tools/scene.ps1                     all three
#   pwsh tools/scene.ps1 -Only closeup       just one: gif | hero | closeup
# The owner's slot: DISPLAY2 at 100 %, Do not disturb on, a working Edge Tuck closed (its hotkeys and tabs).
# --autoplay covers DISPLAY2 with a plain backdrop and moves the real cursor; do not touch the mouse while it runs.
# Takes the desktop lock C:\Demo\desktop.lock (00-shared §9.6): waits while another session holds it, never removes it.
param([ValidateSet('all', 'gif', 'hero', 'closeup')][string]$Only = 'all')
$ErrorActionPreference = 'Stop'; $PSNativeCommandUseErrorActionPreference = $true
if (Get-Process EdgeTuck -ErrorAction Ignore | Where-Object { $_.CommandLine -notlike '*--demo*' }) { throw 'Exit the working Edge Tuck first (tray → Exit)' }
dotnet build src/EdgeTuck -c Release | Out-Null
$exe = 'src/EdgeTuck/bin/Release/net10.0-windows/EdgeTuck.exe'
$rect = "$env:TEMP\EdgeTuck-scene-rect.txt"

function Still([string]$Flag, [string]$Out, [int]$Mouse, [string]$Scale) {
  Remove-Item $rect -ErrorAction Ignore
  $lock = 'C:\Demo\desktop.lock'
  while ($true) {
    try { New-Item $lock -Value 'EdgeTuck' -ErrorAction Stop | Out-Null; break }
    catch { if (-not (Test-Path $lock)) { throw }; Write-Host "desktop.lock is held by $(Get-Content $lock -ErrorAction Ignore), waiting..."; Start-Sleep 10 }
  }
  try {
    $proc = Start-Process $exe -PassThru -ArgumentList '--demo', '--autoplay', $Flag, '--rect-file', $rect
    $deadline = (Get-Date).AddSeconds(30)
    while (-not (Test-Path $rect)) {
      if ((Get-Date) -gt $deadline -or $proc.HasExited) { throw 'the app did not write --rect-file' }
      Start-Sleep -Milliseconds 100
    }
    Start-Sleep -Milliseconds 500
    $x, $y, $w, $h = (Get-Content $rect) -split ' '
    $vf = if ($Scale) { @('-vf', $Scale) } else { @() }
    ffmpeg -y -loglevel error -f gdigrab -draw_mouse $Mouse -offset_x $x -offset_y $y -video_size "${w}x${h}" -i desktop -frames:v 1 @vf $Out
    $proc | Stop-Process -ErrorAction Ignore
  } finally { Remove-Item $lock }
  Write-Host "$Out written: open it and look at it (a monitor that is asleep gives a frozen frame)"
}

New-Item -ItemType Directory docs -Force | Out-Null
if ($Only -in 'all', 'hero') { Still '--hero' 'docs/hero-dark.png' 1 '' }
if ($Only -in 'all', 'closeup') { Still '--closeup' 'docs/tabs-closeup-light.png' 0 'scale=iw*2:-1:flags=neighbor' }
if ($Only -in 'all', 'gif') { pwsh tools/gif.ps1 -Scene -X 3440 -Y 1500 -W 1440 -H 810 -Seconds 9 -Out docs/demo.gif }
oxipng -o 4 --strip safe -q docs/hero-dark.png docs/tabs-closeup-light.png 2>$null
