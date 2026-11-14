# Technical guide

Detailed authentication, API contracts and reliability decisions for Mini Wallet Platform. Start with the [README](../README.md) to run the project and try the demo.

## Sign in through Swagger

1. Open either Swagger UI and select **Authorize**.
2. Under **oauth2**, keep the provided client ID and select the openid/profile/email scopes. Select its **Authorize** button.
3. Sign in at Keycloak with the player or business-admin account. Swagger uses authorization code with PKCE S256; no client secret or password grant is used.
4. Close the authorization dialog, expand an operation, select **Try it out**, fill its fields and **Execute**.

To switch accounts, open **Authorize**, log out the current Swagger OAuth authorization, then authorize again. `prompt=login` forces a fresh Keycloak login. If the current username is locked, choose **Restart login** (the arrow beside it), then enter the other username. Swagger logout clears its token, not the whole Keycloak browser session. Use only one Swagger authorization method at a time.

Both APIs validate the same issuer and `mini-wallet-api` audience. To demonstrate the exact same access token across services, copy the bearer token from a successful Swagger request's displayed curl command into the other UI's **bearer** authorization field. Tokens expire after five minutes; sign in again when needed. Do not share tokens in screenshots or commits.

Public browser URLs use localhost. Inside Compose, discovery uses the Keycloak service address while the validated issuer remains the public URL. Dynamic backchannel discovery supplies container-reachable signing-key URLs.

For a React/Next.js browser client, use another registered public code/PKCE client with exact redirect URIs and the same API audience. The APIs allow the local web origin `http://localhost:3000` by default; configure `Cors:Origins` for other origins. Business services receive access tokens, never login credentials.

## Fresh registration and verification

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

## Local operations

```sh
docker compose logs -f users-worker wallet-worker
docker compose stop
docker compose up -d
```

Stopping keeps data. Keycloak realm import runs only on initial creation; editing the realm JSON does not replace an existing realm. Keep volumes that contain data you want to preserve.

The local setup uses HTTP, development credentials and single-node PostgreSQL/RabbitMQ. Production deployment requires TLS, secret management, hardened identity-provider settings, backups and infrastructure monitoring.
