# WIN payouts

WIN accepts `betId`, optional `multiplier` (default `"2"`) and optional
`bonusAmount` (default `"0"`). Both inputs are decimal strings.

The total credit is **original BET amount × multiplier + bonusAmount**.
The multiplier includes the stake; the bonus is added afterward in the original
bet's currency. A $10 bet at 2× with a $5 bonus credits $25. WIN no longer accepts
an input `amount`; its response `amount` remains the total credited payout.
The referenced BET determines the player, currency and stake.

The multiplier must be positive, use ordinary decimal notation and have at most
8 fractional digits. The bonus must be nonnegative and obey the currency scale
(USD/EUR: 2, BTC: 8). Inputs are at most 64 characters. Omitted or null optional
values use their defaults. Reject unrepresentable payouts instead of rounding;
malformed inputs and payouts beyond the supported money range return HTTP 400.
BET and BONUS retain their existing amount-based requests and reject WIN fields.

Keep admin authorization, atomic wallet/ledger/receipt commits, one WIN per BET,
and notifications only after fresh successful commits. Normalize numeric inputs
for retries: omitted defaults and explicit defaults are equivalent; changing
multiplier or bonus with the same key conflicts, even if the total is unchanged.
Validation failures must not reserve an idempotency key.

Calculate payouts in the domain with exact integer arithmetic. Transaction
handlers resolve validated money before the shared execution pipeline starts
the database transaction. Store the final payout in the existing ledger; no
database migration is required. Update Swagger, manual instructions and Postman.
