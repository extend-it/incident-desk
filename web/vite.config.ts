import react from '@vitejs/plugin-react'
import { defineConfig } from 'vite'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react()],
  server: {
    port: 5173,
    strictPort: true,
    // The page calls relative URLs like /api/incidents; Vite forwards them to
    // the ASP.NET Core API, so the browser sees one origin and no CORS is needed.
    proxy: {
      '/api': 'http://localhost:5080',
    },
  },
})
