/// <reference types="vitest" />
import { defineConfig } from 'vite'
import react from '@vitejs/plugin-react'
import { VitePWA } from 'vite-plugin-pwa'

export default defineConfig({
  plugins: [
    react(),
    VitePWA({
      registerType: 'prompt',
      includeAssets: ['favicon.svg'],
      manifest: {
        name: 'Employee Self-Service',
        short_name: 'ESS',
        description: 'Employee Self-Service portal',
        theme_color: '#0b5394',
        background_color: '#ffffff',
        display: 'standalone',
        start_url: '/',
        icons: [{ src: 'favicon.svg', sizes: 'any', type: 'image/svg+xml', purpose: 'any maskable' }],
      },
      workbox: {
        // Never cache API responses: payroll and personal data must not persist in the browser cache.
        navigateFallbackDenylist: [/^\/api/, /^\/hubs/, /^\/health/],
        runtimeCaching: [],
      },
    }),
  ],
  server: {
    port: 5173,
    proxy: {
      '/api': { target: 'http://localhost:5080', changeOrigin: true },
      '/hubs': { target: 'http://localhost:5080', ws: true, changeOrigin: true },
    },
  },
  test: { environment: 'jsdom', globals: true, setupFiles: './src/setupTests.ts' },
})
