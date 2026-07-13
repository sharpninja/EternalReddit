# Technical Requirements (MCP Server)

## TR-DEPLOY-OCTO-001

**Git-sourced script step runtime contract** — Octopus materializes the repo one level above the script folder and runs the script with CWD = the script folder under Windows PowerShell 5.1, so scripts probe Split-Path -Parent $PSScriptRoot for the repo root and route fallback git calls through cmd /c so git stderr progress cannot become a terminating error under ErrorActionPreference=Stop.
**Covered by:** FR: FR-DEPLOY-001; TEST: TEST-DEPLOY-001
**Status:** completed
Scope: layer-1+

## TR-DEPLOY-OCTO-002

**Git trigger and lifecycle wiring** — Per-project Git triggers need Filter.Sources populated with the DeploymentActionSlug (empty Sources watch nothing) and Action.ChannelId set; git-sourced steps must not carry a Script.Syntax property. Lifecycle Development phase lists Environments-3 as automatic target. Known gap - trigger-created releases are not auto-deploying and currently need a manual deployment.
**Covered by:** FR: FR-DEPLOY-001, FR-DEPLOY-002; TEST: TEST-DEPLOY-001
**Status:** in_progress
Scope: layer-1+

## TR-GATEWAY-ARCH-001

**YARP in-memory routing** — Gateway loads YARP config from memory (InMemoryConfigProvider.Update on route changes). Routes map prefix to '{prefix}/{**catch-all}' at Order 10 with trailing-slash cluster addresses; prefixes pass through un-stripped and downstream apps absorb them with UsePathBase plus a rewritten base href.
**Covered by:** FR: FR-GATEWAY-001, FR-GATEWAY-003; TEST: TEST-GATEWAY-001
**Status:** completed
Scope: layer-1+

## TR-GATEWAY-SEC-001

**Identity header trust boundary** — The gateway strips inbound X-Auth-* and X-Gateway-Key from clients, then injects X-Gateway-Key plus X-Auth-UserId/Name/Email from the authenticated principal. Sites accept the headers only when their GATEWAY_KEY config matches; otherwise the request is anonymous. Trust boundary = docker network + shared key.
**Covered by:** FR: FR-GATEWAY-002, FR-READIT-004; TEST: TEST-GATEWAY-001, TEST-READIT-001
**Status:** completed
Scope: layer-1+

## TR-GATEWAY-SEC-002

**Forwarded headers and challenge scheme** — ForwardedHeaders options must .Clear() KnownIPNetworks/KnownProxies (collection initializers do not clear defaults - caused http redirect_uri). Gateway rewrites scheme before proxying so downstream ForwardLimit=1 works. DefaultChallengeScheme stays Cookies so APIs return 401 instead of redirecting to Google.
**Covered by:** FR: FR-GATEWAY-002; TEST: TEST-GATEWAY-001, TEST-READIT-001
**Status:** completed
Scope: layer-1+

## TR-READIT-CONC-001

**Post write serialization** — PostService serializes whole-document writes behind a SemaphoreSlim; replies commit via re-fetch/append/save (CommitReplyAsync) with AI generation outside the lock, eliminating lost-update races that previously dropped Columbus and reply threads.
**Covered by:** FR: FR-READIT-003, FR-READIT-009; TEST: TEST-READIT-001
**Status:** completed
Scope: layer-1+

## TR-READIT-DATA-001

**LiteDB persistence conventions** — Single-file LiteDB store with BsonMapper EmptyStringToNull=false, DateTime as UTC ticks, fluent string-id mapping for Community/PeerGroup/Figure (Shared models stay attribute-free), and an index on Post.Community. Seeding is per-id insert-if-absent so releases add defaults without clobbering admin edits.
**Covered by:** FR: FR-READIT-001, FR-READIT-002, FR-READIT-005, FR-READIT-006, FR-READIT-007, FR-READIT-008, FR-READIT-010; TEST: TEST-READIT-001
**Status:** completed
Scope: layer-1+

