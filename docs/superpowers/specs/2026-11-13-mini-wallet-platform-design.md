# Mini Wallet Platform — Design Baseline

Date: 2026-11-13
Status: planning baseline for review; no application has been implemented.
Source: the candidate's supplied `candidate-brief-v2.md` and architecture discussion. The assessment document is reference material, not authorization to submit or contact anyone.

## Purpose and scope

Build a backend that demonstrates correctness, maintainability, and a repeatable service structure for a growing team. Target 8–10 focused hours and a solution the candidate can explain and extend during the interview. Deliver source with readable commits, tests, and a README with one-command startup and test-player/admin instructions. Submission is due one day before the interview; the interview date has not been supplied.

Build exactly two business services: Users and Wallet. Run an existing identity provider. API and worker processes belong to their respective services; they are not additional business services.

## Decisions from the discussion

- Use full Clean Architecture in both services, with separate Domain, Application, Infrastructure, API, and Worker projects and identical conventions.
- Use focused repository interfaces and a transaction/unit-of-work boundary. EF Core details stay in Infrastructure; repository methods do not independently commit parts of one use case.
- Keep identity-provider concerns outside business logic. Validate standard JWT access tokens, normalize claims, and link external identities to stable platform player IDs.
- Users owns profiles and identity associations. Wallet owns wallets and transaction history. Wallet resolves player identity from its local projection rather than calling Users on each request.

## Proposed defaults, not yet individually selected by the user

| Decision | Proposed default and reason |
| --- | --- |
| Runtime | .NET 10 LTS, ASP.NET Core, EF Core/Npgsql; satisfies the brief's .NET 8-or-later requirement. Pin supported compatible versions when implementing. |
| Persistence | PostgreSQL: separate databases and credentials for Users and Wallet; a single database server is sufficient locally. Keycloak has its own database. |
| Messaging | RabbitMQ, a Users transactional outbox, and a Wallet inbox for durable automatic provisioning. |
| Identity provider | Keycloak with imported development configuration; Mailpit receives local verification emails. |
| API tooling | Swagger UI with authorization code + PKCE on both services, FluentValidation, Problem Details errors, `/api/v1` routes. |
| Tests | xUnit; domain/application unit tests and integration tests against real PostgreSQL/RabbitMQ. |
| Provisioning semantics | A profile can be committed before its wallets exist. Provisioning resumes automatically after outages. Document this eventual-consistency interpretation explicitly. |
| Demo funding | A development-only fixture credits an existing demo player's wallets with an auditable seed entry. Newly registered players still receive zero balances. There is no public funding endpoint. |
| WIN rules | One successful WIN per BET for the core submission. A separate attempt to settle that BET again returns a conflict, even with a different idempotency key. Partial/multiple payouts are deferred. |
| Currency rules | USD/EUR allow up to 2 fractional digits; BTC up to 8. Amounts must be positive and exactly representable; reject excess precision instead of rounding. No currency conversion. |

## Approaches considered

1. **Outbox + RabbitMQ + independent workers — recommended baseline.** One additional infrastructure component, explicit delivery acknowledgements, and straightforward future subscribers.
2. **Outbox + background HTTP delivery.** Fewer containers, but requires a service-authenticated receiver and custom delivery/retry coordination. Viable if broker setup threatens the time budget; changing to it is an explicit design revision, not silently removing durability.

Simple synchronous Users-to-Wallet calls alone do not handle a committed profile followed by a Wallet outage. Full event sourcing, a gateway, service mesh, Kubernetes, and a frontend do not serve the required core within this time budget. Persist current balances and append-only transaction history.

## Service structure and dependency rules

```text
src/
  Contracts/                         # versioned integration-event DTOs only
  Users/
    Users.Domain/
    Users.Application/               # Abstractions and Features/<UseCase>
    Users.Infrastructure/
    Users.Api/
    Users.Worker/
  Wallet/
    Wallet.Domain/
    Wallet.Application/
    Wallet.Infrastructure/
    Wallet.Api/
    Wallet.Worker/
tests/
  Architecture.Tests/
  Users.Tests/                       # Unit/ and Integration/ folders
  Wallet.Tests/                      # Unit/ and Integration/ folders
  Platform.AcceptanceTests/
deploy/                              # containers, Keycloak import, local infrastructure
```

Domain has no infrastructure or HTTP dependencies. Application references Domain and defines persistence/current-actor ports. Infrastructure implements these ports. API and Worker are composition roots: they register Infrastructure but execute Application use cases. HTTP-specific current-actor access belongs in API, while claim normalization and identity resolution sit behind application abstractions. Workers execute explicitly scoped system operations, not forged HTTP principals.

Share event contracts, not entities, DbContexts, business repositories, or databases. Enforce project dependencies with an architecture test. Use feature folders within Application and keep BET/WIN handling independently extensible behind the single transaction endpoint. Do not require a mediator library or a generic CRUD framework.

