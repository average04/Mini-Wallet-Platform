# Mini Wallet Platform

Users and Wallet are independent .NET 10 services, each with Domain, Application, Infrastructure, API and Worker projects. Keycloak owns credentials and sign-in. PostgreSQL owns durable business state; RabbitMQ carries profile-created events.

[Design and trade-offs](docs/superpowers/specs/2026-11-13-mini-wallet-platform-design.md) · [Implementation roadmap](docs/superpowers/plans/2026-11-13-mini-wallet-platform-roadmap.md)

## Run

Install Docker Desktop with Linux containers, then run from the repository root:

```sh
docker compose up --build
```

The first build downloads the pinned .NET SDK/runtime and infrastructure images. Wait for the API hosts to start. One-shot initialization containers apply migrations before their API/Worker hosts start; runtime replicas do not race schema migrations.

| Service | URL |
| --- | --- |
| Users Swagger | http://localhost:5101/swagger |
| Wallet Swagger | http://localhost:5102/swagger |
| Keycloak | http://localhost:8080 |
| Mailpit verification inbox | http://localhost:8025 |

Only loopback ports are published. Database and broker ports stay inside the Compose network.

| Local account | Username | Password |
| --- | --- | --- |
| Player, verified email | `player` | `Player123!` |
| Business admin, verified email | `admin` | `Admin123!` |
| Keycloak management admin | `keycloak-admin` | `LocalKeycloak123!` |

These are disposable development credentials, including the database/broker passwords in Compose. The local setup uses HTTP and Keycloak development mode. Use TLS, secret management, hardened IdP configuration, backups and infrastructure monitoring before a production deployment.

```sh
docker compose logs -f users-worker wallet-worker
docker compose stop
docker compose up -d
```

Stopping keeps data. Realm import runs on initial creation; editing its JSON does not replace an existing realm. Do not delete volumes containing data you want to keep.

## Sign in through Swagger

1. Open either Swagger UI and select **Authorize**.
2. Under **oauth2**, keep the provided client ID and select the openid/profile/email scopes. Select its **Authorize** button.
3. Sign in at Keycloak with the player or business-admin account. Swagger uses authorization code with PKCE S256; no client secret or password grant is used.
4. Close the authorization dialog, expand an operation, select **Try it out**, fill its fields and **Execute**.

To switch accounts, open **Authorize**, log out the current Swagger OAuth authorization, then authorize again. `prompt=login` forces a fresh Keycloak login. If the current username is locked, choose **Restart login** (the arrow beside it), then enter the other username. Swagger logout clears its token, not the whole Keycloak browser session. Use only one Swagger authorization method at a time.

Both APIs validate the same issuer and `mini-wallet-api` audience. To demonstrate the exact same access token across services, copy the bearer token from a successful Swagger request's displayed curl command into the other UI's **bearer** authorization field. Tokens expire after five minutes; sign in again when needed. Do not share tokens in screenshots or commits.

Public browser URLs use localhost. Inside Compose, discovery uses the Keycloak service address while the validated issuer remains the public URL. Dynamic backchannel discovery supplies container-reachable signing-key URLs.

For a React/Next.js browser client, use another registered public code/PKCE client with exact redirect URIs and the same API audience. The APIs allow the local web origin `http://localhost:3000` by default; configure `Cors:Origins` for other origins. Business services receive access tokens, never login credentials.

## Reproduce the zero-to-105 demo

No profile or money is seeded at application startup.

1. Sign in as **player** in Users Swagger. POST `/api/v1/profiles` with:
   ```json
   { "displayName": "Demo Player" }
   ```
   Copy `playerId` from the response. First creation returns 201; repeating it returns 200 and the existing profile unchanged.
2. Sign in as **player** in Wallet Swagger. GET `/api/v1/wallets/me`. USD, EUR and BTC appear automatically with zero balances. A brief 409 `player_not_provisioned` with `Retry-After: 2` means the worker has not finished yet.
3. Switch Wallet Swagger to **admin**. POST `/api/v1/transactions`, select its **BONUS** example, replace `playerId`, and enter `demo-bonus-1` in **Idempotency-Key**:
   ```json
   { "type": "BONUS", "playerId": "<copied-player-id>", "currency": "USD", "amount": "100" }
   ```
4. Switch to **player**, select **BET**, and use key `demo-bet-1`:
   ```json
   { "type": "BET", "currency": "USD", "amount": "10" }
   ```
   Copy its `transactionId`. USD is now 90.
5. Switch to **admin**, select **WIN**, replace `betId`, and use key `demo-win-1`:
   ```json
   { "type": "WIN", "betId": "<successful-bet-transaction-id>", "amount": "15" }
   ```
