# Incident Desk
[![tests](https://github.com/extend-it/incident-desk/actions/workflows/tests.yml/badge.svg?branch=main)](https://github.com/extend-it/incident-desk/actions/workflows/tests.yml)

A deliberately tiny app that exists to be a **system under test** for practising
Playwright end-to-end tests in C#. It is not a real product: no database, no
authentication, data lives in memory and is lost when the API stops.

- `api/IncidentDesk.Api` — ASP.NET Core minimal API (.NET 10) on http://localhost:5080
- `web/` — React + TypeScript page (Vite) on http://localhost:5173

The web dev server proxies `/api/*` to the API, so both must be running.

## Run it

First, start the local Azure Service Bus emulator (needs Docker). The API
listens on its `incidents-in` queue, so start the emulator **before** the API:

```powershell
docker compose -f docker/docker-compose.yaml up -d
```

The emulator's queues and topics are defined in `docker/Config.json`. If the
emulator isn't running, the API logs Service Bus errors but its HTTP
endpoints keep working.

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

Every incident has a `status` of `Processing` or `Ready`. Incidents created
with POST are `Ready` immediately.

## Queue: `incidents-in` (eventual consistency)

Incidents can also arrive as Service Bus messages on the `incidents-in` queue,
with the same JSON body as POST (property names are case-insensitive). A
background worker in the API handles each message:

1. Invalid message (same rules as POST, or not valid JSON): dead-lettered with
   reason `ValidationFailed` and the errors in the description.
2. Valid message: the incident is stored with status `Processing`, so it shows
   up in GET straight away.
3. After `Enrichment:DelaySeconds` (appsettings.json, default 4) the incident
   becomes `Ready` and the message is completed.

Settings in `api/IncidentDesk.Api/appsettings.json`:

| Key | Default |
|---|---|
| `ServiceBus:ConnectionString` | local emulator |
| `Enrichment:DelaySeconds` | `4` |

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
| Data | `<ul aria-label="Incidents">` with one `<li>` per incident; each has `<span class="status">` with "Processing…" or "Ready" |

The page polls `GET /api/incidents` every second. Only the first load shows
the loading state; later polls update the list in place. A poll that fails
replaces the list with the matching error state until a later poll succeeds.

The API never returns 403 or 5xx by itself; those states are meant to be
reached by intercepting `/api/incidents` in Playwright (`page.RouteAsync`).

## Tests

`tests/IncidentDesk.Tests` holds 11 NUnit tests written with Playwright for .NET. They need the
emulator, the API and the web app running (see *Run it*), then:

```powershell
dotnet test tests/IncidentDesk.Tests
```

Every test creates its own uniquely titled incidents, so tests don't depend on each other or on
data left from earlier runs.

### UI driven from the API: `IncidentListTests`

Arrange through the API, assert through the page.

| Test | What it checks |
|---|---|
| `IncidentCreatedThroughTheApiIsListedOnThePage` | An incident created with POST appears in the list with its severity |
| `EachTenantSeesItsOwnIncident` | Each tenant's page lists that tenant's incident |
| `IncidentOfAnotherTenantIsNotListed` | Another tenant's incident is absent; the test first waits for its own incident, so "absent" isn't checked before the list has loaded |

### Eventual consistency through the queue: `IncidentFromQueueTests`

| Test | What it checks |
|---|---|
| `IncidentSentToTheQueueAppearsAsProcessingThenReady` | The page is opened first; a message is then sent to `incidents-in`, and the row appears as "Processing…" and later "Ready" without a reload |

Opening the page *before* sending the message matters: the row can then only appear through
polling. In an earlier version that sent the message first, the page's first load could already
see the incident, so the test didn't prove that polling works. The test waits with explicit
`Expect` timeouts (10 s, then 15 s), never with sleeps.

### UI states with a mocked API: `IncidentListStateTests`

`Page.RouteAsync` answers `/api/incidents` inside the browser, so these tests need only the web
app, not the API.

| Test | Mocked response | Expected UI |
|---|---|---|
| `EmptyListForStatus200ShowsTheNoIncidentsMessage` | 200 with `[]` | "No incidents for contoso.", no alert |
| `ServerError500ShowsAnErrorInsteadOfAnEmptyList` | 500 | Alert "Couldn't load incidents (HTTP 500)", no empty-list message |
| `ServerError503ShowsAnErrorInsteadOfAnEmptyList` | 503 | Alert "Couldn't load incidents (HTTP 503)" |
| `UnauthorizedAccessError403ShowsAnErrorInsteadOfAnEmptyList` | 403 | Alert "You don't have access to this tenant's incidents." |
| `NetworkFailureShowsAnErrorWithoutStatus` | Request aborted | Alert "Couldn't load incidents", without a status |
| `SlowResponseShowsLoadingFirstThenTheList` | 200 after 2 s | "Loading incidents…" first, then the incident, then the loading message gone |

### API only: `IncidentApiTests`

| Test | What it checks |
|---|---|
| `IncidentWithUnknownSeverityIsRejected` | POST with severity `Urgent` returns 400 |

### Mutation checks

Each check below was done by hand: the code was broken on purpose, the expected test failed
for the expected reason, and the code was reverted. Line numbers refer to the files as committed
with this table; if a file changes, find the line by the quoted code.

| Mutation | File | How to produce it (line · action · code) | Test that failed |
|---|---|---|---|
| GET ignores `tenantId` | `api/IncidentDesk.Api/Program.cs` | **Line 112 · Change value:** `.Where(i => i.TenantId == tenantId)` → `.Where(i => true)`<br>(Alternative: **Line 112 · Comment out**, then **Line 115 · Uncomment** the ready-made `.Where(i => true)`)<br>**Then · Restart the API** (Ctrl+C, `dotnet run`) | `IncidentOfAnotherTenantIsNotListed` (count 1, expected 0); `EachTenantSeesItsOwnIncident` still passed, because presence checks can't catch a leak |
| Severity not rendered | `web/src/App.tsx` | **Line 110 · Comment out** (JSX syntax): `<span className="severity">{incident.severity}</span>` → `{/* <span className="severity">{incident.severity}</span> */}`<br>**Then · Save** (Vite reloads the page by itself) | `IncidentCreatedThroughTheApiIsListedOnThePage` and `EachTenantSeesItsOwnIncident` |
| Errors shown as an empty list | `web/src/App.tsx` | **Line 55 · Comment out:** `setState({ kind: 'error', status: response.status })`<br>**Line 56 · Uncomment:** `setState({ kind: 'loaded', incidents: [] })`<br>**Then · Save** | `ServerError500ShowsAnErrorInsteadOfAnEmptyList`; `NetworkFailureShowsAnErrorWithoutStatus` still passed (different code path) |
| Polling switched off | `web/src/App.tsx` | **Line 71 · Comment out:** `const timer = setInterval(load, POLL_INTERVAL_MS)`<br>**Line 73 · Comment out:** `clearInterval(timer)` (it would no longer compile without line 71)<br>**Then · Save** | `IncidentSentToTheQueueAppearsAsProcessingThenReady`, at "Processing" |
| Worker never sets `Ready` | `api/IncidentDesk.Api/IncidentQueueWorker.cs` | **Line 113 · Comment out:** `store.Update(incident with { Status = IncidentStatus.Ready });`<br>**Then · Restart the API** | `IncidentSentToTheQueueAppearsAsProcessingThenReady`, at "Ready" |

**To revert a mutation,** do the opposite action on the same lines (uncomment what you commented out, restore the original value), restart or save the same way, and run the tests again: all must pass.