## Authentication and stable identity

- Keycloak owns signup, login, credentials, verification, password resets, and token issuance. Neither business service exposes login/password/email-verification endpoints.
- Both APIs validate access-token signature, trusted issuer, audience, and lifetime using ASP.NET Core JWT bearer middleware and OIDC discovery/JWKS. Only configured issuers are trusted. ID tokens are not API access tokens.
- Browser clients and both interactive API docs use authorization code + PKCE. Disable password grant for those clients. Configure an explicit platform API audience (or both service audiences) so the same browser-issued access token is valid in Users and Wallet. Permit only configured local browser origins through CORS. Use a canonical issuer and reachable discovery/key endpoints that work from both the host browser and containers; prove this through an actual browser sign-in. Do not disable issuer validation to solve Docker networking.
- Normalize explicitly permitted provider roles to platform permissions such as `Profiles.ManageAny`, `Wallets.ReadAny`, and `Wallets.RecordWin`. Preserve current token authorization checks on every request, including idempotent replays.
- Link `(issuer, subject)` to a platform-generated `PlayerId`. Before profile creation the authenticated actor has an external identity and permissions but no PlayerId. Signup/profile creation requires a trusted verified-email claim.
- `PlayerCreatedV1` carries the platform player ID and external-identity association. Wallet stores its local lookup in the same transaction as the three wallets. An unresolved player cannot access someone else's wallets.
- For future provider changes, add verified identity associations to the existing PlayerId; never auto-link by email. Define an internal Users application command `LinkExternalIdentityCommand(PlayerId, Issuer, Subject)` that atomically commits the unique association and a `PlayerIdentityLinkedV1` outbox event, plus its idempotent Wallet consumer. This is an operator-controlled action requiring independently verified account ownership, not a public player endpoint. The take-home invokes this application path in an integration test and documents the operator migration procedure; a public account-linking feature, operator CLI/UI, and second live IdP are deferred. This is additive linking; retiring the old issuer is a separate controlled configuration cutover, not automatic unlinking.
- Identity-linked events can arrive before player-created events: store the association independently, but do not provision wallets or permit transactions until the PlayerCreated event is applied. Conflicting associations are quarantined, not reassigned.

## API contract baseline

| Service | Endpoint | Access and result |
| --- | --- | --- |
| Users | `POST /api/v1/profiles` | Verified player; create own profile, 201 + Location. Requires Idempotency-Key. |
| Users | `GET/PUT /api/v1/profiles/me` | Own profile; 200; missing profile 404. |
| Users | `GET/PUT /api/v1/profiles/{playerId}` | Admin only; 200; missing profile 404. |
| Wallet | `POST /api/v1/transactions` | Single endpoint dispatches BET or WIN; 201 + transaction result. Requires Idempotency-Key. |
| Wallet | `GET /api/v1/wallets/me` | Player's balances; 200, or 409 `player_not_provisioned` until identity/wallet provisioning completes. |
| Wallet | `GET /api/v1/transactions/me` | Player's cursor-paginated history; same provisioning rule. |
| Wallet | `GET /api/v1/players/{playerId}/wallets` | Admin balances; 200; unknown player 404. |
| Wallet | `GET /api/v1/players/{playerId}/transactions` | Admin cursor-paginated history; unknown player 404. |

Profile data is a display name (1–100 trimmed characters); additional platform profile fields are optional. Credentials, identity associations, roles, and verification flags are not editable profile fields.

BET input: `type`, `currency`, `amount`. Determine the player from the token's resolved identity. WIN input: `type`, `betId`, `amount`; resolve the target player and currency from the original successful BET. Reject incompatible/unknown transaction fields and unknown types. An admin-only identity cannot place bets for other players; an actor with a player identity can only bet for itself.

Represent monetary values as invariant decimal strings in JSON to avoid browser number precision loss. Use C# decimal and PostgreSQL numeric(28,8), with currency-specific scale validation and checked range handling for amounts and resulting balances. Serialize timestamps as UTC ISO 8601. History sorts by `(createdAt, transactionId)` descending; cursor contains both, is validated against filters/player, and has default page size 20 and maximum 100.

Use consistent Problem Details with a stable `code` and trace ID. 400 for invalid input; 401 for missing/invalid authentication; 403 for insufficient permission; 404 for inaccessible/missing referenced resources as appropriate; 409 for insufficient funds, duplicate settlement, mismatched idempotency payload, balance limits, or unprovisioned wallets; 503 for retryable infrastructure unavailability. OpenAPI includes request/response examples, permissions, PKCE instructions, pagination, and retry semantics. All business HTTP endpoints require bearer authentication; docs/sign-in assets are public, and infrastructure liveness checks do not become public business endpoints.

## Correctness and durability

