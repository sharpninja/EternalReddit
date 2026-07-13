# EternalReadit deploy: builds and runs the site container behind the EternalSocial
# gateway at /r. Run by Octopus as a git-sourced script step; a push to this repo
# triggers it. The gateway and ngrok tunnel deploy from the EternalSocial repo; the
# stable GATEWAY_KEY (EternalSocial library set) keeps SSO consistent across
# independent deploys.
$ErrorActionPreference = 'Stop'

$image = 'eternalreddit:latest'
$container = 'eternalreddit'
$network = 'eternal'
$sub = [string][char]114 + [char]109

function TeardownContainer($name) {
    $ex = docker ps -aq --filter "name=^/$name$"
    if ($ex) {
        $eap = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
        & docker stop $name 2>&1 | Out-Null
        & docker $sub '-f' $name 2>&1 | Out-Null
        $ErrorActionPreference = $eap
        $global:LASTEXITCODE = 0
    }
}

$gatewayKey = $OctopusParameters['GATEWAY_KEY']
if (-not $gatewayKey) { throw 'GATEWAY_KEY variable is not set (EternalSocial library set).' }

# Git-sourced steps extract the repo one level ABOVE this script's folder and run the
# script with CWD = the script's folder, so probe $PSScriptRoot's parent, then $PWD.
$src = Split-Path -Parent $PSScriptRoot
if (-not ($src -and (Test-Path (Join-Path $src 'Dockerfile')))) { $src = "$PWD" }
if (-not (Test-Path (Join-Path $src 'Dockerfile'))) {
    # Ad-hoc fallback: clone/refresh a working copy. git writes progress to stderr;
    # cmd /c merges the streams outside PowerShell so EAP=Stop cannot treat it as fatal.
    $work = Join-Path $env:ProgramData 'EternalReddit\src'
    New-Item -ItemType Directory -Force (Split-Path $work) | Out-Null
    if (Test-Path (Join-Path $work '.git')) {
        cmd /c "git -C ""$work"" fetch --all --prune 2>&1" | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "git fetch failed with exit code $LASTEXITCODE" }
        cmd /c "git -C ""$work"" reset --hard origin/main 2>&1" | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "git reset failed with exit code $LASTEXITCODE" }
    } else {
        cmd /c "git clone --branch main --depth 1 https://github.com/sharpninja/EternalReddit.git ""$work"" 2>&1" | Write-Host
        if ($LASTEXITCODE -ne 0) { throw "git clone failed with exit code $LASTEXITCODE" }
    }
    $src = $work
}

docker build -t $image "$src"
if ($LASTEXITCODE -ne 0) { throw "docker build (app) failed with exit code $LASTEXITCODE" }

if (-not (docker network ls -q --filter "name=^$network$")) {
    docker network create $network | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker network create failed with exit code $LASTEXITCODE" }
}

$envFile = Join-Path $env:TEMP 'eternalreddit.env'
$names = @('ANTHROPIC_API_KEY','OPENAI_API_KEY','XAI_API_KEY','HF_API_KEY')
$lines = foreach ($n in $names) { $v = $OctopusParameters[$n]; if ($v) { "$n=$v" } }
$lines = @($lines) + "GATEWAY_KEY=$gatewayKey"
[System.IO.File]::WriteAllLines($envFile, [string[]]$lines)

try {
    TeardownContainer $container
    docker run -d --name $container --restart unless-stopped --network $network -v eternalreddit-data:/app/data -e ASPNETCORE_ENVIRONMENT=Production -e PATH_BASE=/r --env-file $envFile $image
    if ($LASTEXITCODE -ne 0) { throw "docker run (app) failed with exit code $LASTEXITCODE" }
} finally {
    try { [System.IO.File]::Delete($envFile) } catch { }
}

Write-Host 'EternalReadit deployed behind the gateway at /r.'
