import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { defineConfig } from 'vite'

// The gateway (MS.Principal) address used by the dev proxy.
const GATEWAY = process.env.VITE_GATEWAY_URL ?? 'http://localhost:5059'

// https://vite.dev/config/
export default defineConfig({
  plugins: [react(), tailwindcss()],
  server: {
    // Proxy /api to the gateway so the browser talks same-origin in dev
    // (avoids CORS). SSE streams are forwarded as-is.
    proxy: {
      '/api': {
        target: GATEWAY,
        changeOrigin: true,
      },
    },
  },
})
