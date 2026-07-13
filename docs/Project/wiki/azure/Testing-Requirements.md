# Testing Requirements (MCP Server)

## TEST-DEPLOY

### TEST-DEPLOY-001

Deploy scripts are validated by PowerShell 5.1 parser checks, an Octopus layout simulation (CWD = deploy folder resolves repo root), a cmd /c git-stderr no-throw proof under EAP=Stop, successful Octopus deployments of all three projects from trigger-created releases, and a live estate smoke (landing, /r, /x, /d, gateway health all 200 through the tunnel).



## TEST-GATEWAY

### TEST-GATEWAY-001

xUnit suite (24 tests, serialized assembly) covering GatewayMapper route/cluster mapping and prefix/upstream validation, LiteDbRouteStore seeding (idempotent, fill-only-empty), and WAF endpoints - landing lists networks, health anonymous, route API 401, admin redirects to login, configured prefix proxies (502 with no upstream), unknown prefix 404, login without Google config 503.



## TEST-READIT

### TEST-READIT-001

xUnit suite (127 tests) covering data stores and seeding, roster service picks, per-sub model resolution, user comments (happy/blocked/banned/rate-limited/not-found), purge exemptions, admin authorization (401/403 matrix), moderation, AI feed control, gateway auth handler, and concurrency on real LiteDB (Columbus durability). Run dotnet test -c Debug; gate is 100 percent green, zero skipped.
