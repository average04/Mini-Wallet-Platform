# Mini Wallet Platform Implementation Roadmap

> **For agentic workers:** REQUIRED SUB-SKILL: use superpowers:executing-plans or superpowers:subagent-driven-development when implementation is authorized. This is a milestone roadmap; refine the current milestone into concrete interfaces/tests before coding it. Checkboxes record completed implementation and verification.

**Goal:** Deliver an interview-ready Users/Wallet backend with correct money handling, replaceable identity, durable provisioning, and a runnable demo.

**Architecture:** Full Clean Architecture per service, focused repositories, separate API/Worker hosts, stable PlayerIds, independent databases, Users outbox and Wallet inbox over RabbitMQ.

**Tech Stack:** .NET 10, ASP.NET Core, EF Core/Npgsql, PostgreSQL, RabbitMQ, Keycloak, Mailpit, Swagger UI, FluentValidation, xUnit/Testcontainers, Docker Compose.

**Spec:** [Design baseline](../specs/2026-11-13-mini-wallet-platform-design.md). Claude approved the revised design/roadmap at commit 4200919 in Collidr round 2; its non-blocking implementation notes are folded into the milestones below. Runtime/infrastructure choices remain documented implementation proposals.

**Status:** Implemented 2026-11-13 on codex/implement-mini-wallet. Stable SDK 10.0.404 and runtime 10.0.15 pinned. Automated suite: 89 passed. Fresh isolated Compose startup and real PKCE/signup/zero-to-105/replay browser acceptance passed. Final code review and handoff verification are in progress.

## Global constraints

- C# on .NET 8 or later, ASP.NET Core, a standard validation library, interactive OpenAPI, tests, Git history, and one-command Compose startup.
- Two identically structured services; API and background processing deploy/scale independently.
- All business calls use JWT bearer access tokens. Browsers and both API docs use authorization code with PKCE; the same access token works in both services.
- Credentials, login, signup, and verification remain at the IdP. No password grant or credential proxy.
- New profiles automatically receive zero USD/EUR/BTC wallets; no extra wallet-creation call.
- One transaction endpoint handles player-owned BET and admin-only WIN against an earlier successful BET. Admin-only BONUS is a small added demo-funding operation using that same pipeline.
- Players access only their own resources and cannot credit wallets; admins manage/read profiles and read balances/history for any player.
- No overdraft, duplicate monetary effect, lost committed operation, or lost provisioning during temporary outages.
- Replacing the IdP must preserve business logic and player ownership; live account-migration tooling is not a required deliverable.
- The brief requests 8–10 focused hours, submission one day before the interview, a README with test player/admin, source, and tests. Interview date is unknown.

## Sequence, honest estimate, and stop rule

| Milestone | Working result | Estimate |
| --- | --- | --- |
| 1 | Consistent service template, contracts, minimal container wiring | 0.75–1 h |
| 2 | Wallet BET/WIN/BONUS correctness with isolated test fixtures | 1.75–2.5 h |
| 3 | Real Keycloak/PKCE sign-in and shared-token validation | 1.5–2 h |
| 4 | Profiles, stable identity, and atomic outbox creation | 0.75–1 h |
| 5 | Durable automatic zero-wallet provisioning | 1.25–1.75 h |
| 6 | Balances, history, individual transactions, API polish | 0.5–0.75 h |
| 7 | Reuse failure/concurrency tests for integrated acceptance | 0.5–0.75 h |
| 8 | Fresh-start demo, final verification, README/interview handoff | 1–1.5 h |
| **Total** | **Core estimate; no guarantee of completion in ten hours** | **8–11.25 h** |

The prior fixed ten-hour total was too optimistic. This range still has integration risk; it includes tests in each milestone and is not permission to exceed the brief. Cap focused implementation at ten hours. Write README/run instructions incrementally and reserve the last hour for validation and honest handoff. If required work remains at the cap, explicitly list what is incomplete instead of stretching the estimate or claiming compliance.

Money handling is now before live IdP integration and messaging. Milestone 2 uses real signature/issuer/audience/lifetime validation with an isolated test authority and test-only database fixtures; it does not introduce an unauthenticated runtime API. Milestone 3 connects that boundary to real Keycloak.

