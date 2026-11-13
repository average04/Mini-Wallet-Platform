# Mini Wallet Platform — Design Baseline

Date: 2026-11-13
Status: approved by Claude in Collidr round 2 at commit 4200919; non-blocking implementation notes incorporated below. No application implementation.
Source: the supplied candidate-brief-v2.md and architecture discussion. The assessment is reference material, not authorization to submit the work or contact anyone.

## Purpose and limits

Build exactly two business services, Users and Wallet, with an off-the-shelf identity provider. Demonstrate correct balances, a consistent structure for a growing team, and choices the candidate can explain and extend in the interview. Deliver source with readable commits, meaningful tests, and a README covering one-command startup and test player/admin accounts.

The brief asks for 8–10 focused hours, with submission one day before the interview; the interview date is unknown. The full core is estimated at 8–11.25 hours after the scope reductions below. This is a planning range, not a promise. Use a hard ten-hour implementation timebox, reserve the final hour for validation/documentation, and disclose unfinished requirements if the core still does not fit. Do not silently remove financial or authorization guarantees to meet an estimate.

## Architecture decisions

- Full Clean Architecture in both services: separate Domain, Application, Infrastructure, API, and Worker projects with identical conventions.
- Focused repository interfaces and a transaction boundary; infrastructure details stay out of business logic, and repository methods do not independently commit one use case's writes.
- Stable platform PlayerIds, standard JWT access-token validation, and provider-specific claim mapping outside the business layers.
- Users owns profiles and external-identity associations. Wallet owns balances/history and a local identity projection, avoiding a Users HTTP dependency on each wallet request.

The following are implementation proposals selected for this revised baseline, rather than claims that the user originally chose every detail:

| Choice | Baseline |
| --- | --- |
| Runtime | Supported .NET 10 LTS SDK/runtime; ASP.NET Core and compatible EF Core/Npgsql versions pinned when implementing. |
| Database | PostgreSQL; separate Users, Wallet, and Keycloak databases/credentials on one local server. |
| Messaging | RabbitMQ, Users transactional outbox, Wallet idempotent inbox, and separate worker hosts. |
| Identity | Keycloak realm import; Mailpit for local verification emails. |
| APIs | Swagger UI with code + PKCE, FluentValidation, Problem Details, /api/v1. |
| Tests | xUnit, Testcontainers PostgreSQL/RabbitMQ, two service test projects with unit/integration/architecture folders. |
| Provisioning | Eventually consistent: profile creation commits first; wallet creation resumes automatically after temporary outages. |
| Demo funding | Admin-only BONUS through the existing transaction endpoint. This small extra transaction type makes the demo fundable without startup seeding or a separate endpoint. |
| WIN | One successful WIN per successful BET; a second settlement conflicts even with a new key. |
| Money | Positive exact amounts; USD/EUR up to 2 fractional digits, BTC up to 8; reject excess precision, no conversion. |

Considered alternatives: outbox plus authenticated background HTTP delivery avoids a broker but adds receiver/auth/retry coordination. RabbitMQ is the baseline; changing transport is an explicit design revision, not a late untested shortcut. Synchronous Users-to-Wallet calls alone cannot protect a committed profile through a Wallet outage. Full event sourcing is unnecessary; retain current balances and append-only successful transaction history.

## Projects and dependencies

    src/Contracts/                         # versioned event DTOs, no shared entities
    src/Users/Users.Domain/
    src/Users/Users.Application/            # Abstractions + Features/<UseCase>
    src/Users/Users.Infrastructure/
    src/Users/Users.Api/
    src/Users/Users.Worker/
    src/Wallet/Wallet.Domain/
    src/Wallet/Wallet.Application/
    src/Wallet/Wallet.Infrastructure/
    src/Wallet/Wallet.Api/
    src/Wallet/Wallet.Worker/
    tests/Users.Tests/                      # Unit, Integration, Architecture
    tests/Wallet.Tests/                     # Unit, Integration, Architecture
    deploy/                                # Dockerfiles and local IdP/infrastructure

This is 13 projects, including Contracts and the two test projects. Domain has no HTTP/infrastructure dependencies. Application references Domain and defines persistence/current-actor ports. Infrastructure implements these ports. API and Worker register Infrastructure at their composition roots and invoke Application use cases. HTTP-specific current-actor access belongs in API; claim mapping/resolution sits behind Application abstractions.

