# Security and deployment

## Security model

TcgULtimateServer separates three identities:

1. **Player** - registers or signs in through the API and receives a bearer session.
2. **Moderator or administrator** - a registered account with an elevated server role.
3. **Local operator console** - receives a loopback-only administrator session from the locally hosted server and does not display a login form.

The WinUI console not requiring a login does not make the network API anonymous. Player endpoints continue to enforce bearer sessions, and management endpoints enforce moderator or administrator roles.

## Roles

| Role | Intended access |
|---|---|
| `user` | Player inventory, decks, messages, friends, matchmaking, game creation, and authenticated assets. |
| `moderator` | Account review and permitted account-state changes, account listing, and unified chat monitoring. |
| `admin` | Full management console, player intelligence, LiveOps, AI, matchmaking, commerce, moderation, reports, and archives. |

The server independently enforces roles. Hiding or disabling a WinUI navigation item is not the security boundary.

## Administrator provisioning

Set administrator handles before those users register:

```powershell
$env:TCG_ADMIN_HANDLES = "operator,liveops-lead"
```

Matching registrations receive the administrator role. Protect the environment and do not expose this value through client configuration or source control.

The native WinUI console normally uses the local management session instead of a registered administrator account.

## Local management session

`POST /api/auth/local-management` requires:

- A loopback source address.
- `X-Tcg-Management-Key` matching `TCG_LOCAL_MANAGEMENT_KEY`.
- A key known only to the WinUI host and API process.

The WinUI process generates a random 256-bit key when one is not provided and passes it only to its child server process. Never forward this endpoint through a reverse proxy and never bind it as a remote administration solution.

## TLS

- Production traffic must use HTTPS.
- SignalR must use WSS by connecting to an HTTPS hub URL.
- Install a certificate trusted by game-client platforms.
- Do not ship development certificates.
- Do not disable certificate verification in release clients.
- Redirect or block plaintext HTTP at the edge.
- Apply HSTS only after confirming the production HTTPS configuration is correct.

## Reverse proxy

When deploying behind IIS, Nginx, Apache, a cloud load balancer, or another proxy:

- Allow WebSocket upgrades for `/hubs/game` and `/hubs/state-machine`.
- Keep a client connected to a compatible server instance for the life of its SignalR connection.
- Configure forwarded headers only for known trusted proxies.
- Preserve HTTPS scheme information so generated asset URLs remain HTTPS.
- Use request-body limits compatible with the 50 MB asset upload limit and approved content-pack sizes.
- Set idle timeouts long enough for SignalR connections.
- Restrict public access to routes that are intentionally public.

## CORS

Browser origins are denied by default. To allow a browser-hosted client, explicitly set the comma-separated allowlist before starting the server:

```powershell
$env:TCG_ALLOWED_ORIGINS = "https://play.cardforge.example,https://staging.cardforge.example"
```

Native Unity and .NET clients are not governed by browser CORS. Do not add wildcard origins or a development URL to a production allowlist.

## Session and token handling

- Treat bearer tokens as secrets.
- Keep active tokens in memory when possible.
- Sessions expire after the configured `SessionTimeoutMinutes` period of inactivity (120 minutes by default).
- Call `DELETE /api/auth/session` when the player signs out to revoke the bearer token immediately.
- Do not write tokens to logs, analytics, crash reports, URLs, scene assets, or `PlayerPrefs`.
- Clear the token and stop real-time connections on sign-out or `401`.
- Use platform-protected credential storage if persistent sessions are added.
- Add token expiry, revocation, and refresh behavior before using long-lived production sessions.

## Passwords and account policy

The server requires handles of 3-24 characters and passwords of 8-256 characters. Passwords are salted PBKDF2-SHA256 hashes, and registration/login are limited to ten requests per minute per source IP. Production hardening should still add breach screening, account lockout, recovery, and optional multi-factor requirements. Do not implement those controls only in the client.

## Secure SignalR actions

State-changing hub calls are protected by:

- GZIP-compressed, Base64 payloads.
- HMAC-SHA256 signatures keyed by the session token.
- Scope binding to the exact game or workflow action.
- A 30-second timestamp window.
- One-time packet IDs.
- Per-user action limits.
- Decompressed payload-size limits.

See [Communications protocol](communications-protocol.md) for the signature format.

## Audit logging

Set a protected durable path:

```powershell
$env:TCG_AUDIT_LOG_PATH = "D:\TcgULtimateServerData\audit.ndjson"
```

The server appends structured JSONL audit records and restores recent entries at startup. The configured folder should:

- Be writable only by the service identity and approved operators.
- Be monitored for unexpected access or growth.
- Be included in backup and retention policies.
- Forward records to centralized monitoring in production.
- Be treated as potentially sensitive operational data.

## Data persistence

Gameplay and most operations state is currently in memory. This is appropriate for development and controlled evaluation, but a production deployment should add durable storage, backups, migrations, and recovery testing.

Until durable storage is implemented:

- Generate and save full archives before maintenance.
- Verify archive SHA-256 integrity.
- Store archives outside the application directory.
- Protect archives with file permissions and storage encryption.
- Define retention and deletion procedures.

## Network exposure review

The current API intentionally keeps rule packs, rankings, queue status, workflow definitions, and inventory-by-user-ID discoverable for development and client bootstrap. Game state and workflow instances are restricted to their participants or administrators. Review every public route before an internet deployment and apply authentication or response shaping where the product's privacy model requires it.

## Production checklist

- [ ] Use a supported Windows or server host with current security updates.
- [ ] Run a Release build under a dedicated service identity.
- [ ] Use a trusted TLS certificate and HTTPS-only exposure.
- [ ] Enable WebSocket forwarding for SignalR.
- [ ] Set `TCG_ALLOWED_ORIGINS` when deploying a browser client.
- [ ] Keep local management loopback-only.
- [ ] Store environment secrets outside source control.
- [ ] Define administrator provisioning and role-review procedures.
- [x] Session expiry, revocation, and login throttling are enabled.
- [ ] Configure durable audit storage and centralized monitoring.
- [ ] Review public routes against privacy requirements.
- [ ] Add durable gameplay and operations storage.
- [ ] Back up and restore-test data.
- [ ] Load-test REST, matchmaking, and SignalR connections.
- [ ] Validate Unity and other clients on every target platform.
- [ ] Document incident, rollback, and maintenance procedures.
