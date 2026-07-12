$octoUrl = $env:OCTOPUS_URL.TrimEnd('/'); $headers = @{ 'X-Octopus-ApiKey' = $env:OCTOPUS_API_KEY }; $sp='Spaces-1'
# Reads the Grok key straight from the environment - no pasting needed.
$val = $env:MCP_AGENT_API_KEY
if ([string]::IsNullOrWhiteSpace($val)) { throw 'MCP_AGENT_API_KEY is not set in this shell.' }
$proj = Invoke-RestMethod "$octoUrl/api/$sp/projects/Projects-4" -Headers $headers
$vs = Invoke-RestMethod "$octoUrl/api/$sp/variables/$($proj.VariableSetId)" -Headers $headers
$v = $vs.Variables | Where-Object Name -eq 'XAI_API_KEY'
if ($v) { $v.Value = $val; $v.Type = 'Sensitive'; $v.IsSensitive = $true }
else { $vs.Variables += [pscustomobject]@{ Name='XAI_API_KEY'; Value=$val; Type='Sensitive'; IsSensitive=$true; Scope=[pscustomobject]@{} } }
Invoke-RestMethod "$octoUrl/api/$sp/variables/$($proj.VariableSetId)" -Method Put -Headers $headers -Body ($vs | ConvertTo-Json -Depth 12) -ContentType 'application/json' | Out-Null
$val = $null
'XAI_API_KEY (Grok) set from MCP_AGENT_API_KEY.'
