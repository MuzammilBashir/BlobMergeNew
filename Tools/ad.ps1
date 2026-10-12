<#
  Record gameplay ads with the AdSim persona hand (Assets/AdSim + Assets/Scripts/Ads/BlobMergeDirector.cs).
  Safe while the user has the project open: records from a private mirror (default F:\Projects\BlobMerge\_testclone;
  Unity batchmode, GPU, no -nographics). Needs ffmpeg on PATH. Every video is ONE continuous take - no cuts or edits.

  Usage (PowerShell):
    & F:\Projects\BlobMerge\Tools\ad.ps1                                   # normal persona, seed 1, 59 s, 1080x1920
    & F:\Projects\BlobMerge\Tools\ad.ps1 -persona noob -seed 3 -name noob3
    & F:\Projects\BlobMerge\Tools\ad.ps1 -persona pro -variants 59,45,30 -gameArgs "prefill=16,8,4,2"
    & F:\Projects\BlobMerge\Tools\ad.ps1 -persona pro -mirror _testclone2  # parallel runs: one mirror per run
    -variants 59,45,30   one separate full take per length (<name>_59s/_45s/_30s.mp4): same persona, seed, prefill
           and music track; each ends with the endcard. No cutting, no trimming.
    -mirror <folder>     mirror project to record in (each parallel run needs its own; created from _testclone)
    -msaa 1|2|4|8   anti-aliasing (default 4)        -secs  take length when not using -variants (default 59)
    -endcard blasttheblob_playnow|<name>|<path>|none   (default blasttheblob_playnow)
    -music auto|chill|upbeat|hype|<file in Sounds\>|<path>|none   (auto = random track of the persona's category in
           Sounds\music_catalog.json; -16 LUFS, start offset applied, catalog volume 50%)   -musicVol 0.5
  Output: F:\Projects\BlobMerge\_adout\<name>.mp4, <name>.json (take log), <name>_sheet.png, <name>.log
#>
param([string]$persona = "normal", [int]$seed = 1, [float]$secs = 59, [string]$name = "",
      [int]$width = 1080, [int]$height = 1920, [int]$fps = 30, [int]$msaa = 4, [switch]$nocopy, [switch]$verbose,
      [string]$gameArgs = "", [string]$endcard = "blasttheblob_playnow", [string]$music = "auto", [float]$musicVol = -1,
      [string]$variants = "", [string]$mirror = "_testclone", [string]$scene = "Game")

$root  = Split-Path $PSScriptRoot -Parent
$unity = "C:\Program Files\Unity\Hub\Editor\6000.0.67f1\Editor\Unity.exe"
$src   = Join-Path $root "BlobMergeUnity"
$clone = Join-Path $root $mirror
$out   = Join-Path $root "_adout"
New-Item -ItemType Directory -Force $out | Out-Null
if (-not $name) { $name = "$persona-s$seed" -replace '[\\/:]', '_' }

# a new parallel mirror starts as a full copy of the main one (keeps its imported Library: no re-import)
if (-not (Test-Path (Join-Path $clone "Library"))) {
  "creating mirror $clone ..."
  robocopy (Join-Path $root "_testclone") $clone /MIR /NFL /NDL /NJH /NJS /NP /XD Temp Logs | Out-Null
}
if (-not $nocopy) {
  foreach ($d in "Assets","Packages","ProjectSettings") {
    robocopy (Join-Path $src $d) (Join-Path $clone $d) /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
  }
}

$personaPath = if (Test-Path $persona) { (Resolve-Path $persona).Path } else { "Assets/AdSim/Personas/$persona.json" }
$endcardPath = if ($endcard -eq "none" -or -not $endcard) { "" } elseif (Test-Path $endcard) { (Resolve-Path $endcard).Path } else { "Assets/AdSim/Endcards/$endcard.json" }
$lengths = if ($variants) { $variants -split "," | % { [float]$_ } } else { @($secs) }
$maxLen = ($lengths | Measure-Object -Maximum).Maximum

