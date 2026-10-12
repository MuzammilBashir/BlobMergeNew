<#
  Pressure/softness check: drops a 256, 64, 16 and 4 on the floor and photographs them at rest, once per pressure.
  Uses the last test build (run Tools\test.ps1 first).
    & F:\Projects\BlobMerge\Tools\sag.ps1 -pressures 2.5,3.5,6 [-shape 0.03]
  Logs "sag p=<p> <value>: width/height=<ratio> area=<fraction>" and writes _testout\shotsag_<p>.png
#>
param([string[]]$pressures = @("2.5","3.5","6"), [string]$shape = "")

$root  = Split-Path $PSScriptRoot -Parent
$exe   = Join-Path $root "_testclone\Builds\WinTest\BlobMerge.exe"
$out   = Join-Path $root "_testout"
New-Item -ItemType Directory -Force $out | Out-Null
Remove-Item "$out\shotsag_*.png" -ErrorAction SilentlyContinue
foreach ($pr in $pressures) {
  $a = @('-screen-fullscreen','0','-screen-width','540','-screen-height','1100','-blobsag','-blobpressure',$pr,
         '-blobshot',"$out\shot",'-logFile',"$out\sag_$pr.log")
  if ($shape -ne "") { $a += @('-blobshape', $shape) }
  $g = Start-Process $exe -ArgumentList $a -PassThru
  $null = $g.WaitForExit(40000)
  Select-String "$out\sag_$pr.log" -Pattern "\] sag|Exception" | % Line
}
Get-ChildItem "$out\shotsag_*.png" | % Name
