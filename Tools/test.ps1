<#
  Build a Windows test player from a private mirror of the project and let the DebugAutopilot bot play it.
  Safe while the user has the project open in the Unity Editor (builds from F:\Projects\BlobMerge\_testclone).

  Usage (PowerShell):
    & F:\Projects\BlobMerge\Tools\test.ps1 -secs 60            # build + play 60 s
    & F:\Projects\BlobMerge\Tools\test.ps1 -secs 60 -nobuild   # replay the last build
    & F:\Projects\BlobMerge\Tools\test.ps1 -secs 60 -scene Hexa # build + play the Hexa scene (hexagons)
  Output: F:\Projects\BlobMerge\_testout\shot*.png (screenshots), player.log ([BlobMerge] lines), wbuild.log
#>
param([int]$secs = 30, [switch]$nobuild, [int]$width = 540, [int]$height = 1100, [string[]]$extra = @(), [string]$scene = "Game")

$root  = Split-Path $PSScriptRoot -Parent                       # F:\Projects\BlobMerge
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.67f1\Editor\Unity.exe"
$src   = Join-Path $root "BlobMergeUnity"
$clone = Join-Path $root "_testclone"                           # short path on purpose (long 8.3 paths broke URP loading)
$out   = Join-Path $root "_testout"
New-Item -ItemType Directory -Force $out | Out-Null

if (-not $nobuild) {
  foreach ($d in "Assets","Packages","ProjectSettings") {
    robocopy (Join-Path $src $d) (Join-Path $clone $d) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
  }
  $b = Start-Process $unity -ArgumentList '-batchmode','-quit','-projectPath',$clone,
       '-executeMethod','BlobMerge.EditorTools.ProjectSetup.BuildWindowsTest','-blobscene',$scene,'-logFile',"$out\wbuild.log" -PassThru
  $b.WaitForExit()
  Select-String -Path "$out\wbuild.log" -Pattern "error CS|Win build result|executeMethod method .* threw" | Select -First 15 | % Line
}

Get-Process BlobMerge -ErrorAction SilentlyContinue | Stop-Process -Force   # stale test players
Remove-Item "$out\shot*.png" -ErrorAction SilentlyContinue
$args2 = @('-screen-fullscreen','0','-screen-width',"$width",'-screen-height',"$height",
           '-blobauto',"$secs",'-blobshot',"$out\shot",'-logFile',"$out\player.log") + $extra
$g = Start-Process (Join-Path $clone "Builds\WinTest\BlobMerge.exe") -ArgumentList $args2 -PassThru
$null = $g.WaitForExit(($secs + 40) * 1000)
if (-not $g.HasExited) { $g | Stop-Process -Force; "player timed out (killed)" }
Get-ChildItem "$out\shot*.png" | % Name
Select-String "$out\player.log" -Pattern "Exception|\] summary|\] t=" | % Line | Select -Unique
