<#
# === Manual commands (reference) ===
# 1) Trainer (expects 8 envs on 6005..6012)
(mlagents) PS Y:\Drive\sync\UU\2_1\proj\Ai_game_project\Python> `
mlagents-learn fox2.yaml `
  --run-id=fox_par8_mix `
  --base-port=6005 `
  --num-envs=8

# 2) Workers (example for one port)
"Y:\Drive\sync\UU\2_1\proj\Ai_game_project\Python\Train_build\Ai_game_project.exe" `
  --mlagents-port=6005 -batchmode --fps=80 --quality=VeryLow `
  -screen-width 320 -screen-height 180 -screen-fullscreen 0 `
  -logFile "Y:\Drive\sync\UU\2_1\proj\Ai_game_project\Python\Train_build\logs\env_6005.txt"

# 3) Viewer (last port = 6012)
"Y:\Drive\sync\UU\2_1\proj\Ai_game_project\Python\Train_build\Ai_game_project.exe" `
  --mlagents-port=6012 --fps=80 --quality=VeryLow `
  -screen-width 1280 -screen-height 720 -screen-fullscreen 0 `
  -logFile "Y:\Drive\sync\UU\2_1\proj\Ai_game_project\Python\Train_build\logs\env_6012.txt"
# ====================================
#>

param(
  [string]$EnvExe   = "Y:\Drive\sync\UU\2_1\proj\Ai_game_project\Python\Train_build\Ai_game_project.exe",
  [string]$Yaml     = "Y:\Drive\sync\UU\2_1\proj\Ai_game_project\Python\fox2.yaml",
  [string]$RunId    = "fox_par8_mix",
  [int]$BasePort    = 6005,
  [int]$Fps         = 80,
  [string]$Quality  = "VeryLow",
  [int]$WorkerW     = 320,
  [int]$WorkerH     = 180,
  [int]$ViewerW     = 1280,
  [int]$ViewerH     = 720
)

function Test-File($p){ if(-not (Test-Path $p)){ throw "Missing: $p" } }
Test-File $EnvExe; Test-File $Yaml

$logDir = "Y:\Drive\sync\UU\2_1\proj\Ai_game_project\Python\Train_build\logs"
New-Item -ItemType Directory -Force -Path $logDir | Out-Null

# Trainer (expects 8 envs on BasePort..BasePort+7)
$trainer = Start-Process -PassThru powershell -ArgumentList @(
  "-NoExit","-Command",
  "conda activate mlagents; mlagents-learn `"$Yaml`" --run-id `"$RunId`" --num-envs 8 --base-port $BasePort"
)
Start-Sleep -Seconds 2

# Seven batchmode workers (BasePort..BasePort+6)
for($i=0; $i -lt 7; $i++){
  $p = $BasePort + $i
  Start-Process $EnvExe -ArgumentList @(
    "--mlagents-port=$p",
    "-batchmode",
    "--fps=$Fps",
    "--quality=$Quality",
    "-screen-width",$WorkerW,"-screen-height",$WorkerH,"-screen-fullscreen","0",
    "-logFile","$logDir\env_$p.txt"
  )
}

# One visible viewer (BasePort+7)
$viewerPort = $BasePort + 7
Start-Process $EnvExe -ArgumentList @(
  "--mlagents-port=$viewerPort",
  "--fps=$Fps",
  "--quality=$Quality",
  "-screen-width",$ViewerW,"-screen-height",$ViewerH,"-screen-fullscreen","0",
  "-logFile","$logDir\env_$viewerPort.txt"
)

"Trainer PID: $($trainer.Id)"
"Batch workers: $BasePort..$($BasePort+6)"
"Viewer: $viewerPort"