6. GET `/api/v1/players/{playerId}/wallets` and `/api/v1/players/{playerId}/transactions` as admin, or the `/me` routes as player. USD is **105**, the other currencies remain zero, and history contains three successful entries.
7. Repeat the three POSTs with their original payloads, keys and authorized accounts. They return their original results/Locations; balance and history do not change.

The demo assumes a player with no earlier transactions. A new verified account gives a fresh demonstration; do not fund an existing player again with new keys unless intended.

### Fresh registration and verification

In the Keycloak login screen choose **Register**, use a unique username and a test email address such as `new-player@example.test`, and enter the requested name fields. Open Mailpit, open the verification message and follow its verification link. This Keycloak version then asks you to set the account's initial password. If verification opens a separate tab and Swagger reports `authentication_expired`, authorize again and sign in with the new account. New accounts receive the player role automatically. After verification, create a profile through Users Swagger and read the three zero wallets in Wallet Swagger. Password resets and verification remain entirely in Keycloak.

## API contract

| API | Operation | Access |
| --- | --- | --- |
| Users | POST `/api/v1/profiles` | Verified player creates their profile |
| Users | GET/PUT `/api/v1/profiles/me` | Own profile |
| Users | GET/PUT `/api/v1/profiles/{playerId}` | Admin |
| Wallet | POST `/api/v1/transactions` | BET: own player; WIN/BONUS: admin |
| Wallet | GET `/api/v1/wallets/me` | Own balances |
| Wallet | GET `/api/v1/transactions/me` | Own successful history |
| Wallet | GET `/api/v1/players/{playerId}/wallets` | Admin |
| Wallet | GET `/api/v1/players/{playerId}/transactions` | Admin |
| Wallet | GET `/api/v1/transactions/{transactionId}` | Owner/admin; otherwise 404 |

Profile updates accept only `displayName` (trimmed, 1–100 characters). Client-supplied identities, roles and unknown properties are rejected.

Amounts are positive invariant **JSON strings**: USD/EUR allow at most two decimal places; BTC allows eight. Exponents, floats as JSON numbers, excess precision and overflow are rejected rather than rounded. Balances use decimal arithmetic and PostgreSQL `numeric(28,8)`. There is no currency conversion.

BET derives the player from the authenticated identity. WIN derives player/currency from an earlier successful BET and can settle it once. BONUS is a small admin-only addition to fund the demo through the same protected pipeline. Payout amount is supplied by the admin; no game engine or payout formula is modeled.

Every transaction POST requires **Idempotency-Key** (1–128 ASCII letters, digits, `-`, `_`, `.` or `:`). Scope is target player + `wallet.transactions.v1` + key. The same key can be used independently for a different player. Equivalent money strings such as `10` and `10.00` have the same fingerprint. Changed payload within a scope returns 409. Authorization is checked again before replay.

Successful operations return 201 with a transaction Location, including replays. Deterministic business rejections are remembered for that key but never enter transaction history. For example, an insufficient-funds rejection remains rejected after later funding; use a new key for a new attempt. Invalid input and infrastructure failures do not reserve a completed receipt.

History returns `{ "items": [...], "nextCursor": "..." }`, ordered by `(createdAt, transactionId)` descending. Default `limit` is 20, maximum 100. Pass the opaque cursor unchanged; it is versioned and bound to its player. Newer appended entries do not duplicate earlier pages. This is a forward page traversal, not a database snapshot across arbitrary backdated imports.

Errors use Problem Details with `code` and `traceId`: 400 validation, 401 invalid/missing token, 403 permission denied, 404 missing/inaccessible resource, 409 business conflict/pending provisioning, 503 unavailable storage. Pending provisioning and retryable storage errors include `Retry-After: 2`. Swagger and liveness endpoints are public; business endpoints require bearer authentication.

## Architecture and failure handling

There are **13 projects**: five per service, two service test projects and one small Contracts project.

- **Domain:** money, balance and profile invariants; no framework/database dependencies.
- **Application:** use cases, transaction handlers, permissions and focused repository/unit-of-work ports.
- **Infrastructure:** EF mappings/migrations, PostgreSQL repositories and RabbitMQ adapters.
- **API:** HTTP contracts, validation composition, provider-specific claim mapping and bearer validation.
- **Worker:** independent background-processing composition root.

Users and Wallet have separate databases/roles. They never read each other's database. A shared PostgreSQL container is a local hosting convenience, not shared ownership. Domain/Application dependency tests enforce inward references. Focused repositories isolate persistence changes; changing a database still requires suitable adapters, migrations and equivalent concurrency guarantees.

A wallet transaction reserves its key, locks the target wallet with `SELECT FOR UPDATE`, applies a handler, appends successful history and saves the response in one transaction. Row locks work across API processes. A unique WIN/bet constraint and nonnegative balance constraints add database protection. No network call happens inside this transaction.

