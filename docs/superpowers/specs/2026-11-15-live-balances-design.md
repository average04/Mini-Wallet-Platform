# Live balance updates

Implement the brief's optional SignalR feature: a connected player receives updates after BET, WIN or BONUS changes their balance. The existing JWT, stable player identity, money rules and API/worker separation remain authoritative.

## Approach

Use a hub in Wallet.Api and Microsoft's Redis backplane. A single-process hub cannot reach connections on other API instances. A custom RabbitMQ broadcast transport would reuse the existing broker but adds transport plumbing that the supported Redis adapter already provides. Redis is notification infrastructure, not a balance store.

These implementation choices follow the request to add SignalR to the existing platform; they are not additional requirements from the briefing.

## Boundaries and contract

- `/hubs/balances` requires the existing JWT. Browser WebSocket/SSE query tokens are accepted only at this exact hub path. Connections close on token expiry.
- Resolve the authenticated issuer/subject to the existing platform PlayerId. Join only a server-selected player group; expose no client method to choose another player or send updates. Admins receive updates only for their own provisioned identity, not every player's data.
- After joining, send `Ready`. Send `BalanceChanged` with `transactionId`, `currency`, `balanceAfter` (decimal string) and `createdAt` after a successful committed transaction.
- Application returns replay metadata alongside its existing outcome; stored receipts and HTTP transaction responses stay compatible. The API dispatches the notification after the use case returns. Domain/Application have no SignalR or Redis dependency.
- Idempotent replays and rejected/rolled-back transactions produce no new notification. Notification failures are logged and bounded; they never turn a committed transaction into an HTTP failure.
- Events can be missed, duplicated or delivered out of order. They are hints to reload `GET /api/v1/wallets/me`, not an ordered accounting feed. Reload on Ready/reconnect and periodically while connected. PostgreSQL remains authoritative; no durable notification outbox is added.
- Each deployed API instance uses the same Redis endpoint and channel prefix. Local single-instance development/tests can omit Redis; Docker Compose always configures it. Load balancers require affinity for fallback transports; the sample uses WebSockets with negotiation skipped.

## Manual testing

Provide a small `/live-balances` page served by Wallet.Api, with a JWT field, Connect/Disconnect controls, balances and an event log. Use the official pinned SignalR JavaScript client served locally. Keep tokens in memory only; accept tokens already obtained via PKCE or local Postman testing. This is a testing aid, not a new login flow or full frontend.

## Verification

Exercise real PostgreSQL and SignalR connections, including two API hosts sharing Redis. Check player isolation, BET/WIN/BONUS delivery, replay/rejection suppression, expiry/invalid tokens, query-token confinement, and notification outage safety. Run the full existing suite, build Compose, and check the manual page without altering the user's wallet data. Commit design, backend and testing/documentation as separate stages.
