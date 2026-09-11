# Day 27 Task 1 — Security pass

## Notes for mentor

### 1. Threat model

Full STRIDE-lite threat model at [`THREAT-MODEL.md`](THREAT-MODEL.md), one
section per category, each tied to a real invoice-domain actor (a buyer
forging an approval, a supplier impersonating another supplier, one buyer
reading another buyer's invoices). Headline finding, named once and cross-
referenced from three different STRIDE letters: the API currently has
exactly one authorization tier — "has a valid token" — with no claim mapping
a caller to a specific buyer/supplier identity, so it stops an anonymous
stranger but not a legitimate caller acting as if they were a different
party. Closing that (the `Counterparty/Identity` context `DESIGN.md` already
names as unbuilt) would resolve the open item under Spoofing, Information
disclosure, and Elevation of privilege simultaneously.

### 2. Private endpoint — what shipped, and what didn't

SQL now has a real private endpoint: `vnet-capstone-dev` (two subnets — one
delegated to App Service for VNet Integration, one holding the private
endpoint NIC), a `privatelink.database.windows.net` private DNS zone, and
`publicNetworkAccess: 'Disabled'` on the server (the old
`AllowAllAzureServices` firewall rule is gone — nothing left for it to gate).
Both Web Apps get VNet Integration so they can still reach it.

**Service Bus did not get one.** Confirmed live, twice, against this
project's real Standard namespace: a private endpoint was rejected outright
(`PrivateEndpointInvalidSku`), and the fallback — a
`networkRuleSets` VNet restriction, not a private IP but still a real access
filter — was *also* rejected (`InvalidSkuForNetworkRuleSet: "Sku 'Standard'
does not support network rule set."`). Private endpoints, VNet rules, and IP
firewall rules are **all** Premium-only on Service Bus; there is no cheaper
partial option. Premium's smallest SKU prices at $0.9275/hour (confirmed live
against the Azure Retail Prices API) — roughly 69x Standard's cost, and left
running per this task's "leave the dev stack running" instruction would burn
through most of the remaining ₹19,079 credit in about ten days. Raised with
Devansh mid-session; decision was to keep Standard and document the gap
rather than pay for Premium. See `THREAT-MODEL.md`'s Information disclosure
section for the real consequence: Service Bus is reachable from the public
internet, gated only by identity-based auth (Day 25).

**Proof the SQL block is real** (not just a property that silently does
nothing): a raw TCP check still connects (Azure SQL's shared gateway accepts
the TCP handshake regardless), but a real login attempt from outside Azure
gets Azure's own explicit denial:

```
FAILED: SqlException: Reason: An instance-specific error occurred while
establishing a connection to SQL Server. Connection was denied because Deny
Public Network Access is set to Yes.
```

The deployed app, over its private path, still gets a real result from the
same server: `{"pinged":true,"result":1,"atUtc":"2026-09-11T09:28:47Z"}`.
Full before/after commands and output: `security/network/connectivity-evidence.md`.

**Added cost**: one private endpoint (SQL) — Microsoft's publicly documented
Private Link rate is $0.01/hour plus $0.01/GB processed (I could not find the
exact meter under this name via the Azure Retail Prices API this session to
re-verify it live the way other costs in this project are — flagging that
honestly rather than presenting it as independently confirmed). At near-zero
data volume for a dev scaffold, this adds roughly $7-8/month, negligible
against the SQL/App Service costs already running. A private DNS zone and
VNet add no meaningful cost of their own.

**A real Deployment Stack limitation hit this session**: removing the stale
firewall rule made the stack try to delete it (`actionOnUnmanage: deleteAll`)
— that delete failed with `DenyAssignmentAuthorizationFailed`, because the
stack's own `denyDelete` protection blocked the stack itself from deleting a
resource it was unmanaging (a documented limitation, not a bug here). Fixed
by redeploying once with `--deny-settings-mode none` to clear it, then once
more with `denyDelete` restored — same protected end state, stale resource
actually gone.

