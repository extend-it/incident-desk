# Incident Desk

A deliberately tiny app that exists to be a **system under test** for practising
Playwright end-to-end tests in C#. It is not a real product: no database, no
authentication, data lives in memory and is lost when the API stops.

- `api/IncidentDesk.Api` — ASP.NET Core minimal API (.NET 10) on http://localhost:5080
- `web/` — React + TypeScript page (Vite) on http://localhost:5173

The web dev server proxies `/api/*` to the API, so both must be running.

## Run it

Terminal 1 — API:

```powershell
dotnet run --project api/IncidentDesk.Api
```

Terminal 2 — web:

```powershell
cd web
npm install
npm run dev
```

Open http://localhost:5173 (tenant `contoso`) or
http://localhost:5173/?tenant=fabrikam for another tenant.

## API

| Method | Path | Result |
|---|---|---|
| GET | `/health` | `200 {"status":"healthy"}` |
| GET | `/api/incidents?tenantId=contoso` | `200` list for the tenant, newest first; `400` if `tenantId` is missing |
| POST | `/api/incidents` | `201` with the created incident; `400` problem details if invalid |

POST body: `tenantId`, `title`, `severity` — all required; `severity` must be
exactly one of `Low`, `Medium`, `High`, `Critical` (case-sensitive).

Create an incident for `contoso`:

```powershell
Invoke-RestMethod -Method Post -Uri http://localhost:5080/api/incidents `
  -ContentType 'application/json' `
  -Body '{"tenantId":"contoso","title":"Checkout page is down","severity":"High"}'
```

List them:

```powershell
Invoke-RestMethod 'http://localhost:5080/api/incidents?tenantId=contoso'
```

## UI states (useful for test assertions)

| State | What appears |
|---|---|
| Loading | `role="status"`: "Loading incidents…" |
| HTTP 403 | `role="alert"`: "You don't have access to this tenant's incidents." |
| Other error | `role="alert"`: "Couldn't load incidents (HTTP {status})", or "Couldn't load incidents" on network failure |
| Empty | paragraph "No incidents for {tenant}." |
| Data | `<ul aria-label="Incidents">` with one `<li>` per incident |

The API never returns 403 or 5xx by itself; those states are meant to be
reached by intercepting `/api/incidents` in Playwright (`page.RouteAsync`).
