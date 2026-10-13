import react from '@vitejs/plugin-react'
import tailwindcss from '@tailwindcss/vite'
import { existsSync, readFileSync } from 'node:fs'
import path from 'node:path'
import { defineConfig } from 'vite'

function localHttps() {
  const cert = path.resolve(import.meta.dirname, '.certs/localhost.pem')
  const key = path.resolve(import.meta.dirname, '.certs/localhost.key')
  if (!existsSync(cert) || !existsSync(key)) {
    throw new Error('Local HTTPS certificate is missing. Follow the certificate export steps in docs/local-development.md.')
  }
  return { cert: readFileSync(cert), key: readFileSync(key) }
}

export default defineConfig(({ command }) => ({
  plugins: [react(), tailwindcss()],
  server: {
    host: 'localhost',
    port: 5173,
    strictPort: true,
    https: command === 'serve' ? localHttps() : undefined,
  },
  resolve: {
    alias: {
      '@': path.resolve(import.meta.dirname, './src'),
    },
  },
}))
