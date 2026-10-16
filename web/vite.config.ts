/// <reference types="vitest/config" />
import react from '@vitejs/plugin-react'
import { defineConfig, loadEnv } from 'vite'

// https://vite.dev/config/
export default defineConfig(({ mode }) => {
  // Empty prefix: read every variable from .env* / the shell, including ones WITHOUT the VITE_ prefix.
  // API_PROXY_TARGET is only used by the dev server here and is never exposed to the client bundle.
  const env = loadEnv(mode, process.cwd(), '')

  return {
    plugins: [react()],
    server: {
      proxy: {
        // Default: the compose API (HTTP only, so UseHttpsRedirection never redirects).
        // Pointing this at `dotnet run` (:5013) gets a 307 to https://localhost:7187 — a different origin.
        '/api': env.API_PROXY_TARGET || 'http://localhost:8080',
      },
    },
    test: {
      environment: 'jsdom',
      setupFiles: ['./src/test/setup.ts'],
      restoreMocks: true,
    },
  }
})
