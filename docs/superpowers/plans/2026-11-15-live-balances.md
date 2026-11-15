# Live Balances Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans to implement this plan task-by-task. Track checks below and in the ignored task ledger.

**Goal:** Deliver authenticated live balance notifications and a usable manual test page.

**Architecture:** Wallet.Api owns SignalR and its Redis adapter. Application exposes whether a committed transaction was replayed; notification dispatch happens after it returns. PostgreSQL stays authoritative.

**Tech Stack:** .NET 10, ASP.NET Core SignalR, StackExchange.Redis, xUnit, Testcontainers, Docker Compose, official SignalR JavaScript client.

**Spec:** `docs/superpowers/specs/2026-11-15-live-balances-design.md`

## Global constraints

- No SignalR/Redis dependency in Domain or Application; no password login endpoints.
- Existing receipt JSON and transaction HTTP response shapes remain compatible.
- Only the affected player receives events, and only after commit.
- Replays/rejections produce no event; notification outages cannot fail money operations.
- Preserve local wallet data and unrelated IDE files. Use the current checkout on `codex/signalr-balance-updates`.

## Review focus

- Browser query tokens must not authenticate REST requests or similarly prefixed paths.
- The first initial balance read must happen after server-side group enrollment.
- Admin-created credits must target the player's group, not the admin's identity.
- Token expiry and unprovisioned identities must not leave an authorized live feed open.
- Reconnect/out-of-order notifications must not overwrite a newer displayed balance.

## Task 1: Backend delivery and scale-out

Files: `Wallet.Application/Features/Transactions/ExecuteTransaction.cs`, `TransactionModels.cs`; `Wallet.Api/Realtime/*`, `Program.cs`, `Auth/AuthenticationSetup.cs`, `OpenApiSetup.cs`; central/package references; `compose.yaml`; `tests/Wallet.Tests/Integration/BalanceHubTests.cs`, `WalletApiFactory.cs`.

Interfaces: `TransactionExecutionResult(TransactionOutcome Outcome, bool IsReplay)`; `BalanceHub` at `/hubs/balances`; `BalanceUpdateNotifier.PublishAsync(TransactionView transaction)`; `Ready` and `BalanceChanged` client events.

- [ ] Write integration tests for authenticated hub negotiation, real client delivery to only the owner, credits to a player, replay/rejection suppression and query-token confinement. Observe failure because the hub does not exist.
- [ ] Return execution metadata without changing persisted outcomes. Implement authenticated group enrollment, bounded best-effort post-commit dispatch and Redis configuration.
- [ ] Add real two-host Redis delivery and Redis-outage transaction-safety tests; test unprovisioned identities and expiring/invalid tokens. Keep credentials out of logs.
- [ ] Run `dotnet test tests/Wallet.Tests/Wallet.Tests.csproj`; require all tests passing. Commit backend with its tests and Compose configuration.

## Task 2: Manual client and documentation

Files: `Wallet.Api/wwwroot/live-balances/*`, pinned local client/license; `Program.cs`; `README.md`, `docs/technical-guide.md`, `postman/README.md`; client behavior tests where synchronization needs verification.

Interfaces: uses Task 1's hub and existing authenticated balances endpoint. Wait for `Ready`, serialize/coalesce balance reloads, re-read after reconnect and every 30 seconds. Disconnect on unrecoverable/expired-token error; never persist the token.

- [ ] Add the testing page and pinned official client. Verify event-driven reload and reconnect behavior, including responses arriving while a newer update is pending.
- [ ] Update run/manual-testing instructions and remove SignalR from the current deferred scope. Retain the historical design as history.
- [ ] Run the full solution tests and `docker compose config --quiet`; build/restart only affected local services without resetting data. Inspect the page and live authenticated connection.
- [ ] Commit the testing page and documentation. Review the complete diff and resolve correctness/security blockers before reporting completion.
