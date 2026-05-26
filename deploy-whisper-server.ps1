# deploy-whisper-server.ps1
# Unpack and run the prebuilt whisper-server bundle on a Windows + Vulkan GPU box.
# No compiler or winget needed -- the runtime ships in whisper-server-bundle.zip (~19 MB);
# the model (~575 MB) is downloaded from HuggingFace on first run.
#
# Prereq on the target box: a working Vulkan GPU driver (AMD/Intel/NVIDIA all OK).
# The driver provides vulkan-1.dll system-wide. Run `vulkaninfo --summary` to verify.
#
# Usage:
#   .\deploy-whisper-server.ps1                       # unzip + download model + smoke test
#   .\deploy-whisper-server.ps1 -InstallDir D:\whisper -Port 8088
#   .\deploy-whisper-server.ps1 -Model ggml-small.bin # smaller alternative (~466 MB)
#   .\deploy-whisper-server.ps1 -AutoStart            # also register Scheduled Task for auto-start at logon
#   .\deploy-whisper-server.ps1 -StartNow             # leave the server running after the smoke test
#   .\deploy-whisper-server.ps1 -SkipModelDownload    # use a model you've already copied into models\

[CmdletBinding()]
param(
    [string]$Zip            = (Join-Path $PSScriptRoot 'whisper-server-bundle.zip'),
    [string]$InstallDir     = (Join-Path $env:USERPROFILE 'whisper-server'),
    [int]$Port              = 8088,
    [string]$BindHost       = '0.0.0.0',
    [string]$InferencePath  = '/v1/audio/transcriptions',
    [string]$Model          = 'ggml-large-v3-turbo-q5_0.bin',
    [string]$ModelURL,
    [switch]$AutoStart,
    [switch]$StartNow,
    [switch]$SkipExtract,
    [switch]$SkipModelDownload
)

$ErrorActionPreference = 'Stop'

function Step($msg) { Write-Host "`n=== $msg ===" -ForegroundColor Cyan }

# --- 1. Vulkan driver sanity check -----------------------------------------
Step 'Checking Vulkan driver'
$vk = Join-Path $env:SystemRoot 'System32\vulkan-1.dll'
if (-not (Test-Path $vk)) {
    Write-Error @'
vulkan-1.dll not found in System32. This usually means the GPU driver doesn't ship a Vulkan ICD.
Fix: install the latest GPU driver (AMD Adrenalin / NVIDIA GeForce / Intel Arc), reboot, then re-run.
'@
}
Write-Host "Found $vk"

# --- 2. Extract bundle ------------------------------------------------------
if ($SkipExtract -and (Test-Path (Join-Path $InstallDir 'whisper-server.exe'))) {
    Step "Skipping extract (-SkipExtract); using existing $InstallDir"
} else {
    if (-not (Test-Path $Zip)) {
        Write-Error "Bundle zip not found at $Zip. Pass -Zip <path> or place whisper-server-bundle.zip next to this script."
    }
    Step "Extracting $Zip to $InstallDir"
    if (Test-Path $InstallDir) { Remove-Item -Recurse -Force $InstallDir }
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    Expand-Archive -Path $Zip -DestinationPath $InstallDir -Force
}

$serverExe = Join-Path $InstallDir 'whisper-server.exe'
$modelsDir = Join-Path $InstallDir 'models'
New-Item -ItemType Directory -Force -Path $modelsDir | Out-Null

# --- 3. Ensure a model is present -------------------------------------------
$modelPath = Join-Path $modelsDir $Model
if (-not (Test-Path $modelPath)) {
    if ($SkipModelDownload) {
        Write-Error "Model $Model not found under $modelsDir and -SkipModelDownload was passed. Copy a .bin file in or drop -SkipModelDownload."
    }
    if (-not $ModelURL) {
        # ggerganov/whisper.cpp on HuggingFace hosts the canonical ggml-*.bin files.
        $ModelURL = "https://huggingface.co/ggerganov/whisper.cpp/resolve/main/$Model"
    }
    Step "Downloading model $Model"
    Write-Host "From: $ModelURL"
    Write-Host "To:   $modelPath"
    Write-Host '(large-v3-turbo-q5_0 is ~575 MB; first download can take several minutes)'
    # Use Start-Process so curl's stderr progress bar doesn't get wrapped as a PowerShell
    # error record (PS 5.1 with $ErrorActionPreference=Stop aborts on the first stderr line otherwise).
    $curlProc = Start-Process -FilePath 'curl.exe' `
        -ArgumentList @('-L','--fail','--progress-bar','-o', $modelPath, $ModelURL) `
        -NoNewWindow -PassThru -Wait
    if ($curlProc.ExitCode -ne 0) {
        Write-Error "Model download failed (curl exit $($curlProc.ExitCode)). Check network / URL and re-run."
    }
    $size = (Get-Item $modelPath).Length
    if ($size -lt 50MB) {
        Remove-Item $modelPath -Force
        Write-Error "Downloaded file is only $size bytes -- likely an HTML error page. Aborted."
    }
    Write-Host ('Model size: {0:N1} MB' -f ($size / 1MB))
}

