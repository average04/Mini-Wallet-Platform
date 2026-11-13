# Mini Wallet Platform

Two independently hosted .NET services: Users owns player profiles; Wallet owns balances and successful transaction history. Both follow Domain → Application ← Infrastructure, with separate API and Worker composition roots.

Implementation is in progress. See [design](docs/superpowers/specs/2026-11-13-mini-wallet-platform-design.md) and [roadmap](docs/superpowers/plans/2026-11-13-mini-wallet-platform-roadmap.md).

## Prerequisites

- Docker Engine / Docker Desktop with Linux containers running (also required by Testcontainers tests).
- .NET SDK 10.0.404 for local build/test. The Compose build uses the pinned SDK image.

## Development

```sh
dotnet build MiniWalletPlatform.sln
dotnet test MiniWalletPlatform.sln
docker compose up --build
```

Full sign-in and demo instructions will accompany the working hosts. Credentials in local development configuration are disposable examples, never production secrets.

