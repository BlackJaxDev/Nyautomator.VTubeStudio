# VTube Studio Integration Setup and Troubleshooting

This guide covers initial setup, API prerequisites, and common failure modes for Nyautomator's VTube Studio integration.

## Prerequisites
- VTube Studio running locally.
- Public API enabled in VTube Studio.
- Matching endpoint in the module config (`ServerUrl`, default `ws://localhost:8001`).
- Stable plugin identity values in the module config:
  - `PluginName`
  - `PluginDeveloper`

## First-Time Authorization Flow
1. Open dashboard VTube Studio card.
2. Click `Connect`.
3. Click `Request Access`.
4. Approve Nyautomator in the VTube Studio prompt.
5. Click `Authenticate Session`.

Token is persisted via `IntegrationTokenStore` under `vtubestudio`.

## Reconnect Behavior
- With `Enabled=true` and `AutoReconnect=true`, runtime startup attempts:
  - connect
  - authenticate with stored token
- Startup now includes transient retry/backoff for socket/connection failures.

## Common Issues
- **Connection refused / unreachable**
  - Verify VTube Studio is open.
  - Verify API port/URL and firewall rules.
- **Authentication denied / revoked**
  - Token may have been revoked in VTS.
  - Use `Forget Token`, then run first-time authorization flow again.
- **Connected but unauthenticated**
  - Stored token missing or stale.
  - Use `Request Access` then `Authenticate Session`.
- **Hotkey trigger fails**
  - Ensure model is loaded and hotkey ID is valid for current model.
- **Current model returns empty**
  - VTS may have no active loaded model, or session auth is not active.

## Verification Endpoints
- `GET /modules/vtubestudio/api/status`
- `POST /modules/vtubestudio/api/connect`
- `POST /modules/vtubestudio/api/disconnect`
- `POST /modules/vtubestudio/api/auth/token/request`
- `POST /modules/vtubestudio/api/auth/session`
- `POST /modules/vtubestudio/api/auth/clear`
- `GET /modules/vtubestudio/api/model/current`
- `GET /modules/vtubestudio/api/models`
- `GET /modules/vtubestudio/api/hotkeys`
- `POST /modules/vtubestudio/api/hotkey/trigger`
- `GET /modules/vtubestudio/api/expressions`
- `POST /modules/vtubestudio/api/expression`
- `GET /modules/vtubestudio/api/parameter`
- `POST /modules/vtubestudio/api/parameter/inject`

## Operational Notes
- Do not rotate `PluginName`/`PluginDeveloper` casually; token validity is plugin-identity scoped.
- If startup retries still fail, the integration remains non-fatal and can be recovered via dashboard actions.
