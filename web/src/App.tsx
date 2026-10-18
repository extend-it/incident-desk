import { useEffect, useState } from 'react'

// Shape of one incident as returned by GET /api/incidents (camelCase JSON).
type Incident = {
  id: string
  tenantId: string
  title: string
  severity: string
  createdAt: string
}

// The page is always in exactly one of these states.
type LoadState =
  | { kind: 'loading' }
  | { kind: 'forbidden' }
  | { kind: 'error'; status?: number } // status is undefined for network failures
  | { kind: 'loaded'; incidents: Incident[] }

// The tenant comes from the URL, e.g. http://localhost:5173/?tenant=fabrikam
function getTenantFromUrl(): string {
  return new URLSearchParams(window.location.search).get('tenant') || 'contoso'
}

export default function App() {
  const [tenant] = useState(getTenantFromUrl)
  const [state, setState] = useState<LoadState>({ kind: 'loading' })

  // Runs after the first render: fetches the tenant's incidents and moves
  // the page from "loading" to one of the other states.
  useEffect(() => {
    // Lets us cancel the request if the component goes away (React's dev
    // StrictMode mounts components twice, so this avoids a duplicate update).
    const controller = new AbortController()

    async function load() {
      try {
        const response = await fetch(
          `/api/incidents?tenantId=${encodeURIComponent(tenant)}`,
          { signal: controller.signal },
        )

        if (response.status === 403) {
          setState({ kind: 'forbidden' })
        } else if (!response.ok) {
          setState({ kind: 'error', status: response.status })
        } else {
          const incidents: Incident[] = await response.json()
          setState({ kind: 'loaded', incidents })
        }
      } catch (error) {
        if (controller.signal.aborted) return // cancelled on purpose, not an error
        console.error(error)
        setState({ kind: 'error' }) // network failure: no HTTP status
      }
    }

    load()
    return () => controller.abort()
  }, [tenant])

  return (
    <main>
      <h1>Incidents — {tenant}</h1>

      {/* State: request in flight */}
      {state.kind === 'loading' && <p role="status">Loading incidents…</p>}

      {/* State: API answered 403 Forbidden */}
      {state.kind === 'forbidden' && (
        <p role="alert">You don't have access to this tenant's incidents.</p>
      )}

      {/* State: any other non-2xx status, or the request failed entirely */}
      {state.kind === 'error' && (
        <p role="alert">
          {state.status !== undefined
            ? `Couldn't load incidents (HTTP ${state.status})`
            : "Couldn't load incidents"}
        </p>
      )}

      {/* State: success, but the tenant has no incidents */}
      {state.kind === 'loaded' && state.incidents.length === 0 && (
        <p>No incidents for {tenant}.</p>
      )}

      {/* State: success with incidents (the API already sorts newest first) */}
      {state.kind === 'loaded' && state.incidents.length > 0 && (
        <ul aria-label="Incidents">
          {state.incidents.map((incident) => (
            <li key={incident.id}>
              <span className="title">{incident.title}</span>
              <span className="severity">{incident.severity}</span>
              <time dateTime={incident.createdAt}>
                {new Date(incident.createdAt).toLocaleString()}
              </time>
            </li>
          ))}
        </ul>
      )}
    </main>
  )
}