Checkpoints:
- **Hour 4:** wallet domain/transaction and two-host debit tests should be proven; live PKCE is the next gate or in progress. If money correctness is still failing, stop optional work immediately.
- **Hour 7:** assess profile-to-wallet provisioning and access rules. Use the cuts below; do not start any optional feature.
- **Hour 9:** feature freeze; validation, README, demonstrated limitations only.
- **Hour 10:** stop and record actual outcomes/unmet requirements.

At the hour-7 checkpoint, account for the tight profile milestone: its association/outbox migrations, race handling and tests may take 1–1.5 hours rather than the estimated 0.75–1 hour. The ten-hour cap and disclosure rule still apply.

Ordered cut list:
1. Live identity-link command/event/consumer, migration UI, and second IdP — already deferred.
2. Poison-message replay automation or a long operational runbook — keep retained dead-letter messages and short diagnostic instructions.
3. Prolonged-outage/many-replica permutations — keep one basic automatic-recovery scenario, duplicate-delivery proof, and the required two-host money test.
4. Separate platform/architecture test projects — already folded into the two service test projects.
5. Custom outbox leases — already replaced by SKIP LOCKED and bounded publisher-confirm transactions.
6. Optional endpoint filters, further profile fields, UI polish, and additional transaction types — defer.
Do not remove Clean Architecture, PKCE, BONUS demo funding, core access rules, precision validation, idempotency, atomic writes, or the minimum tests needed to substantiate them.

## Review focus

1. A token/claim/issuer change must not grant admin permission or change resource ownership (3, 4).
2. Concurrent debits and a response lost after commit must preserve nonnegative balances and one effect per in-scope key (2).
3. Different-key WINs cannot settle the same BET twice; rejected attempts never become WIN-eligible transactions (2).
4. Duplicate profile requests/events and outages must create one profile and three wallets without resetting money (4, 5).
5. Excess precision, overflow, and malformed/cross-player cursors must fail without corruption or disclosure (2, 6).

## Milestone 1 — Establish boundaries and a runnable skeleton

**Files:** MiniWalletPlatform.sln, global.json, Directory.Build.props, Directory.Packages.props, .editorconfig, .gitignore, compose.yaml, deploy/Dockerfiles, src/Contracts, the ten service projects, and tests/{Users.Tests,Wallet.Tests}/Architecture.

- [x] Create the 13-project layout in the design and pin stable compatible versions.
- [x] Define Application ports for current actor, focused repositories, and transaction control; freeze the small /api/v1 contract and money/error conventions.
- [x] Add dependency checks in the existing service test projects. Domain/Application cannot reference Infrastructure/API/Worker or other-service internals.
- [x] Bring up minimal container wiring and database initialization; add each actual migration alongside its feature. Start README prerequisites/commands now, including that Docker must be running for Testcontainers-based dotnet test.
- [x] Run dotnet build MiniWalletPlatform.sln, the architecture tests, and docker compose config; commit.

**Exit:** Consistent enforced boundaries and a usable local foundation; no business rules in host projects.

## Milestone 2 — Prove money handling first

**Files:** Wallet.Domain/{Money,Wallets,Transactions}; Wallet.Application/Features/Transactions/{Execute,Bet,Win,Bonus}; Wallet.Infrastructure/Persistence and Migrations; Wallet.Api/Endpoints/TransactionsEndpoints.cs; tests/Wallet.Tests/{Unit,Integration}.

- [x] Create isolated Testcontainers PostgreSQL fixtures with wallets/identity associations. Use a configured test JWT authority with real bearer validation; these fixtures never seed the runtime application.
- [x] Write domain cases for positive amounts, USD/EUR 2-digit and BTC 8-digit precision, insufficient funds, storage/arithmetic limits, and successful-BET-only WIN eligibility.
- [x] Implement one execution endpoint/pipeline and separate BET/WIN/BONUS handlers. BET owner comes from identity; WIN target/currency comes from the BET; BONUS requires admin permission and an existing target wallet.
- [x] Commit wallet changes, successful history and idempotency results atomically under a row lock. Rejections have no history/money effect. Document per-target-player key scope and authorize before replay.
- [x] Use two WebApplicationFactory hosts over the same real database: two USD 80 BETs on USD 100 produce one success and USD 20 remaining.
- [x] Cover concurrent duplicate keys, changed payload within one scope, allowed key reuse for a different player, a lost response after commit, and a cached insufficient-funds result after later funding.
- [x] Cover different-key duplicate WIN, player WIN/BONUS denial, cross-player BET denial, unauthorized replay, and BONUS retry without duplicate credit.
- [x] Run dotnet test tests/Wallet.Tests; record results and commit.

