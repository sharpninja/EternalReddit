# Technical Requirements (MCP Server)

## TR-AI-GEN-001

**AI selection and generation context** — The server assigns the speaker - models never pick their own figure; they receive a fixed persona input. Thread selection gives the AI all posts and replies from the previous 24 hours sorted by activity over the last hour, and the AI chooses from that list. Reply context is reproducible - the post plus the ancestor chain of the comment being answered. Every AI comment records the exact provider and model that produced it.
**Covered by:** FR: FR-AI-003, FR-AI-004, FR-AI-006, FR-AI-009; TEST: TEST-CORE-001, TEST-AI-001
**Status:** completed
Scope: layer-1+

## TR-AI-SEED-001

**Roster seed mechanics** — DefaultRoster exposes expression-bodied collections so every access returns fresh instances (seed data is immutable to callers). RosterSeed inserts per-id if absent and runs after build but before the startup purge. The devblog community seeds with AiParticipation false and PostingRestricted true.
**Covered by:** FR: FR-AI-005, FR-AI-007, FR-CORE-017; TEST: TEST-AI-001, TEST-CORE-004
**Status:** completed
Scope: layer-1+

## TR-CORE-ARCH-001

**YARP in-memory routing** — Gateway loads YARP config from memory (InMemoryConfigProvider.Update on route changes). Routes map prefix to '{prefix}/{**catch-all}' at Order 10 with trailing-slash cluster addresses; prefixes pass through un-stripped and downstream apps absorb them with UsePathBase plus a rewritten base href.
**Covered by:** FR: FR-CORE-009, FR-CORE-011; TEST: TEST-CORE-002, TEST-CORE-003
**Status:** completed
Scope: layer-1+

## TR-CORE-CONC-001

**Post write serialization** — PostService serializes whole-document writes behind a SemaphoreSlim; replies commit via re-fetch/append/save (CommitReplyAsync) with AI generation outside the lock, eliminating lost-update races that previously dropped Columbus and reply threads.
**Covered by:** FR: FR-AI-003, FR-CORE-003, FR-CORE-007; TEST: TEST-CORE-001
**Status:** completed
Scope: layer-1+

## TR-CORE-DATA-001

**LiteDB persistence conventions** — Single-file LiteDB store with BsonMapper EmptyStringToNull=false, DateTime as UTC ticks, fluent string-id mapping for Community/PeerGroup/Figure (Shared models stay attribute-free), and an index on Post.Community. Seeding is per-id insert-if-absent so releases add defaults without clobbering admin edits.
**Covered by:** FR: FR-AI-001, FR-AI-002, FR-AI-004, FR-AI-007, FR-AI-008, FR-CORE-001, FR-CORE-002, FR-CORE-005, FR-CORE-006, FR-CORE-008, FR-CORE-015, FR-UI-001, FR-UI-002, FR-UI-003, FR-UI-004; TEST: TEST-CORE-001, TEST-AI-001, TEST-CORE-004
**Status:** completed
Scope: layer-1+

## TR-CORE-EXPORT-001

**Export bundle contract** — ExportBundle is a versioned record (Version 1) carrying posts, communities, peer groups, figures, users, and settings, downloaded as eternalreddit-export-<timestamp>.json. Restore returns 400 for any Version other than 1 or a malformed bundle, clears stores, replaces from the snapshot, and raises FeedChanged.
**Covered by:** FR: FR-CORE-018; TEST: TEST-CORE-004
**Status:** completed
Scope: layer-1+

## TR-CORE-OCTO-001

**Git-sourced script step runtime contract** — Octopus materializes the repo one level above the script folder and runs the script with CWD = the script folder under Windows PowerShell 5.1, so scripts probe Split-Path -Parent $PSScriptRoot for the repo root and route fallback git calls through cmd /c so git stderr progress cannot become a terminating error under ErrorActionPreference=Stop.
**Covered by:** FR: FR-CORE-013; TEST: TEST-CORE-003
**Status:** completed
Scope: layer-1+

## TR-CORE-OCTO-002

**Git trigger and lifecycle wiring** — Per-project Git triggers need Filter.Sources populated with the DeploymentActionSlug (empty Sources watch nothing) and Action.ChannelId set; git-sourced steps must not carry a Script.Syntax property. Lifecycle Development phase lists Environments-3 as automatic target. Known gap - trigger-created releases are not auto-deploying and currently need a manual deployment.
**Covered by:** FR: FR-CORE-013, FR-CORE-014; TEST: TEST-CORE-003
**Status:** in_progress
Scope: layer-1+

## TR-CORE-SEC-001

**Identity header trust boundary** — The gateway strips inbound X-Auth-* and X-Gateway-Key from clients, then injects X-Gateway-Key plus X-Auth-UserId/Name/Email from the authenticated principal. Sites accept the headers only when their GATEWAY_KEY config matches; otherwise the request is anonymous. Trust boundary = docker network + shared key.
**Covered by:** FR: FR-CORE-004, FR-CORE-010; TEST: TEST-CORE-001, TEST-CORE-002
**Status:** completed
Scope: layer-1+

## TR-CORE-SEC-002

**Forwarded headers and challenge scheme** — ForwardedHeaders options must .Clear() KnownIPNetworks/KnownProxies (collection initializers do not clear defaults - caused http redirect_uri). Gateway rewrites scheme before proxying so downstream ForwardLimit=1 works. DefaultChallengeScheme stays Cookies so APIs return 401 instead of redirecting to Google.
**Covered by:** FR: FR-CORE-010, FR-CORE-012; TEST: TEST-CORE-001, TEST-CORE-002
**Status:** completed
Scope: layer-1+

## TR-CORE-SEC-003

**Site container isolation** — Site containers (eternalreddit, eternalx, eternaldiscord) expose no public ports on the docker host. Only the gateway is reachable (host 8090 behind the ngrok tunnel), so all site traffic and identity headers flow through the gateway trust boundary.
**Covered by:** FR: FR-CORE-009; TEST: TEST-CORE-002, TEST-CORE-003
**Status:** completed
Scope: layer-1+

## TR-CORE-VOL-001

**Named volume persistence** — Each site persists LiteDB under /app/data on a named docker volume (eternalreddit-data, eternalsocial-data, eternalx-data, eternaldiscord-data) with LITEDB_PATH pointing into the mount, so databases outlive container replacement during redeploys.
**Covered by:** FR: FR-CORE-016; TEST: TEST-CORE-003, TEST-CORE-004
**Status:** completed
Scope: layer-1+