**Profile creation:** in one Users transaction, create the profile, its unique external association, the durable idempotency result, and a PlayerCreated outbox event. Same-key/same-payload retries replay the response; a new key for the same registered identity returns a conflict without a new profile or event. PUT is a full deterministic replacement of the editable profile fields.

**Outbox and consumer:** workers claim work with short database leases suitable for multiple instances. Publish persistent messages to durable queues with publisher confirms and routing-failure handling; mark published only after confirmation. A crash between publish and marking can duplicate delivery. Wallet commits the inbox marker, identity projection, and all three unique `(playerId, currency)` wallets together, then acknowledges. Redelivery cannot reset a funded wallet to zero. During transient database/broker outages, keep work durable and retry automatically with capped backoff; reaching a retry count must not strand it awaiting manual action. Quarantine only poison/invalid messages and ownership conflicts in a visible dead-letter queue with documented replay. Expired worker leases are reclaimable. Broker I/O does not run inside a wallet database transaction.

**BET/WIN:** enforce authorization before replay lookup; open one short database transaction, reserve/resolve the idempotency key, lock the target wallet row, check rules, append history, change balance, and store the result before committing. The lock covers the balance read and write across all API instances. WIN additionally verifies the original BET and enforces unique settlement in the database. Keep network calls outside the transaction. Add database checks for nonnegative balances and positive transaction amounts.

**Idempotency:** wallet operation keys are scoped to `(targetPlayerId, wallet.transactions.v1, key)` so identity-provider migration does not change the retry scope. Audit the actual authenticated actor separately. Fingerprint canonical validated business fields; equivalent decimal spellings normalize to the same value. Profile keys use `(issuer, subject, profiles.create.v1, key)` before a platform ID exists. Same key/different canonical payload is 409. Concurrent duplicates produce one effect and replay one durable response. Deterministic business rejections after reservation, such as insufficient funds, are retained as results; authentication/input failures and transient infrastructure failures are not cached. A different authorized attempt after funds change needs a new key. Keep keys for the life of the take-home data.

**Outage boundary:** a committed operation is durable and retryable without duplication even if its HTTP response is lost. A request that never reaches a running service is not accepted and must be retried by the client. Profile creation returns the created profile, not a promise that Wallet has already finished. Reads while provisioning return the documented pending conflict rather than fabricated zero balances.

## Runtime and demo

`docker compose up --build` must start the application from a clean checkout: Users API/Worker, Wallet API/Worker, PostgreSQL, RabbitMQ, Keycloak, Mailpit, and controlled one-shot database initialization. Introduce container startup in the foundation milestone and each service's migrations alongside its first persistence feature; do not leave runtime assembly to the last milestone. Separate API and worker entrypoints and migrations prevent every API replica racing to migrate.

Import local-only test player/admin identities and roles. Make the demo player's profile and wallets available through the same provisioning flow, then apply one stable, idempotent, development-only seed credit with visible history. New profiles still start at zero. Document local credentials, verification-mail UI, sign-in steps, and an end-to-end BET/WIN example. Keep real credentials and tokens out of source and logs. Docker volumes preserve state across ordinary restarts; a destructive reset is a separate explicit operator action.

## Validation and non-goals

Prove concurrent debits, duplicate request/event delivery, permission isolation, one WIN per BET, currency boundaries, lost responses, process restarts, and service/broker outages against real dependencies. Unit tests cover business rules; mocks do not prove locking or transaction atomicity. Each guarantee gets one representative integration scenario reused by the final acceptance run, not a duplicate suite for every host/process permutation. Verify additive identity linking through the Users application command, durable delivery, and both services' identity resolution while preserving the same platform player and balances. Perform a real PKCE sign-in in both API docs, prove the same token works across them, and exercise fresh signup/email verification.

Defer SignalR, a React/Next frontend, extra transaction types, production deployment, account migration tooling/UI, a second live IdP, exhaustive chaos/replica matrices, and elaborate generic frameworks. Reserve time for the README and interview explanation. The 8–10-hour target is ambitious from an empty repository: prioritize one demonstrable path per required guarantee, track elapsed time, and explicitly record unfinished requirements rather than claiming complete coverage. The time budget is an estimate, not a claim that all proposed infrastructure has already been tested.

## Reference guidance

- [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy)
- [Clean Architecture](https://learn.microsoft.com/en-us/dotnet/architecture/modern-web-apps-azure/common-web-application-architectures)
- [Repository and unit of work](https://learn.microsoft.com/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/infrastructure-persistence-layer-design)
- [JWT bearer validation](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-jwt-bearer-authentication)
- [OIDC identity stability](https://www.openid.net/specs/openid-connect-core-1_0.html#ClaimStability)
- [Transactional outbox](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/multi-container-microservice-net-applications/subscribe-events)
- [RabbitMQ delivery guarantees](https://www.rabbitmq.com/docs/reliability)
- [PostgreSQL row locking](https://www.postgresql.org/docs/current/explicit-locking.html)