# music: one track per persona/run (the same for every length), start offset cut, loudness normalised
$musicArg = ""
if ($music -and $music -ne "none") {
  $lib = Join-Path $root "Sounds"
  $cat = Get-Content (Join-Path $lib "music_catalog.json") -Raw | ConvertFrom-Json
  $all = @(); foreach ($c in $cat.categories.PSObject.Properties) { foreach ($t in $c.Value.tracks) { $all += [pscustomobject]@{ cat = $c.Name; file = $t.file; start = $t.start } } }
  $want = if ($music -eq "auto") { $cat.personaCategory.$persona } else { $music }
  if (-not $want) { $want = "upbeat" }
  $pick = $all | ? { $_.cat -eq $want } | Get-Random
  if (-not $pick) { $pick = $all | ? { $_.file -eq $music } | Select -First 1 }
  if (-not $pick -and (Test-Path $music)) { $pick = [pscustomobject]@{ cat = "custom"; file = (Resolve-Path $music).Path; start = 0 } }
  if ($pick) {
    $srcFile = if ([IO.Path]::IsPathRooted($pick.file)) { $pick.file } else { Join-Path $lib $pick.file }
    $prepared = Join-Path $out "${name}_music.wav"
    ffmpeg -y -v error -ss $pick.start -t ($maxLen + 2) -i $srcFile -af "loudnorm=I=$($cat.normalizeLufs):TP=-2:LRA=11" -ar 48000 -ac 2 $prepared
    $vol = if ($musicVol -ge 0) { $musicVol } else { $cat.volume }
    $musicArg = "music=$prepared;musicVol=$vol"
    "music: [$($pick.cat)] $($pick.file) from $($pick.start)s at $vol"
  } else { "music: nothing matched '$music' - recording without music" }
}
$takeArgs = ((@($gameArgs, $musicArg) + $(if ($verbose) { "verbose=1" })) | ? { $_ }) -join ";"

foreach ($len in $lengths) {
  $recName = if ($variants) { "${name}_$([int]$len)s" } else { $name }
  $req = [ordered]@{
    scene = "Assets/Scenes/$scene.unity"; director = "BlobMerge.Ads.BlobMergeDirector, Assembly-CSharp"
    persona = $personaPath; hand = "Assets/AdSim/Hands/cartoon_hand.json"; endcard = $endcardPath
    output = (Join-Path $out "$recName.mp4"); width = $width; height = $height; fps = $fps; msaa = $msaa; seed = $seed
    maxSeconds = $len; args = $takeArgs; ffmpeg = "ffmpeg"
  }
  $reqPath = Join-Path $out "$recName.request.json"
  [IO.File]::WriteAllText($reqPath, ($req | ConvertTo-Json), (New-Object Text.UTF8Encoding $false))
  Remove-Item (Join-Path $out "$recName.mp4") -ErrorAction SilentlyContinue
  $log = Join-Path $out "$recName.log"
  $p = Start-Process $unity -ArgumentList '-batchmode','-projectPath',$clone,
       '-executeMethod','AdSim.EditorTools.AdSimLauncher.RunFromCommandLine','-adRequest',$reqPath,'-logFile',$log -PassThru
  if (-not $p.WaitForExit(900000)) { $p | Stop-Process -Force; "unity timed out (killed)" }
  Select-String -Path $log -Pattern "error CS|\[AdSim\].*(summary|cap|endcard|music|audio|take finished)|Exception" | % Line | Select -Unique
  $mp4 = Join-Path $out "$recName.mp4"
  if (Test-Path $mp4) {
    ffmpeg -y -loglevel error -i $mp4 -vf "fps=1/3,scale=216:-1,tile=5x4" -frames:v 1 (Join-Path $out "${recName}_sheet.png")
    "video: $mp4"
  } else { "no video produced: $recName" }
}
