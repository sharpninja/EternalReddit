$octoUrl = $env:OCTOPUS_URL.TrimEnd('/'); $headers = @{ 'X-Octopus-ApiKey' = $env:OCTOPUS_API_KEY }; $sp='Spaces-1'
$sec = Read-Host 'Paste Anthropic API key (sk-ant-...)' -AsSecureString
$val = [System.Net.NetworkCredential]::new('', $sec).Password
$proj = Invoke-RestMethod "$octoUrl/api/$sp/projects/Projects-4" -Headers $headers
$vs = Invoke-RestMethod "$octoUrl/api/$sp/variables/$($proj.VariableSetId)" -Headers $headers
$v = $vs.Variables | Where-Object Name -eq 'ANTHROPIC_API_KEY'
if ($v) { $v.Value = $val; $v.Type = 'Sensitive'; $v.IsSensitive = $true }
else { $vs.Variables += [pscustomobject]@{ Name='ANTHROPIC_API_KEY'; Value=$val; Type='Sensitive'; IsSensitive=$true; Scope=[pscustomobject]@{} } }
Invoke-RestMethod "$octoUrl/api/$sp/variables/$($proj.VariableSetId)" -Method Put -Headers $headers -Body ($vs | ConvertTo-Json -Depth 12) -ContentType 'application/json' | Out-Null
$val = $null
'ANTHROPIC_API_KEY set.'
