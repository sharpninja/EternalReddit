$octoUrl = $env:OCTOPUS_URL.TrimEnd('/'); $headers = @{ 'X-Octopus-ApiKey' = $env:OCTOPUS_API_KEY }; $sp='Spaces-1'
$sec = Read-Host 'Paste Google Client Secret' -AsSecureString
$val = [System.Net.NetworkCredential]::new('', $sec).Password
$proj = Invoke-RestMethod "$octoUrl/api/$sp/projects/Projects-4" -Headers $headers
$vs = Invoke-RestMethod "$octoUrl/api/$sp/variables/$($proj.VariableSetId)" -Headers $headers
$v = $vs.Variables | Where-Object Name -eq 'Authentication__Google__ClientSecret'
if ($v) { $v.Value = $val; $v.Type = 'Sensitive'; $v.IsSensitive = $true }
else { $vs.Variables += [pscustomobject]@{ Name='Authentication__Google__ClientSecret'; Value=$val; Type='Sensitive'; IsSensitive=$true; Scope=[pscustomobject]@{} } }
Invoke-RestMethod "$octoUrl/api/$sp/variables/$($proj.VariableSetId)" -Method Put -Headers $headers -Body ($vs | ConvertTo-Json -Depth 12) -ContentType 'application/json' | Out-Null
$val = $null
'Authentication__Google__ClientSecret set.'