### 3. OpenAPI hardening

The API surface through Day 26 was two demo endpoints proving DI wiring
resolves — nothing shaped like a real API to harden, so a minimum real
surface was added (`POST /v1/purchase-orders`, `POST /v1/invoices`,
`GET /v1/invoices`), stated plainly rather than pretending the demo endpoints
were already "the API":

- **Auth**: Entra ID JWT bearer, via a new app registration
  (`capstone-api-dev`). `Auth:TenantId`/`Auth:ApiAppId` are non-secret
  identifiers, set only in deployed environments — local `dotnet run` has
  neither, so it stays unauthenticated (existing README demo instructions
  keep working), while every deployed environment enforces it. Verified live:
  no token → `401`; a real client-credentials token (a throwaway test client
  app, its own app role, admin-consent granted) → `201`/`200`.
- **Versioning**: `/v1` route group.
- **Input limits**: 16 KB request body cap, bounded string lengths and
  quantities/prices, page size clamped to 100, a 100-req/min/IP rate limiter
  applied globally. All verified live (413 on an oversized body, 400 with
  field-level errors on bad input, 429 after ~100 requests in a burst).

### 4. ZAP baseline — before and after

Scanned the live `capstone-dev-api-f7nsoj.azurewebsites.net` with
`docker run ghcr.io/zaproxy/zaproxy:stable zap-baseline.py`. Full reports:
`security/zap/zap-baseline-before.html` / `.json` and
`security/zap/zap-baseline-final.html` / `.json`.

| Before (7 warnings) | Fix | 
|---|---|
| Strict-Transport-Security Header Not Set (Low/High) | `app.UseHsts()` |
| X-Content-Type-Options Header Missing (Low) | `X-Content-Type-Options: nosniff` |
| Cross-Origin-Resource-Policy Header Missing (Low) | `Cross-Origin-Resource-Policy: same-origin` |
| Cookie with SameSite Attribute None (Low) x3 | Disabled App Service `clientAffinityEnabled` — removed the platform's own `ARRAffinity`/`ARRAffinitySameSite` cookies at the source; this app has no per-instance state a sticky session was ever protecting |
| Storable and Cacheable Content (Info) | `Cache-Control: no-store, no-cache, must-revalidate` |
| Re-examine Cache-control Directives (Info) | same fix — matches ZAP's own suggested directive combination |
| Session Management Response Identified (Info) | resolved as a side effect of removing the affinity cookies |

**After: 1 informational-only finding remains — "Non-Storable Content."**
Not fixed, and not worth fixing: this alert literally means "this response
correctly cannot be cached," which is the *goal* of the `no-store` fix above,
not a defect. ZAP flags it only to suggest that non-sensitive static content
*could* be cached for performance — this API has no such content; everything
it returns is either live domain data or an auth failure. Making it
cacheable would be the wrong direction.

Live app connections are unaffected by any of this — `/`, `/v1/*`, and
`/demo/*` all still return their expected status codes after every fix
(re-verified with real `curl` calls, not assumed).

### Live project and snapshot

The live project is `capstone/` — `day-27/task-1/` is a frozen snapshot of it
as this day ended, copied after the work above, never edited directly.

Commit representing this day's state: `5c1991d91c4df3e384ca3f133c3d0b725786640d`.

## What did you learn this session?
Turns out Service Bus can't be network-locked on Standard tier at all, not even a cheap VNet rule — only Premium supports it, and I only learned that from a real rejected deployment.
Also learned a blocked SQL server still accepts a TCP handshake — you need an actual login attempt to prove the block is real, not just a ping.

## What would break this?
Right now any valid token can read or approve any invoice — there's no check yet that ties the token to a specific buyer or supplier.
And since Service Bus has no network restriction, anyone with a valid token for it could reach it from anywhere, not just from inside our app.