Profile creation commits profile + unique external identity + outbox event atomically. Concurrent profile POSTs resolve to one player and one event. The Users worker claims one pending row with `FOR UPDATE SKIP LOCKED`, publishes a persistent mandatory message with publisher-confirm tracking, then marks it published. The claim/publish transaction has a three-second cancellation bound. Unroutable returns and negative confirmations fail publication; retries back off outside the transaction.

Both workers declare the durable exchange, queue, binding and dead-letter topology on connection. Users can publish before Wallet's worker starts. The Wallet worker uses prefetch 1 and manual acknowledgment; inbox marker, identity projection and three zero wallets commit before acknowledgment. Duplicate delivery is harmless and never resets funded wallets. Transient failures retain the unacknowledged delivery and retry at 1, 2, 4, 8, 16 and then 30 seconds; connection loss requeues it.

Malformed or conflicting messages go to the retained `mini-wallet.provisioning.dead` queue. Inspect worker logs and `docker compose exec rabbitmq rabbitmqctl list_queues name messages_ready messages_unacknowledged`. Investigate the event ID and source data before any manual replay. Automated poison replay is deferred.

“Accepted” means committed. A committed request survives process restart or a lost response; clients retry requests that never reached a live API. Profile 201 confirms durable profile creation, not completion of asynchronous wallet provisioning.

### Replace the identity provider

Business ownership uses platform-generated `PlayerId`, never email or a provider's subject alone. Each service resolves an exact `(issuer, subject)` association. Trusted token claims are converted to application permissions in the API adapter; JWT/provider types do not enter business use cases.

For a new provider, register code/PKCE clients and API audience, update issuer/discovery/JWKS configuration, and adjust the API claim-to-permission adapter. Validate signatures, issuer, audience, expiry and required subject throughout. For existing accounts, independently verify old/new identities, add the new association to the existing PlayerId in Users and Wallet through a controlled migration, verify both services agree, then cut over and retire the old issuer. Never link by matching email alone. No balance, profile or transaction IDs change.

Multiple associations per PlayerId are supported and identity resolution is tested. Live linking UI/events, dual-provider rollout automation and a second live provider are deliberately deferred.

### Add a transaction type

Implement an `ITransactionHandler` in Wallet.Application, define its permission and allowed request fields, register it in the API composition root, extend validation/OpenAPI examples and database constraints if needed, and add money/authorization/replay/concurrency cases. Keep mutations inside the shared executor so a new handler inherits the row lock, atomic history and idempotency behavior.

## Build and tests

Install the pinned stable **.NET SDK 10.0.404**. Docker must be running for the real PostgreSQL/RabbitMQ Testcontainers tests; the application Compose stack does not need to be running.

```sh
dotnet restore MiniWalletPlatform.sln
dotnet build MiniWalletPlatform.sln --no-restore
dotnet test MiniWalletPlatform.sln --no-build
docker compose config --quiet
```

This workspace also has an ignored local SDK: on Windows, replace `dotnet` with `.\.tools\dotnet\dotnet.exe` if the pinned SDK is not on PATH. It is not committed or required by Compose.

Tests cover precision/overflow, two API hosts spending the same funds, in-flight duplicate keys, lost responses, one WIN per BET, fresh authorization before replay, transaction rollback, profile races/outbox atomicity, identity continuity, shared JWT validation, cursor ties/appends/access, mandatory returns, cold publisher startup, duplicate provisioning and automatic database-outage recovery. Containers are disposable test-owned fixtures.

Verified on 2026-11-13: **89 tests passed** (Users 24, Wallet 65), with no skipped tests. A separate fresh Compose project and fresh volumes started successfully with `docker compose up --build`. Actual browser acceptance covered new registration, Mailpit verification, PKCE in both Swagger UIs, shared-token access, player/admin switching, automatic zero wallets, the USD 105 demo, and all three safe replays. Final history contained exactly three entries. The original local project volumes were preserved.

Implementation began at 08:29 UTC; final elapsed time and review results will be recorded at handoff.

## Scope and assumptions

Required backend functionality is implemented. Optional React UI, SignalR, game engine, multi-currency conversion, refunds, production deployment, external real-money integration and automated IdP migration are excluded. One successful WIN settles one BET; no multiple partial settlements. UTC timestamps use database-compatible precision. Single-node local PostgreSQL/RabbitMQ demonstrates recovery from temporary outages, not replicated infrastructure disaster recovery.

The take-home brief allows AI assistance. The implementation follows the approved design and keeps its trade-offs explicit so the candidate can explain and extend it. Source is kept on a feature branch; publishing/submitting the repository is a separate action.

