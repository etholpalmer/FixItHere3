# FixItHere.Demo

Proof-of-concept for the FixItHere mobile-services marketplace.
See [`docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md`](docs/superpowers/specs/2026-07-17-fixithere-demo-prototype-design.md).

## Run the backend + demo control panel

```bash
dotnet run --project src/Backend.Api
```

Then open <http://localhost:5000/dev> — press **Start Demo** to watch the full
book → accept → travel → chat → arrive → work → pay → rate flow, live on the map.

Every startup resets the database to identical seed data
(7 services, 20 customers, 20 providers, 80 jobs, ratings, messages).

## Test

```bash
dotnet test
```

## Projects

- `src/Shared` — pure F# domain: types, DTOs, Job state machine
- `src/Backend.Api` — F# Minimal API + EF Core/SQLite + SignalR + `/dev` console
- `src/Customer.Mobile`, `src/Provider.Mobile` — Fabulous MAUI apps (Plans 2–3, not yet built)

## Notes

- **.NET 10** SDK (the design doc says net8.0; net10 is what the toolchain here provides — same code).
- Backend listens on `http://localhost:5000` by default. `WebApplication.CreateBuilder()`
  does not read CLI `--urls`; set `ASPNETCORE_URLS` to change the port.
- Dev endpoints (`/dev`, `/dev/reset`, `/dev/demo/start`) are mapped in the
  **Development** environment only.
