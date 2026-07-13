# EternalSocial core deploy: the gateway, EternalReadit, and the ngrok tunnel.
# Run by Octopus as a git-sourced script step; a push to this repo triggers it.
# EternalX and EternalDiscord deploy from their own repos' scripts and are NOT
# restarted here - the stable GATEWAY_KEY (EternalSocial library set) keeps SSO
# consistent across independent deploys.
$ErrorActionPreference = 'Stop'

$image = 'eternalreddit:latest'
$proxyImage = 'eternalsocial-proxy:latest'
$container = 'eternalreddit'
$proxy = 'eternalsocial-proxy'
$ngrok = 'eternalreddit-ngrok'
$network = 'eternal'
$hostPort = 8090
$domain = 'eternalsocial.ngrok.app'
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

function Resolve-Source($repoUrl, $workLeaf) {
    # Git-sourced steps materialize the repo in the working directory; otherwise clone.
    if (Test-Path (Join-Path $PWD 'Dockerfile')) { return "$PWD" }
    $work = Join-Path $env:ProgramData $workLeaf
    New-Item -ItemType Directory -Force (Split-Path $work) | Out-Null
    if (Test-Path (Join-Path $work '.git')) {
        git -C $work fetch --all --prune
        git -C $work reset --hard origin/main
    } else {
        git clone --branch main --depth 1 $repoUrl $work
    }
    return $work
}

$gatewayKey = $OctopusParameters['GATEWAY_KEY']
if (-not $gatewayKey) { throw 'GATEWAY_KEY variable is not set (EternalSocial library set).' }

$src = Resolve-Source 'https://github.com/sharpninja/EternalReddit.git' 'EternalReddit\src'
docker build -t $image $src
if ($LASTEXITCODE -ne 0) { throw "docker build (app) failed with exit code $LASTEXITCODE" }
docker build -t $proxyImage -f (Join-Path $src 'src\EternalSocial.Proxy\Dockerfile') $src
if ($LASTEXITCODE -ne 0) { throw "docker build (gateway) failed with exit code $LASTEXITCODE" }

if (-not (docker network ls -q --filter "name=^$network$")) {
    docker network create $network | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "docker network create failed with exit code $LASTEXITCODE" }
}

$envFile = Join-Path $env:TEMP 'eternalreddit.env'
$names = @('ANTHROPIC_API_KEY','OPENAI_API_KEY','XAI_API_KEY','HF_API_KEY','NGROK_AUTHTOKEN',
    'Authentication__Google__ClientId','Authentication__Google__ClientSecret',
    'Authentication__Microsoft__ClientId','Authentication__Microsoft__ClientSecret',
    'Authentication__GitHub__ClientId','Authentication__GitHub__ClientSecret')
$lines = foreach ($n in $names) { $v = $OctopusParameters[$n]; if ($v) { "$n=$v" } }
$lines = @($lines) + "GATEWAY_KEY=$gatewayKey"
[System.IO.File]::WriteAllLines($envFile, [string[]]$lines)

try {
    TeardownContainer $container
    docker run -d --name $container --restart unless-stopped --network $network -v eternalreddit-data:/app/data -e ASPNETCORE_ENVIRONMENT=Production -e PATH_BASE=/r --env-file $envFile $image
    if ($LASTEXITCODE -ne 0) { throw "docker run (app) failed with exit code $LASTEXITCODE" }

    TeardownContainer $proxy
    docker run -d --name $proxy --restart unless-stopped --network $network -p ${hostPort}:8080 -v eternalsocial-data:/app/data -e ASPNETCORE_ENVIRONMENT=Production --env-file $envFile $proxyImage
    if ($LASTEXITCODE -ne 0) { throw "docker run (gateway) failed with exit code $LASTEXITCODE" }
} finally {
    try { [System.IO.File]::Delete($envFile) } catch { }
}

$ngrokToken = $OctopusParameters['NGROK_AUTHTOKEN']
if (-not $ngrokToken) { $ngrokToken = $env:NGROK_AUTHTOKEN }
TeardownContainer $ngrok
if ($ngrokToken) {
    docker run -d --name $ngrok --restart unless-stopped --add-host=host.docker.internal:host-gateway -e NGROK_AUTHTOKEN=$ngrokToken ngrok/ngrok:latest http --domain=$domain host.docker.internal:$hostPort
    if ($LASTEXITCODE -ne 0) { throw "docker run (ngrok) failed with exit code $LASTEXITCODE" }
} else {
    Write-Host 'WARNING: no ngrok token found; tunnel NOT started.'
}
Write-Host "EternalSocial core deployed: gateway on :$hostPort, EternalReadit at /r; ngrok -> https://$domain/"
