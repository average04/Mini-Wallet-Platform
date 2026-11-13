# Mini Wallet Platform Implementation Roadmap

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans or superpowers:subagent-driven-development to implement an approved plan task-by-task. This document is a milestone roadmap; refine the current milestone into concrete interfaces and tests before coding it. Checkboxes describe future work, not completed implementation.

**Goal:** Deliver an interview-ready Users and Wallet backend with correct money handling, replaceable identity, durable provisioning, and a reproducible local demo.

**Architecture:** Full Clean Architecture in each service, focused repositories, independent API/Worker hosts, stable platform player IDs, and separate service-owned databases. The proposed integration mechanism is a transactional outbox, RabbitMQ, and an idempotent Wallet consumer.

**Tech Stack:** Proposed .NET 10, ASP.NET Core, EF Core/Npgsql, PostgreSQL, RabbitMQ, Keycloak, Mailpit, Swagger UI, FluentValidation, xUnit, Docker Compose.

**Spec:** [Design baseline](../specs/2026-11-13-mini-wallet-platform-design.md). Read the decisions and proposed defaults separately; proposals are not represented as user-selected choices.

**Status:** Roadmap created on 2026-11-13. No application milestones are complete. The repository was empty when this was written; .NET 10 SDK and Docker Compose are installed locally, but container startup has not been verified.

## Global constraints

- "C# on .NET 8 (or later)"; "ASP.NET Core"; "OpenAPI" with an interactive UI.
- "Every backend call uses a JWT bearer token"; browser sign-in uses "authorization code flow with PKCE".
- "Your services never receive a password: no login endpoints, no password grant, no proxying credentials to the identity provider."
- "Structure both services the same way."
- Supported currencies: "USD, EUR, BTC"; new-player wallets start "at a balance of zero".
- "The Wallet service exposes one transaction endpoint for all transaction types."
- A player cannot record WINs, overdraw a wallet, or access another player's resources; admins can manage profiles, read all balances/history, and record WINs against earlier BETs.
- "For each service, the API and any background processing must be deployable and scalable independently of each other."
- "When that happens, no profile creation or wallet operation may be lost or duplicated." Define acceptance and client retry boundaries as in the design baseline.
- "Docker and Docker Compose: the whole system starts with one command"; deliver source, tests, README, demo player/admin, and readable commits.
- "plan for about 8–10 hours"; submission is one day before the scheduled interview, whose date is not yet known.

## Sequence and time budget

| Milestone | Working result | Target |
| --- | --- | --- |
| 1 | Service boundaries, container skeleton, build, and contracts | 0.75 h |
| 2 | Real IdP sign-in and protected interactive APIs | 1.5 h |
| 3 | Profiles with durable creation | 1 h |
| 4 | Automatic wallets through durable messaging | 1.25 h |
| 5 | Correct BET/WIN transaction endpoint | 2 h |
| 6 | Authorized balances and paginated history | 0.75 h |
| 7 | Combined acceptance run of existing failure scenarios | 0.5 h |
| 8 | Reproducible demo, README, and interview walkthrough | 1.25 h |
| Reserve | Fix failures in the required core | 1 h |
| **Total** | **Nine hours planned work plus one hour reserve** | **10 h** |

These are ambitious timeboxes from an empty repository, not guaranteed estimates. Tests and documentation are part of every milestone; milestone 7 reruns the existing scenarios rather than creating another failure-test suite. At hours 4 and 7, reassess against the remaining time. Use one representative scenario per required guarantee, a shared real-dependency fixture, and one test project per service with Unit/Integration folders. Defer exhaustive crash-point/replica combinations and a second live IdP. Cut optional features first, and record any unmet core requirement honestly rather than weakening correctness or exceeding the brief substantially.

## Review focus

Five particularly important failure modes are assigned to the milestones below:

1. Token issuer/audience/claim variations or a new external identity must never change player ownership or grant admin rights (2, 4, 7).
2. Duplicate profile requests and event redelivery after a crash must yield one profile and exactly three wallets without resetting balances (3, 4, 7).
3. Concurrent debits and a response lost after commit must preserve both nonnegative balances and exactly one monetary effect per key (5, 7).
4. Concurrent WINs with different keys must not settle one BET twice, and players must not bypass authorization through replay (5, 7).
5. Excess currency precision, numeric overflow, and malformed/cross-player pagination cursors must fail predictably without corrupting money or exposing history (5, 6).

