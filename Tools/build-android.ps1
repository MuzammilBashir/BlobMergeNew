<#
  Build the Android APK from the private mirror (works while the user has the Editor open) and copy it to
  BlobMergeUnity\Builds\BlobMerge.apk.
  MUST run outside the agent sandbox (Gradle opens a loopback socket) — e.g. PowerShell tool with
  dangerouslyDisableSandbox, after the user approves.
#>
param([string]$scene = "Game")   # Game | Hexa | Fish (-> BlobMerge_Hexa.apk / BlobMerge_Fish.apk, own app ids)
$root  = Split-Path $PSScriptRoot -Parent
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.67f1\Editor\Unity.exe"
$src   = Join-Path $root "BlobMergeUnity"
$clone = Join-Path $root "_testclone"
$out   = Join-Path $root "_testout"
New-Item -ItemType Directory -Force $out | Out-Null
foreach ($d in "Assets","Packages","ProjectSettings") {
  robocopy (Join-Path $src $d) (Join-Path $clone $d) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
}
# Gradle fails with "Unable to establish loopback connection" if TEMP is the agent's long session temp path.
# These are restored below: the caller's session keeps running, and a TEMP pointing at the folder this script
# deletes breaks every later build in it.
$oldTemp = $env:TEMP; $oldTmp = $env:TMP; $oldGradleHome = $env:GRADLE_USER_HOME
$tmp = Join-Path $root "tmp"; New-Item -ItemType Directory -Force $tmp | Out-Null
$env:TEMP = $tmp; $env:TMP = $tmp
# ... and ninja hits "Filename longer than 260 characters" if GRADLE_USER_HOME is one too (the agent sandbox cache),
# so pin it to the real home, which is short and already holds the caches
$env:GRADLE_USER_HOME = Join-Path $env:USERPROFILE ".gradle"
$b = Start-Process $unity -ArgumentList '-batchmode','-quit','-projectPath',$clone,'-buildTarget','Android',
     '-executeMethod','BlobMerge.EditorTools.ProjectSetup.SetupAndBuildAndroid','-blobscene',$scene,'-logFile',"$out\abuild.log" -PassThru
$b.WaitForExit()
Remove-Item -Recurse -Force $tmp -ErrorAction SilentlyContinue
$env:TEMP = $oldTemp; $env:TMP = $oldTmp; $env:GRADLE_USER_HOME = $oldGradleHome
Select-String -Path "$out\abuild.log" -Pattern "error CS|Build result|What went wrong" -Context 0,2 | Select -First 6 | % { $_.Line; $_.Context.PostContext }
$apkName = if ($scene -eq "Hexa") { "BlobMerge_Hexa.apk" } elseif ($scene -in "Fish","FishFantasy") { "BlobMerge_Fish.apk" } else { "BlobMerge.apk" }
$apk = Join-Path $clone "Builds\$apkName"
if (Test-Path $apk) {
  New-Item -ItemType Directory -Force (Join-Path $src "Builds") | Out-Null
  Copy-Item $apk (Join-Path $src "Builds\$apkName") -Force
  Get-Item (Join-Path $src "Builds\$apkName") | Select FullName, Length, LastWriteTime
}