**Exit:** The highest-risk financial rules are proven before broker/IdP setup can consume the budget.

## Milestone 3 — Connect real authentication and interactive docs

**Files:** deploy/keycloak/realm.json, compose.yaml, each API's auth/OpenAPI setup, provider adapters, and the service test projects' authentication tests.

- [x] Import local player/admin accounts, default player role for signup, explicit mini-wallet-api audience mapping, and trusted admin permission mapping.
- [x] Configure public PKCE S256 clients, exact Swagger redirect URIs/Web Origins, API web-app CORS, disabled password grants, verified email, and Mailpit.
- [x] Add prompt=login to both Swagger OAuth configurations. Document clearing the current Swagger authorization before changing accounts; separate player/admin browser windows are an alternative. Verify an actual player-to-admin switch.
- [x] Implement the public issuer/internal metadata topology from the design, including reachable discovered JWKS. Do not weaken issuer/signature/audience/lifetime checks.
- [x] Test wrong issuer/audience, expired/unsigned/missing tokens, missing admin permission, and authenticated actors without a profile. Assert API authorization against milestone 2's real handlers.
- [x] Sign in through both Swagger UIs and prove the same browser token reaches both APIs. Check real signup/email verification; add these steps to README.
- [x] Run auth tests, document the working URLs/realm settings, and commit.

**Exit:** The actual IdP and browser auth flow work; business layers remain provider-independent.

## Milestone 4 — Profiles and durable player creation

**Files:** Users.Domain/Profiles; Users.Application/Features/Profiles/{Create,Get,Update}; Users.Infrastructure identity/profile/outbox repositories and migrations; Users.Api/Endpoints/ProfilesEndpoints.cs; Contracts/PlayerCreatedV1.cs; tests/Users.Tests.

- [x] Test create/read/update ownership and admin access, verification requirements, display-name validation, and forbidden role/identity mutations.
- [x] Generate platform PlayerIds and unique (issuer, subject) associations; keep the association model capable of multiple verified identities per player.
- [x] In one transaction, create profile, association, and PlayerCreatedV1 outbox record. Concurrent/retried POSTs return the existing profile with 200/Location; they do not update it or publish another event. No profile idempotency-key store.
- [x] Prove rollback on failure and recovery from a lost response. Add one focused identity-resolution test with a second association; document migration rather than building a linking workflow.
- [x] Run dotnet test tests/Users.Tests; update docs and commit.

**Exit:** Profiles work while Wallet is unavailable; one durable event follows one created player.

## Milestone 5 — Automatic wallet provisioning

**Files:** Users.Worker, Users.Infrastructure/Outbox, Wallet.Worker, Wallet.Application/Features/Provisioning, Wallet.Infrastructure inbox/projection repositories and migrations, service Integration/Provisioning tests.

- [x] Both workers idempotently declare the exchange, durable queue/binding and dead-letter topology before use, including reconnects; publisher startup must not depend on Wallet.Worker being running.
- [x] Publish one outbox row using SKIP LOCKED, persistent/durable routing, mandatory publication and a three-second confirm bound. Mark it published only after confirmation with no basic.return; a returned-then-confirmed message fails, rolls back the claim and retries with backoff outside the transaction.
- [x] Consume with prefetch 1/manual acknowledgement. Atomically commit inbox marker, identity projection and three zero USD/EUR/BTC wallets, then acknowledge.
- [x] Retain unacknowledged transient failures and retry with capped delay; avoid immediate nack/requeue loops. Retain malformed/conflicting messages in a dead-letter queue with a short diagnostic note.
- [x] Prove duplicate/restarted delivery does not reset an existing funded wallet. Prove all three wallets appear automatically after a new profile.
- [x] Use one representative outage scenario: start with no broker queues declared and Wallet.Worker stopped, create profiles, restore dependencies, and observe automatic recovery with one provisioning effect. In the same fixture force an unroutable publish, assert its outbox row stays pending, restore routing and prove retry succeeds. Reuse this evidence in milestone 7.
- [x] Run the provisioning tests, update README pending/retry behaviour, and commit.

