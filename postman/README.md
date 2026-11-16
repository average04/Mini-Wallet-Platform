# Manual tests in Postman

Import both JSON files in this directory into your **MiniWallet** workspace, then select the **MiniWallet Local** environment. The collection contains request bodies, expected statuses, response checks and automatic ID capture.

## Watch balance changes live

Open http://localhost:5102/live-balances, copy the current **MiniWallet Local → player_token** value into the page, and click **Connect**. Send a BET as that player or BONUS/WIN as admin from this collection. Only that player's live page updates. Use a new idempotency key for a new transaction; replaying an old request produces no new event. Paste a fresh token and reconnect when it expires.

## Log in and get your tokens

1. Open **00 - Login and service health → Login as player**.
2. Select **Authorization → Get New Access Token**. The OAuth settings are already filled in.
3. Sign in on Keycloak's page with your registered player, or `player` / `Player123!`.
4. Return to Postman and click **Use Token**, then **Send** on the login request. A 200 response confirms your identity and automatically saves `player_token` in **MiniWallet Local**.
5. Repeat **Login as admin**, using `admin` / `Admin123!`. This saves `admin_token` separately.

Both requests use **authorization code with PKCE (SHA-256)**. Postman's OAuth helper exchanges the code for tokens at Keycloak's `/protocol/openid-connect/token` endpoint. The visible GET request then calls Keycloak's `userinfo` endpoint to verify the account and capture the selected access token. A player login rejects an admin account so the later permission checks stay meaningful.

Local access tokens expire after one hour. In the matching login request, refresh the token under **Authorization** (or use **Get New Access Token** again), select **Use Token**, then **Send** to update the environment. Tokens issued before a lifetime change keep their original expiry. The same access token works with both APIs. No copying from Swagger is needed.

The dedicated `mini-wallet-postman` public client registers Postman's browser callback, requires PKCE S256, and keeps password grant disabled. See [Postman's OAuth documentation](https://learning.postman.com/docs/use/send-requests/authorization/oauth-20) for its token controls.

### Existing Docker installation

New databases import the Postman client automatically. If your Keycloak database predates this collection, run this once from the repository root in PowerShell:

```powershell
.\postman\Configure-Keycloak.ps1
```

The script creates or updates only `mini-wallet-postman` using the local container's management credentials, then verifies the effective protocol mappers and default client scopes. A mismatch reports an error rather than declaring the client ready. It preserves existing accounts and application data; a database reset is unnecessary. It can be run again safely.

Login settings are collection variables (`oidc_issuer`, `oidc_client_id`, `oidc_callback_url`, `api_audience`), so an existing **MiniWallet Local** environment does not need to be replaced when importing the updated collection.

## Send requests in order

| Folder | What to test |
| --- | --- |
| 00 - Login and service health | Get player/admin tokens and check that both API processes respond. |
| 01 - Player profile | Create, repeat creation, read and update your profile. Saves `player_id`. |
| 02 - Admin profile | Admin reads and updates the selected player's profile. |
| 03 - Wallet happy path | Three wallets, BONUS 100, BET 10, WIN 20 (2×), balances and history. Saves transaction IDs. |
| 04 - Retry and business rules | Same-key replays, changed-payload conflict and double-settlement rejection. |
| 05 - Validation failures | Invalid names, amounts, WIN inputs, currency, missing key and bad pagination return 400. |
| 06 - Authorization failures | No token returns 401; player use of admin operations returns 403. |
| 07 - History pagination | Two-item pages, automatic cursor capture and no overlapping IDs. |
| 08 - Optional fresh-account checks | Missing profile, zero initial balances and insufficient funds at the stated stages. |

Click **Send** on one request at a time. Inspect the response and **Test Results**. Each request uses the appropriate token automatically, so there is no need to switch authorization between player and admin requests.

Before the first BONUS, wait until **Check my three wallets** returns 200. A temporary `409 player_not_provisioned` means provisioning is pending: wait two seconds and resend. Creating a profile triggers wallet creation automatically.

On the first execution, the USD demo adds **110** to the starting balance. A fresh player finishes with USD 110, EUR 0 and BTC 0. Existing balances and history remain. The final balance check uses the actual WIN response, so it also works for a player who already had money. Send the final balance request again after the retry tests to check that retries had no additional effect.

WIN sends `multiplier: "2"` and `bonusAmount: "0"`; both are optional and default to those values. Payout = original bet amount × multiplier + bonusAmount. A $10 bet with `bonusAmount: "5"` credits $25 at 2×. The response `amount` is this total. To try custom terms, use a new unsettled bet and key, and update the request's expected payout assertion and its replay body. WIN no longer accepts input `amount`. Re-import this collection after upgrading; start a new demo for requests previously settled under the old amount-based contract.

Run folder 08 requests only when their stated prerequisites hold. Do not include it in a whole-collection run: for example, a missing-profile check should return 404 **before** profile creation, and correctly returns 200 afterward. The insufficient-funds example assumes EUR is still unfunded.

## Repeat a demo

Idempotency keys use `run_id`, generated once and retained for retries. Resending a transaction returns its original result, including the original balance at that time.

For a deliberately new demo with the same player, clear `run_id`, `bonus_id`, `bet_id`, `win_id` and `expected_usd` in the environment, then restart folder 03 from BONUS. This adds another 110 USD. Do not clear them for a retry. After switching to a different player token, creating or reading that player's profile updates `player_id` and clears the previous demo state automatically.

These manual requests exercise features and sequential retries. They do not establish cross-host concurrency or outage-recovery guarantees; those are covered by the integration suite and separate outage exercises.

To check the token-capture scripts without making network requests, run `node postman/verify-auth-scripts.mjs` with Node 18 or later. The checks execute the actual scripts and ensure wrong-role, expired, mismatched and failed logins cannot populate a token variable.