# --- 4. Smoke test ----------------------------------------------------------
Step 'Starting whisper-server for smoke test'
$srvArgs = @(
    '-m', $modelPath,
    '--host', $BindHost,
    '--port', $Port,
    '--inference-path', $InferencePath
)
$outLog = Join-Path $env:TEMP 'whisper-server.out.log'
$errLog = Join-Path $env:TEMP 'whisper-server.err.log'
$srv = Start-Process -FilePath $serverExe -ArgumentList $srvArgs `
        -WorkingDirectory $InstallDir `
        -RedirectStandardOutput $outLog `
        -RedirectStandardError  $errLog `
        -PassThru

# Poll for readiness (up to 30s; first model load can be slow off cold disk)
$ready = $false
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep -Seconds 1
    if ($srv.HasExited) { break }
    try {
        $r = Invoke-WebRequest -Uri "http://127.0.0.1:$Port/" -TimeoutSec 1 -UseBasicParsing
        if ($r.StatusCode -eq 200) { $ready = $true; break }
    } catch { }
}
if (-not $ready) {
    Write-Host '--- stderr ---'
    Get-Content $errLog -ErrorAction SilentlyContinue | Select-Object -Last 30
    if (-not $srv.HasExited) { Stop-Process -Id $srv.Id -Force }
    Write-Error "whisper-server didn't come up on port $Port within 30s."
}

$jfk = Join-Path $InstallDir 'samples\jfk.wav'
if (Test-Path $jfk) {
    Step 'Transcribing samples\jfk.wav'
    $resp = curl.exe -s -X POST "http://127.0.0.1:$Port$InferencePath" `
        "-F" "file=@$jfk;type=audio/wav" `
        "-F" "response_format=json" `
        "-F" "temperature=0" `
        -m 60
    Write-Host $resp
    if ($resp -notmatch 'ask not what your country') {
        if (-not $srv.HasExited) { Stop-Process -Id $srv.Id -Force }
        Write-Error 'Smoke test failed: expected phrase not in transcription response.'
    }
    Step 'Smoke test PASSED'
} else {
    Write-Warning 'jfk.wav not in bundle; skipping transcription check.'
}

if (-not $StartNow) {
    if (-not $srv.HasExited) { Stop-Process -Id $srv.Id -Force }
    Write-Host 'Server stopped (pass -StartNow to leave it running).'
}

# --- 5. Optional auto-start via Task Scheduler ------------------------------
if ($AutoStart) {
    Step "Registering Scheduled Task 'WhisperServer' for auto-start at logon"
    $argString = '-m "{0}" --host {1} --port {2} --inference-path {3}' -f $modelPath,$BindHost,$Port,$InferencePath
    $action   = New-ScheduledTaskAction -Execute $serverExe -Argument $argString -WorkingDirectory $InstallDir
    $trigger  = New-ScheduledTaskTrigger -AtLogOn -User $env:USERNAME
    $settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1)
    Register-ScheduledTask -TaskName 'WhisperServer' -Action $action -Trigger $trigger -Settings $settings -Force | Out-Null
    Write-Host 'Task registered. Start now with: Start-ScheduledTask -TaskName WhisperServer'
    Write-Host 'View status with:                Get-ScheduledTask    -TaskName WhisperServer | Get-ScheduledTaskInfo'
    Write-Host 'Remove with:                     Unregister-ScheduledTask -TaskName WhisperServer -Confirm:$false'
}

# --- 6. Done ----------------------------------------------------------------
Step 'Done'
Write-Host "Install dir   : $InstallDir"
Write-Host "Server binary : $serverExe"
Write-Host "Model         : $modelPath"
Write-Host "Endpoint      : http://${BindHost}:${Port}${InferencePath}"
Write-Host ''
Write-Host 'Run manually:'
$cmd = '  & "{0}" -m "{1}" --host {2} --port {3} --inference-path {4}' -f $serverExe,$modelPath,$BindHost,$Port,$InferencePath
Write-Host $cmd
Write-Host ''
Write-Host 'Test from another box:'
$test = "  curl -X POST http://<gpu-box>:${Port}${InferencePath} -F 'file=@your.wav;type=audio/wav' -F 'response_format=verbose_json'"
Write-Host $test
