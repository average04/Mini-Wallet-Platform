# WIN Payouts Implementation Plan

**Goal:** Settle WIN from the original stake with a default 2× multiplier and optional additive bonus.

**Architecture:** Domain payout calculation uses exact integer arithmetic. Handlers resolve money and normalized settlement terms before the shared atomic transaction pipeline; storage and notification boundaries stay unchanged.

**Tech Stack:** .NET 10, FluentValidation, xUnit, PostgreSQL Testcontainers.

**Spec:** `docs/superpowers/specs/2026-11-16-win-payouts-design.md`

## Global constraints

- Default multiplier `"2"`, default bonus `"0"`; payout = stake × multiplier + bonus.
- Decimal strings only; multiplier positive, bonus nonnegative; no rounding.
- Currency scale USD/EUR 2, BTC 8; multiplier scale at most 8; input length at most 64.
- Admin-only WIN; one settlement per BET; retries never apply money twice.
- No schema change; preserve existing BET/BONUS receipt fingerprints and response amounts.

## Review focus

- Large products must not silently round; domain boundary tests cover exact arithmetic and range.
- Bonuses must respect the original currency; domain and API tests include BTC and EUR.
- Changed components with identical totals must conflict; integration tests compare normalized terms.
- Invalid inputs must return 400 without reserving a key; API tests retry corrected requests.
- Concurrency and SignalR must use the calculated payout; integration tests cover both.

### Task 1: Domain payout calculation

**Files:** Create `src/Wallet/Wallet.Domain/WinPayout.cs` and `tests/Wallet.Tests/Unit/WinPayoutTests.cs`.
**Interfaces:** `WinPayout.Calculate(Money stake, string? multiplier = null, string? bonusAmount = null)` returns `Total` (Money), normalized `Multiplier` and `BonusAmount` strings.

- [x] Write tests for defaults (10 → 20), 2× plus 5 (10 → 25), fractional multipliers, all currencies, invalid input, precision, overflow and normalized terms.
- [x] Run `dotnet test tests/Wallet.Tests --filter FullyQualifiedName~WinPayoutTests`; observe the missing implementation failure.
- [x] Implement bounded decimal-string parsing and exact scaled integer arithmetic.
- [x] Run the unit tests; expect all passing. Commit the domain change.

### Task 2: Transaction API and settlement guarantees

**Files:** Modify transaction models, validator, handlers and executor under `src/Wallet/Wallet.Application/Features/Transactions/`; `src/Wallet/Wallet.Api/ApiErrors.cs`; transaction and balance hub integration tests.
**Interfaces:** Extend `TransactionRequest` with optional `Multiplier` and `BonusAmount`, make `Amount` optional. `TransactionTarget` carries resolved Money and optional WinPayout. WIN uses Task 1's calculator. Fingerprints include normalized WIN terms.

- [x] Write API tests for default/explicit payouts, bonuses, invalid fields and reuse after 400, equivalent retries and changed terms, authorization, concurrent settlement and actual event amounts.
- [x] Run the transaction tests; observe failures against the old request contract.
- [x] Implement handler resolution, conditional validation, 400 mapping and normalized fingerprint inputs; preserve BET/BONUS fingerprints.
- [x] Run Wallet tests; expect all passing. Commit API behavior and tests.

### Task 3: Manual testing and final verification

**Files:** Swagger examples and endpoint description; `README.md`, `docs/technical-guide.md`, `postman/README.md`, Postman collection and relevant assertions.
**Interfaces:** WIN examples send `multiplier`/`bonusAmount`; responses retain total `amount`.

- [x] Update manual examples to the default 2× payout and include an additive bonus example.
- [x] Check Postman JSON, token scripts and examples; run full .NET tests and live-page client tests. Expect all passing.
- [x] Review the whole branch, address substantive findings, and rebuild the local Wallet API for manual testing without changing demo balances.
- [x] Commit documentation and examples; report branch, validation and new request format.

## Verification

Completed 2026-11-16. Full solution: 182 tests passed (152 Wallet, 30 Users).
Live-page JavaScript: 4 passed; Postman token checks: 14 passed; WIN collection
checks: 9 passed. Docker Wallet API rebuilt and restarted; live Swagger verified
both 2x/default-zero and 2x/bonus-five examples and the new request schema.
Independent read-only review of `950fc06..3591ed6` found no issues.
