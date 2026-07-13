# EternalSocial Gateway SSO Contract

Every Eternal site (EternalReadit `/r`, EternalX `/x`, EternalDiscord `/d`, and any
future network) authenticates users through the **EternalSocial gateway**, not with
its own OAuth flow. The gateway (src/EternalSocial.Proxy in the EternalReddit repo)
owns the single Google OIDC sign-in at the public root and shares the identity with
every site it proxies.

## How identity reaches a site

On every proxied request the gateway:

1. **Strips** any inbound `X-Gateway-Key` / `X-Auth-*` headers from the client
   (nothing can be spoofed from outside).
2. Adds `X-Gateway-Key: <shared secret>` - a per-deploy secret injected into the
   gateway and every site container as the `GATEWAY_KEY` environment variable.
3. When the request carries the gateway's signed-in cookie, adds:
   - `X-Auth-UserId` - the stable user id (Google `sub`)
   - `X-Auth-Name` - display name
   - `X-Auth-Email` - email (used by sites for admin checks)

## What a site must implement

- An authentication handler that, **only when `GATEWAY_KEY` is configured and the
  request's `X-Gateway-Key` matches it**, builds the request principal from the
  `X-Auth-*` headers (`NameIdentifier`, `Name`, `Email` claims). No key or no match:
  treat the request as anonymous. Never run your own OAuth in gateway mode.
- Login/logout links point at the gateway root: `/login?returnUrl=<current url>` and
  `/logout` (root-absolute, outside the site's path base).
- `PATH_BASE`/`Proxy:PathBase` support: the gateway forwards the site's path prefix
  un-stripped; the site absorbs it with `UsePathBase` and a matching document
  `<base href>`.
- Forwarded headers: trust `X-Forwarded-Proto/Host` (the gateway sends the public
  https origin, having already resolved ngrok's headers).

Reference implementations:
- EternalReadit: `src/EternalReddit.Server/Auth/GatewayAuthHandler.cs`
- EternalDiscord: `src/EternalDiscord/EternalDiscord/Authentication/GatewayAuthenticationHandler.cs`

## Deployment

The Octopus deploy generates one `GATEWAY_KEY` per deploy and injects it into the
gateway and every site container on the shared `eternal` docker network. Sites have
no public ports; only the gateway is reachable (host :8090 behind the ngrok tunnel).
Site containers must therefore be redeployed together with the gateway (the single
deploy process already does this).

## Security notes

- The header trust boundary is the docker network plus the shared key; sites must
  never accept the headers without the key match.
- The gateway's admin surface (`/admin`, `/_gateway/*`) is restricted to the owner's
  Google account (`Authorization__AdminEmail`).

## Deployment triggers

Each repo deploys independently: an Octopus Git trigger on each project polls
its GitHub repo and, on a push to main, creates a release that auto-deploys to
Development (lifecycle). The stable GATEWAY_KEY lives in the EternalSocial
library variable set, so sites can restart at different times without breaking
SSO. Deploy scripts live in each repo at deploy/octopus-deploy.ps1.

Trigger polling runs on the Octopus server roughly every four minutes; expect a
push-to-live latency of about five minutes end to end.