## Milestone 1 — Establish the service template and contracts

**Files:** `MiniWalletPlatform.sln`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore`, `compose.yaml`, container build files, projects under `src/{Users,Wallet}/`, `src/Contracts/`, and `tests/Architecture.Tests/DependencyRulesTests.cs`.

- [ ] Review the design defaults: RabbitMQ, eventual provisioning, demo funding, one WIN per BET, and currency precision. Record any revisions before dependent work.
- [ ] Create the five projects per service and the test projects using the documented dependency direction. Pin a stable SDK and compatible supported package/container versions; do not use the installed preview SDK.
- [ ] Establish container build/startup and database initialization scaffolding now; add each service's migrations with its first persistence feature. Validate Compose configuration and host/container connectivity before extending the system.
- [ ] Freeze `/api/v1` routes, transaction discriminator, money representation, Problem Details codes, and versioned event DTOs from the design. Keep the contract small enough to finish.
- [ ] Define current-actor, focused repository, and transaction ports in Application; define per-type transaction handlers with one common execution boundary.
- [ ] Add an architecture test proving Domain/Application cannot reference Infrastructure/API/Worker or the other service's internals.
- [ ] Verify `dotnet build MiniWalletPlatform.sln` and `dotnet test tests/Architecture.Tests`; commit the working template.

**Exit:** Both services build with enforceable, identical Clean Architecture boundaries. No core business rules live in the hosts.

## Milestone 2 — Make authentication work end to end

**Files:** `compose.yaml`, `deploy/keycloak/realm.json`, `deploy/postgres/init-databases.sql`, `deploy/mailpit/`, both API `Program.cs` files, each service's `Application/Abstractions/ICurrentActor.cs`, API authentication adapters, Infrastructure identity adapters, and `tests/Platform.AcceptanceTests/AuthenticationTests.cs`.

- [ ] Configure Keycloak public clients, PKCE, audiences, role mapping, email verification, and local-only test identities. Configure Mailpit for verification messages.
- [ ] Protect both APIs with JWT validation and configure each Swagger UI to sign in through the IdP. Prove host/container issuer and JWKS addressing, a shared platform audience (or both audiences), and configured-origin CORS. Verify the same browser-issued token passes each API's authentication boundary.
- [ ] Add middleware/claim-normalization tests for missing/expired tokens, wrong issuer/audience, unsigned tokens, and absent admin permissions. Keep endpoint-specific access assertions in milestones 3, 5, and 6, where those endpoints exist.
- [ ] Normalize claims through a provider adapter. Model an authenticated actor without a PlayerId and an admin without a player profile; later use cases resolve their appropriate permissions.
- [ ] Verify a player and admin can sign in through both API docs using code + PKCE, and verify signup/email verification via Keycloak and Mailpit. Use a controlled signing authority for negative integration tests; tests do not replace real browser verification.
- [ ] Run the authentication tests and commit.

**Exit:** The reviewer can authenticate through the browser; business code has no Keycloak dependency and no credential-handling endpoint exists.

## Milestone 3 — Implement profiles and the Users outbox

**Files:** `Users.Domain/Profiles/PlayerProfile.cs`; `Users.Application/Features/Profiles/{Create,Get,Update}/`; `Users.Application/Features/Identity/LinkExternalIdentity/`; Users Infrastructure persistence/migrations, identity links, idempotency, and outbox implementations; `Users.Api/Endpoints/ProfilesEndpoints.cs`; `Contracts/{PlayerCreatedV1,PlayerIdentityLinkedV1}.cs`; `tests/Users.Tests/{Unit,Integration}/`.

- [ ] Write tests for create/read/update ownership, admin access, validation, unverified signup, and attempted role/identity-field changes.
- [ ] Implement platform-generated player IDs and unique `(issuer, subject)` associations. Define `PlayerCreatedV1` with event ID, schema version, timestamp, player ID, issuer, and subject.
- [ ] Implement internal `LinkExternalIdentityCommand(PlayerId, Issuer, Subject)` for a previously verified operator request. Commit the unique new association and its PlayerIdentityLinkedV1 outbox event together; reject reassignment to another player. Exercise this application command in tests; public endpoints and operator CLI/UI are deferred.
- [ ] Commit profile, identity link, idempotency response, and outbox event in one transaction. Handle repeated concurrent profile requests with the same and different keys deterministically.
- [ ] Assert one profile/link/event on duplicate creation, unchanged state for failed authorization/validation, replay after a lost response, and rollback of all records on an injected transaction failure.
- [ ] Run `dotnet test tests/Users.Tests`; commit.

**Exit:** Profile management works without Wallet being available, and its provisioning event is durable.

## Milestone 4 — Provision wallets reliably

**Files:** `Users.Worker/Program.cs`, Users outbox publisher/lease persistence; `Wallet.Worker/Program.cs`; `Wallet.Domain/Wallets/Wallet.cs`; Wallet provisioning and identity-link handlers; Wallet inbox/projection repositories and migrations; `tests/Wallet.Tests/Integration/ProvisioningTests.cs`; `tests/Platform.AcceptanceTests/IdentityLinkingTests.cs`.

- [ ] Write a representative real-database/broker scenario covering worker downtime, redelivery after commit, and a broker outage longer than the initial retry interval. Assert automatic recovery without manual replay and safe competing worker claims; reuse these scenarios in milestone 7.
- [ ] Publish with confirmations, persistent messages, durable routing, and expiring work claims. Keep transient outage work pending with capped automatic backoff; quarantine poison/conflicting messages separately with a documented replay procedure.
- [ ] Consume PlayerCreatedV1 atomically: inbox marker, local identity association, and exactly one zero wallet for each of USD/EUR/BTC. Acknowledge only after database commit.
- [ ] Add the idempotent identity-link consumer. Invoke the Users command from milestone 3, deliver its real outbox event, and assert the new identity resolves to the original profile and wallets. Cover event-before-PlayerCreated ordering and ownership conflicts. Use configured test authorities rather than deploying a second live IdP.
- [ ] Replay a provisioning event after a test credits an existing wallet; assert the credit survives. Assert a fresh player receives exactly three zero balances.
- [ ] Run `dotnet test tests/Wallet.Tests --filter Provisioning` and `dotnet test tests/Platform.AcceptanceTests --filter IdentityLinking`; commit.

**Exit:** Creating a profile automatically provisions wallets, and outages/duplicates cannot lose provisioning or reset balances. API and worker deployments are separate.

## Milestone 5 — Implement the single BET/WIN endpoint

**Files:** `Wallet.Domain/Transactions/`, `Wallet.Domain/Money/`; `Wallet.Application/Features/Transactions/{Execute,Bet,Win}/`; wallet repository/transaction/idempotency/history persistence; `Wallet.Api/Endpoints/TransactionsEndpoints.cs`; `deploy/seed/`; `tests/Wallet.Tests/Unit/TransactionRulesTests.cs`; `tests/Wallet.Tests/Integration/TransactionExecutionTests.cs`.

- [ ] Write domain tests for positive amounts, currency scale, insufficient funds, arithmetic/storage limits, and WIN referencing a valid BET.
- [ ] Implement the shared request pipeline and distinct BET/WIN handlers. Derive BET ownership from identity and WIN ownership/currency from the referenced BET. Reject cross-player BETs and player-issued WINs.
- [ ] Add the development-only seed path alongside money persistence: fund an existing provisioned demo player with one stable, visible seed entry. Repeated startup/seed cannot credit twice; fresh players still start at zero. Reuse it for the final walkthrough.
- [ ] In one database transaction, reserve the player-scoped idempotency key, lock the wallet, enforce rules, persist history/balance, and store the response. Persist deterministic business rejections as specified; roll back transient failures.
- [ ] Assert that two simultaneous USD 80 BETs against USD 100 yield one success, one insufficient-funds result, and USD 20 remaining. Repeat against two independently hosted API instances sharing the same database.
- [ ] Assert concurrent same-key requests cause one effect; changed payload conflicts; a response lost after commit replays the original result; a previously rejected key remains rejected after funding.
- [ ] Assert two admin WINs with different keys against one BET produce one settlement, and an unauthorized caller cannot retrieve a cached WIN response by replaying its key.
- [ ] Run `dotnet test tests/Wallet.Tests`; commit.

**Exit:** BET/WIN work through one documented endpoint with atomic, authorized, retry-safe monetary effects across API instances.

## Milestone 6 — Complete balances, history, and API usability

**Files:** Wallet Application balance/history queries; Infrastructure query implementations; API balance/history endpoints and Problem Details mapping; `tests/Wallet.Tests/Integration/ReadApiTests.cs`; both APIs' OpenAPI configuration.

- [ ] Implement player `/me` queries and separate admin queries. Return the documented pending conflict for unprovisioned players, never invented zero balances.
- [ ] Add cursor pagination ordered by UTC creation time and transaction ID, default 20 and maximum 100. Validate cursor/filter/player combinations and preserve exact decimal strings in responses.
- [ ] Test cross-player isolation, admin access, malformed cursors, tied timestamps, empty/end pages, and appending transactions between page reads.
- [ ] Document request examples, money precision, authentication/permissions, versioning, response codes, idempotent retries, and pending provisioning in both interactive APIs.
- [ ] Run `dotnet test tests/Wallet.Tests --filter ReadApi`; commit.

**Exit:** Both actors can explore the required API flows, and history remains bounded and isolated by player.

## Milestone 7 — Prove resilience and independent scaling

**Files:** Existing milestone 3–6 integration/acceptance tests and fixtures, Compose test wiring, and operational notes in `README.md`. Add a new test only for a guarantee not already covered.

- [ ] Rerun the existing profile/provisioning outage and duplicate-delivery scenarios. Include an outage lasting beyond the initial retry interval and confirm automatic recovery without operator intervention.
- [ ] Verify database-unavailable retry behaviour and reuse the lost-response/idempotency scenarios from milestone 5. If a guarantee has no test yet, add one focused test to its owning service suite.
- [ ] Run the existing two-host concurrency and competing-worker scenarios against the shared real-dependency fixture. Confirm API replicas do not run competing migrations and worker scaling does not require matching API replica counts.
- [ ] Rerun the Users-to-Wallet additive identity-linking scenario from milestone 4. Existing profile and wallet ownership must remain unchanged, with no second live IdP required.
- [ ] Run `dotnet test MiniWalletPlatform.sln`; record commands and actual results. Fix failures, then commit the verified scenarios.

**Exit:** Tests provide evidence for the distributed-system guarantees rather than relying on in-memory mocks or process-local locks.

## Milestone 8 — Package the demo and explain the choices

**Files:** `README.md`, `deploy/seed/`, container build files, `.env.example` if needed, and final design/roadmap updates.

- [ ] Verify the migrations introduced with each persistence feature and the development-only seed from milestone 5. Rerunning startup cannot grant funds again; new profiles remain zero.
- [ ] From an isolated fresh Compose project/volumes, verify `docker compose up --build` without manual database edits. Use existing Docker/Compose as prerequisites; do not delete the user's existing volumes to test a clean start.
- [ ] Follow README-only instructions: sign in as player/admin through the IdP, inspect initial balances, place BET, record WIN, view history, and create a new verified player with zero wallets.
- [ ] Document local credentials/URLs, architecture, repository and transaction boundaries, identity replacement, all assumptions, tests, optional omissions, failure/replay operations, and exact validation results.
- [ ] Prepare a short interview walkthrough: one BET transaction, one profile-to-wallet event, why duplicate delivery is safe, how a future transaction type is added, and how identity migration preserves ownership.
- [ ] Review the diff for secrets and temporary artifacts, run the final build/test suite after any fixes, and commit the delivery documentation. Publish/submit only when separately requested.

**Exit:** Another developer can run and inspect the required system with the README, and the candidate can defend the design.

## Deferred scope

SignalR, a frontend, refunds/bonuses/jackpots, multiple payouts per BET, public funding/account-linking APIs, an operator migration CLI/UI, a second live IdP, exhaustive chaos/replica test matrices, an API gateway, Kubernetes, and elaborate shared frameworks. Reconsider extras only after all required acceptance checks pass within the timebox.

## Definition of done

- [ ] All required functionality and access rules demonstrated through the documented APIs.
- [ ] Full Clean Architecture enforced and consistent across both services.
- [ ] New profiles always provision three zero wallets without an extra creation call.
- [ ] Correct balances across instances, durable accepted work, idempotent request/event retries, and independent worker scaling verified.
- [ ] Real PKCE sign-in works in both API docs; credentials remain with the IdP.
- [ ] One-command startup, test player/admin, meaningful tests, readable commits, and an accurate README delivered.
- [ ] Assumptions, actual test outcomes, and any unmet requirements are clearly disclosed.