**Exit:** Durable, independently hosted background processing provisions wallets without a separate wallet-creation call.

## Milestone 6 — Complete reads and API usability

**Files:** Wallet.Application/Features/{Balances,History,GetTransaction}; Infrastructure queries; API endpoints/Problem Details/OpenAPI; tests/Wallet.Tests/Integration/ReadApiTests.cs.

- [x] Add owner/admin balances and history plus GET /api/v1/transactions/{transactionId}; POST successes return its Location. Unauthorized transaction lookup returns 404.
- [x] Add the two-column cursor, default 20/max 100, exact money strings, pending-provisioning 409 with Retry-After: 2, and consistent errors.
- [x] Test ownership/admin access, unknown resources, malformed/cross-player cursors, tied timestamps and appended history.
- [x] Document auth, all three transaction types, per-player idempotency scope, rejected attempts, pagination and the zero-to-funded demo.
- [x] Declare Idempotency-Key as a required transaction POST header in OpenAPI so Swagger Try it out shows the input; document where the profile response exposes PlayerId.
- [x] Run read API tests and commit.

**Exit:** Reviewer can inspect successful transactions and explore every required API flow.

## Milestone 7 — Integrated evidence, without duplicate suites

**Files:** Existing service integration tests/fixtures and README verification section.

- [x] Rerun milestone 2's two-host concurrency/replay tests and milestones 4–5's profile/provisioning duplicate/outage cases.
- [x] Cover any missing basic DB-unavailable path: no false success and safe same-key retry after recovery.
- [x] Inspect separate API/Worker entrypoints and migration ordering; replicas must not share an in-process lock or race migrations.
- [x] Run dotnet test MiniWalletPlatform.sln; record real results and fix failures in required guarantees. Commit.

**Exit:** Minimum evidence exists for the claimed distributed guarantees. Exhaustive permutations remain deferred.

## Milestone 8 — Fresh-start demo and interview handoff

**Files:** README.md, final deployment configuration, and design/roadmap updates.

- [x] From an isolated fresh Compose project/volumes, verify docker compose up --build. Preserve the user's existing volumes.
- [x] Follow the design's real demo: player signs in/creates profile → three zero wallets → admin BONUS USD 100 → player BET USD 10 → admin WIN USD 15 → USD 105 and three successful history entries. Explicitly switch Swagger accounts, copy PlayerId from the profile response and use the visible Idempotency-Key field. Replays preserve that result.
- [x] Exercise fresh signup/verification and confirm that new players get zero wallets. No startup profile/money fixture or password grant is involved.
- [x] Finish README local credentials/URLs, commands/tests, architecture, assumptions, IdP migration procedure, pending/outage behaviour, optional cuts, and actual incomplete requirements.
- [x] Prepare the interview walkthrough: transaction atomicity, outbox/inbox recovery, adding a transaction handler, and replacing identity-provider configuration/mapping.
- [ ] Inspect for secrets/temp artifacts, run final appropriate checks after changes, and commit. Publishing/submitting remains a separate user action.

**Exit:** A reviewer can reproduce the demo and the candidate can explain the trade-offs within the timebox.

## Definition of done

- [x] Required profiles, wallet currencies, BET/WIN, balances/history and all access rules work.
- [x] Full Clean Architecture is consistent and enforced across both services.
- [x] Automatic zero wallets, correct multi-instance balances, durable accepted work, safe retries, and separate workers have meaningful evidence.
- [x] Real PKCE works in both API docs with one token; credentials stay with the IdP.
- [x] One-command startup, working admin-funded demo, source/history, tests and accurate README are delivered.
- [ ] Actual elapsed work, assumptions, cuts, test results, and any unmet requirements are disclosed.
