# EternalSocial deploy watcher (runs on the deploy target via a scheduled task).
# Polls each repo's main branch on GitHub; when any HEAD moves, creates a release
# of the EternalSocial Octopus project and deploys it. The estate deploys as one
# unit (the shared per-deploy GATEWAY_KEY couples the containers by design).
param(
    [string]$StateDir = 'C:\ProgramData\EternalSocial'
)

$ErrorActionPreference = 'Stop'
$stateFile = Join-Path $StateDir 'deploy-watch.json'
$keyFile = Join-Path $StateDir 'octopus.key'
$logFile = Join-Path $StateDir 'deploy-watch.log'
$octopusUrl = 'http://payton-desktop:8065'
$projectId = 'Projects-4'
$environmentId = 'Environments-3'
$spaceId = 'Spaces-1'

$repos = @(
    @{ Name = 'EternalReddit';  Url = 'https://github.com/sharpninja/EternalReddit.git' },
    @{ Name = 'EternalX';       Url = 'https://github.com/sharpninja/EternalX.Blazor.git' },
    @{ Name = 'EternalDiscord'; Url = 'https://github.com/sharpninja/EternalDiscord.git' }
)

function Write-Log([string]$message) {
    $line = "{0:yyyy-MM-dd HH:mm:ss}  {1}" -f (Get-Date), $message
    Add-Content -Path $logFile -Value $line
}

New-Item -ItemType Directory -Force $StateDir | Out-Null

# API key protected with DPAPI for the account this task runs as.
$secure = Get-Content $keyFile | ConvertTo-SecureString
$apiKey = [System.Net.NetworkCredential]::new('', $secure).Password
$headers = @{ 'X-Octopus-ApiKey' = $apiKey }

# Windows PowerShell 5.1 compatible state load (no -AsHashtable).
$state = @{}
if (Test-Path $stateFile) {
    (Get-Content $stateFile -Raw | ConvertFrom-Json).PSObject.Properties |
        ForEach-Object { $state[$_.Name] = $_.Value }
}

$changed = @()
foreach ($repo in $repos) {
    $line = git ls-remote $repo.Url refs/heads/main 2>$null
    if (-not $line) { Write-Log "WARN: ls-remote failed for $($repo.Name)"; continue }
    $sha = ($line -split "`t")[0]
    if ($state[$repo.Name] -ne $sha) {
        $changed += "$($repo.Name)@$($sha.Substring(0, 9))"
        $state[$repo.Name] = $sha
    }
}

if ($changed.Count -eq 0) { exit 0 }

# Skip (without consuming the change) while a deploy is already in flight.
$active = Invoke-RestMethod "$octopusUrl/api/$spaceId/tasks?name=Deploy&states=Queued,Executing&project=$projectId&take=1" -Headers $headers
if ($active.Items.Count -gt 0) {
    Write-Log "Change detected ($($changed -join ', ')) but a deploy is in flight; retrying next tick."
    exit 0
}

$version = Get-Date -Format 'yyyy.MM.dd.HHmmss'
$release = Invoke-RestMethod "$octopusUrl/api/$spaceId/releases" -Headers $headers -Method Post `
    -Body (@{ ProjectId = $projectId; Version = $version } | ConvertTo-Json) -ContentType 'application/json'
$deploy = Invoke-RestMethod "$octopusUrl/api/$spaceId/deployments" -Headers $headers -Method Post `
    -Body (@{ ReleaseId = $release.Id; EnvironmentId = $environmentId } | ConvertTo-Json) -ContentType 'application/json'

$state | ConvertTo-Json | Set-Content $stateFile
Write-Log "Triggered $($deploy.TaskId) (release $version) for: $($changed -join ', ')"