Each service gets the same dependency-rule checks in its existing test project. Do not share entities, DbContexts, or repositories between services. Keep BET, WIN, and BONUS handlers separate behind one execution pipeline. A mediator package, generic repository framework, dedicated architecture-test project, and separate platform acceptance project are unnecessary.

## Authentication and provider replacement

Keycloak owns signup, login, passwords, email verification, resets, and token issuance. Business services expose no login, credential-proxy, or verification endpoints. Both APIs validate signature, issuer, audience, and lifetime using ASP.NET Core JWT bearer middleware and discovery/JWKS. ID tokens are not accepted as API access tokens.

Use an explicitly configured platform audience, mini-wallet-api, in both APIs. Keycloak's public clients must issue that audience, enable authorization code + PKCE S256, disable password grants, and use exact redirect URIs. Configure the client Web Origins for both Swagger origins; this enables browser token exchange. Separately configure API CORS for the future web-app origin. New registrations receive the player role. Normalize permitted nested provider roles into platform permissions, including Profiles.ManageAny, Wallets.ReadAny, Wallets.RecordWin, and Wallets.GrantBonus. Test the same browser-issued access token against both APIs.

Configure both Swagger UIs with the additional OAuth parameter prompt=login. The demo explains clearing Swagger's current authorization and signing in with the other account; separate normal/private browser windows for player/admin are an alternative. Verify the role switch so a retained IdP session does not silently reuse the player's identity. See [Swagger OAuth configuration](https://swagger.io/docs/open-source-tools/swagger-ui/usage/oauth2/).

For the local Docker setup, propose a fixed browser-facing Keycloak hostname of http://localhost:8080 with hostname-backchannel-dynamic enabled. APIs fetch discovery from the internal keycloak:8080 address while validating the public realm issuer. Verify that the discovered JWKS URL is also reachable inside containers: changing MetadataAddress alone does not solve an unreachable jwks_uri. Browser authorization/token URLs remain browser-reachable. Local HTTP metadata is development-only; issuer/signature/audience/lifetime validation remains enabled. The pinned Keycloak version must pass this actual browser/container smoke check. See [Keycloak hostname guidance](https://www.keycloak.org/server/hostname).

Map the exact pair (issuer, subject) to a platform-generated PlayerId. Before profile creation the current actor has external identity/permissions but no PlayerId. A verified-email claim is required to create a profile. Admin operations do not require the admin to have a player profile. PlayerCreatedV1 includes the platform ID and external association; Wallet commits its local lookup with the three wallets.

Preserve a many-to-one identity-association model, with unique (issuer, subject) and an association to an existing player. A focused Users integration test can insert two verified associations and prove both resolve to the same PlayerId. Do not automatically link accounts by email.

Provider migration is documented, not implemented as a live feature: independently verify old/new accounts, add associations to the existing platform IDs in Users, update Wallet's local identity projection through a controlled migration, verify both services resolve the original owners, then cut over configured issuers/claim mapping and retire the old issuer. No profile, balance, or transaction IDs change. The migration must not be declared complete while the services disagree. Live linking commands/events, an additional consumer, ordering/replay machinery, operator tooling, and a second live IdP are deferred. The core demonstrates the replaceable boundary, not a complete migration product.

## API contract

| Service | Endpoint | Behaviour |
| --- | --- | --- |
| Users | POST /api/v1/profiles | Verified player creates own profile: 201 + Location. Repeat for the same identity: 200 + Location to the existing profile; never creates another event or updates it. No Idempotency-Key required. |
| Users | GET/PUT /api/v1/profiles/me | Own profile; 200, missing 404. |
| Users | GET/PUT /api/v1/profiles/{playerId} | Admin profile access; 200, missing 404. |
| Wallet | POST /api/v1/transactions | One endpoint for BET/WIN/BONUS; Idempotency-Key required; success 201 + result + Location. |
| Wallet | GET /api/v1/transactions/{transactionId} | Owner or admin; otherwise 404; returns only successful transactions. |
| Wallet | GET /api/v1/wallets/me | Own balances; 409 player_not_provisioned with Retry-After: 2 until provisioned. |
| Wallet | GET /api/v1/transactions/me | Own cursor-paginated history; same provisioning response. |
| Wallet | GET /api/v1/players/{playerId}/wallets | Admin balances; unknown player 404. |
| Wallet | GET /api/v1/players/{playerId}/transactions | Admin history; unknown player 404. |

Profile data is a trimmed display name of 1–100 characters. Credentials, roles, external associations, and verification flags cannot be changed through profile updates. POST means create-if-absent: a retry returns the current profile even if its submitted display name differs; use PUT to edit. PUT fully replaces editable profile fields.

Transaction inputs:
- BET: type, currency, amount. Player comes from the authenticated actor; no caller-selected playerId.
- WIN: type, betId, amount. Admin only; player/currency come from an earlier successful BET.
- BONUS: type, playerId, currency, amount. Admin only; credit an already provisioned target wallet, with actor and target in the audit record. This is a simulated promotional credit, not a payment/deposit integration.
Reject unknown types, unknown fields, and fields incompatible with the chosen type. All credits require the corresponding admin permission; no player-only token can credit a wallet.

Declare Idempotency-Key as a required OpenAPI header parameter on transaction POST so Swagger's Try it out exposes an editable field. Return PlayerId in the profile response and show how to reuse it in the admin BONUS example.

Monetary JSON values are invariant decimal strings. Use C# decimal and PostgreSQL numeric(28,8), validate scale/range before storage, and check resulting balance limits. Timestamps are UTC ISO 8601. History is ordered by (createdAt, transactionId) descending; cursor contains both, is bound to player/filters, and has default page size 20 and maximum 100.

Use Problem Details with stable code and trace ID: 400 invalid input; 401 unauthenticated/invalid token; 403 insufficient permission; 404 inaccessible/missing referenced transaction or resource; 409 insufficient funds, duplicate WIN, conflicting in-scope idempotency payload, balance limit, or pending provisioning; 503 retryable infrastructure failure. Docs/sign-in assets are public; all business HTTP endpoints require bearer authentication. Infrastructure health checks do not create unauthenticated business routes.

## Atomicity, concurrency, and retries

**Profiles:** atomically insert the profile, unique identity association, and PlayerCreated outbox record. Concurrent duplicate POSTs resolve through the uniqueness constraint to one profile/event; the losing attempt returns the existing profile. A lost HTTP response is recovered by repeating POST. No separate Users idempotency store is needed.

**Wallet operations:** authorize before looking up a replay; open one transaction, reserve/resolve the idempotency key, lock the target wallet row, check business rules, append a successful history entry, update balance, and store the result before commit. Use SELECT FOR UPDATE consistently; the row lock spans the balance read and update across API instances. WIN also verifies an existing successful BET and enforces unique settlement in the database. No network I/O inside wallet transactions. Add nonnegative-balance and positive-transaction-amount constraints.

**History:** only successful BET/WIN/BONUS operations become transaction rows. Rejected attempts such as insufficient funds are retained only as idempotency results, never as debit/history rows or WIN-eligible BETs. Starting from zero, balance equals the signed sum of committed history.

**Idempotency scope:** (targetPlayerId, wallet.transactions.v1, key). Keys are unique per target player, not globally or per admin. An admin may use the same key for different players as separate operations; document this explicitly. Within one target-player scope, the same key and canonical payload returns the original status/result/Location; different payload returns 409. Currency, type, normalized amount and bet/target identifiers participate in the fingerprint. Equivalent decimal spellings normalize. Authorization is checked afresh even on replay; audit records retain the original actor. Concurrent duplicates have one effect. Retain deterministic business failures after reservation, but not authentication/validation errors or transient infrastructure failures. A new business attempt after funds change needs a new key. Retain keys for the lifetime of the take-home data.

**Broker topology:** both workers idempotently declare the same exchange, durable provisioning queue/binding, and dead-letter exchange/queue/binding before publishing or consuming. The publisher can safely start while Wallet.Worker is stopped. Keep declarations consistent across hosts and reconnects.

**Users publisher:** use SELECT FOR UPDATE SKIP LOCKED to claim one pending outbox row in a transaction and publish a persistent message with mandatory routing. Mark published and commit only after a publisher confirm with no basic.return for that publication. A returned message counts as failure even if subsequently confirmed; roll back the claim and retry. See [RabbitMQ confirm semantics](https://www.rabbitmq.com/docs/confirms#when-publishes-are-confirmed). No lease table. A crash after publish but before commit can redeliver; the inbox makes this safe. Limit broker confirmation to three seconds, roll back/release the outbox lock on failure, then back off outside the transaction (1, 2, 4, 8, 16, up to 30 seconds). This deliberately holds only an outbox-row lock over a bounded broker exchange, never a profile/wallet lock. It is a small-system trade-off to document; fail fast on a broken connection.

**Wallet consumer:** durable queue, manual acknowledgements, prefetch 1 per worker. In one transaction commit the inbox marker, local identity lookup, and exactly one zero wallet per USD/EUR/BTC. Acknowledge only after commit. Redelivery cannot recreate/reset a funded wallet. During a transient DB failure, retain the delivery unacknowledged and retry the whole DB transaction with capped backoff; do not immediately nack/requeue in a tight loop. Shutdown closes the channel so unacknowledged work is redelivered. Transient outages never exhaust into a manual-only queue. Reject malformed/conflicting messages to a retained dead-letter queue and log the reason; automated poison-message repair/replay tooling is deferred.

**Outage boundary:** accepted means committed. A committed operation survives restart and a lost response; a request that never reaches a live service must be retried by the client. Profile 201 confirms the profile, not finished Wallet provisioning. Pending reads return the documented retryable conflict, not fabricated balances.

## Startup and usable demo

Start all hosts and dependencies with docker compose up --build from a clean checkout: Users API/Worker, Wallet API/Worker, PostgreSQL, RabbitMQ, Keycloak, Mailpit, and one-shot schema initialization. Start container wiring in the foundation milestone; add migrations with each feature. Do not have each API replica race to migrate. Persist ordinary restarts with volumes.

Keycloak imports local test player/admin accounts and roles; it does not create platform profiles or balances. There is no background demo-money seed. The README walks the reviewer through:
1. Start Compose and sign in as the verified test player through Swagger PKCE.
2. POST the player's profile; wait for the Wallet API's provisioning response to become ready.
3. Confirm USD, EUR, and BTC all start at zero.
4. Switch Swagger authorization to the admin account and submit BONUS for USD 100 using the PlayerId from the profile POST response (or GET /profiles/me) and a documented key in the Idempotency-Key field.
5. Sign in as player and BET USD 10 with a new key; balance becomes USD 90.
6. Sign in as admin and WIN USD 15 against that successful BET; balance becomes USD 105.
7. Inspect the three successful history entries and replay each request with its original key to prove no repeated effect.
8. Register a fresh player at Keycloak, verify through Mailpit, create its profile, and confirm three zero wallets.

Tests may insert wallet/identity fixtures directly into their isolated databases to prove money handling before profile messaging exists. That is test setup, not the runtime demo. Real secrets/tokens do not belong in source or logs.

## Verification and deliberate scope cuts

Use two WebApplicationFactory hosts sharing Testcontainers PostgreSQL for cross-instance wallet correctness; a load balancer demo is unnecessary. Docker must be running for dotnet test; Testcontainers start test dependencies, so the application Compose stack need not be running. Use one representative scenario per guarantee, not duplicate service/platform suites. Required checks include concurrent debits, same-key replay, separate-key duplicate WIN, unauthorized credits/access/replays, precision limits, atomic profile/event creation, duplicate provisioning, automatic outage recovery, and a real PKCE/signup demo. The provisioning fixture starts with no predeclared broker queues and Wallet.Worker stopped; additionally force an unroutable publish and prove the outbox stays pending until routing is restored and retry succeeds.

Cuts already applied: live identity-link command/event/consumer and second IdP; separate architecture/platform test projects; custom lease table; startup money seed; exhaustive crash/replica matrices. Defer SignalR, frontend, further transaction types beyond demo BONUS, production deployment, poison-message replay tooling, and migration UI. The roadmap ranks remaining cuts and checkpoints. Do not cut the required two-instance concurrency, basic duplicate/outage tests, authorization, or durable transaction boundaries.

## References

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Clean Architecture](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [Repository/unit of work](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-design)
- [JWT validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication)
- [OIDC identity stability](https://www.openid.net/specs/openid-connect-core-1_0.html#ClaimStability)
- [RabbitMQ delivery](https://www.rabbitmq.com/docs/reliability)
- [PostgreSQL locking](https://www.postgresql.org/docs/current/explicit-locking.html)
