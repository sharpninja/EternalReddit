# Testing Requirements (MCP Server)

## TEST-AI

### TEST-AI-001

Covers group-scoped figure picks with fallback so feeds never dead-end, approval and Enabled semantics, persona threading into provider prompts with per-sub model overrides, and the Columbus scripted-gag durability under concurrent writes.



## TEST-CORE

### TEST-CORE-001

xUnit suite (127 tests) covering data stores and seeding, roster service picks, per-sub model resolution, user comments (happy/blocked/banned/rate-limited/not-found), purge exemptions, admin authorization (401/403 matrix), moderation, AI feed control, gateway auth handler, and concurrency on real LiteDB (Columbus durability). Run dotnet test -c Debug; gate is 100 percent green, zero skipped.


### TEST-CORE-002

xUnit suite (24 tests, serialized assembly) covering GatewayMapper route/cluster mapping and prefix/upstream validation, LiteDbRouteStore seeding (idempotent, fill-only-empty), and WAF endpoints - landing lists networks, health anonymous, route API 401, admin redirects to login, configured prefix proxies (502 with no upstream), unknown prefix 404, login without Google config 503.


### TEST-CORE-003

Deploy scripts are validated by PowerShell 5.1 parser checks, an Octopus layout simulation (CWD = deploy folder resolves repo root), a cmd /c git-stderr no-throw proof under EAP=Stop, successful Octopus deployments of all three projects from trigger-created releases, and a live estate smoke (landing, /r, /x, /d, gateway health all 200 through the tunnel).


### TEST-CORE-004

Covers LiteDB round-trips for all entities, seeding idempotence and fill-only-empty behavior, purge exemptions (human and scripted content), concurrency on a real LiteDB file (no lost updates), and export/restore round-trip including version rejection. Part of the 151-test green suite.
