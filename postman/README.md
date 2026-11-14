# Manual tests in Postman

Import both JSON files in this directory into your **MiniWallet** workspace, then select the **MiniWallet Local** environment. The collection contains request bodies, expected statuses, response checks and automatic ID capture.

## Set your two tokens

1. Open [Users Swagger](http://localhost:5101/swagger), authorize with OAuth2 as your player, and execute `GET /api/v1/profiles/me`. A 404 is fine if you have not created the profile yet.
2. In the generated **Curl** block, copy only the token after `Authorization: Bearer `, without the prefix or surrounding quotes. Paste it into the environment's **player_token** local value.
3. Open [Wallet Swagger](http://localhost:5102/swagger) and authorize as the business admin (`admin` / `Admin123!`). Execute `GET /api/v1/wallets/me`; even a 409 response provides the generated Curl block. Copy that token into **admin_token**.

Use your own registered player or the seeded `player` / `Player123!` account. Keep tokens as local values; the supplied environment has both blank. Tokens expire after five minutes. If a request returns 401, sign in through Swagger again and replace the relevant token. The same access token works with both APIs.

The current Keycloak clients register Swagger callback URLs only. These requests reuse access tokens from the existing authorization-code + PKCE login. Postman's built-in OAuth flow would need a separately registered callback; no password-grant workaround is used. See [Postman's OAuth documentation](https://learning.postman.com/docs/use/send-requests/authorization/oauth-20) for its callback requirements.

## Send requests in order

| Folder | What to test |
| --- | --- |
| 00 - Service health | Both API processes respond without authentication. |
| 01 - Player profile | Create, repeat creation, read and update your profile. Saves `player_id`. |
| 02 - Admin profile | Admin reads and updates the selected player's profile. |
| 03 - Wallet happy path | Three wallets, BONUS 100, BET 10, WIN 15, balances and history. Saves transaction IDs. |
| 04 - Retry and business rules | Same-key replays, changed-payload conflict and double-settlement rejection. |
| 05 - Validation failures | Invalid names, amounts, currency, missing key and bad pagination return 400. |
| 06 - Authorization failures | No token returns 401; player use of admin operations returns 403. |
| 07 - History pagination | Two-item pages, automatic cursor capture and no overlapping IDs. |
| 08 - Optional fresh-account checks | Missing profile, zero initial balances and insufficient funds at the stated stages. |

Click **Send** on one request at a time. Inspect the response and **Test Results**. Each request uses the appropriate token automatically, so there is no need to switch authorization between player and admin requests.

Before the first BONUS, wait until **Check my three wallets** returns 200. A temporary `409 player_not_provisioned` means provisioning is pending: wait two seconds and resend. Creating a profile triggers wallet creation automatically.

On the first execution, the USD demo adds **105** to the starting balance. A fresh player finishes with USD 105, EUR 0 and BTC 0. Existing balances and history remain. The final balance check uses the actual WIN response, so it also works for a player who already had money. Send the final balance request again after the retry tests to check that retries had no additional effect.

Run folder 08 requests only when their stated prerequisites hold. Do not include it in a whole-collection run: for example, a missing-profile check should return 404 **before** profile creation, and correctly returns 200 afterward. The insufficient-funds example assumes EUR is still unfunded.

## Repeat a demo

Idempotency keys use `run_id`, generated once and retained for retries. Resending a transaction returns its original result, including the original balance at that time.

For a deliberately new demo with the same player, clear `run_id`, `bonus_id`, `bet_id`, `win_id` and `expected_usd` in the environment, then restart folder 03 from BONUS. This adds another 105 USD. Do not clear them for a retry. After switching to a different player token, creating or reading that player's profile updates `player_id` and clears the previous demo state automatically.

These manual requests exercise features and sequential retries. They do not establish cross-host concurrency or outage-recovery guarantees; those are covered by the integration suite and separate outage exercises.
