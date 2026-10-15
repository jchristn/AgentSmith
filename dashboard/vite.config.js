import { defineConfig } from 'vite';
import react from '@vitejs/plugin-react';
import path from 'path';

// Local development proxies the Hugo server so the dev server and the API share one origin.
const apiTarget = process.env.HUGO_API_URL || 'http://127.0.0.1:8800';

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: {
      '@': path.resolve(__dirname, './src'),
      '@components': path.resolve(__dirname, './src/components'),
      '@views': path.resolve(__dirname, './src/views'),
      '@context': path.resolve(__dirname, './src/context'),
      '@utils': path.resolve(__dirname, './src/utils'),
      '@hooks': path.resolve(__dirname, './src/hooks'),
      '@i18n': path.resolve(__dirname, './src/i18n')
    }
  },
  server: {
    port: 3000,
    host: '127.0.0.1',
    proxy: {
      '/v1.0': { target: apiTarget, changeOrigin: true },
      '/mcp': { target: apiTarget, changeOrigin: true },
      '/openapi.json': { target: apiTarget, changeOrigin: true }
    }
  },
  preview: { port: 4173, host: '127.0.0.1' },
  build: {
    outDir: 'dist',
    sourcemap: true,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (/node_modules\/(react|react-dom|react-router|react-router-dom|scheduler)\//.test(id)) return 'vendor';
          if (/node_modules\/(i18next|react-i18next|i18next-browser-languagedetector)\//.test(id)) return 'i18n';
          if (/node_modules\/(marked|dompurify)\//.test(id)) return 'markdown';
          // English is bundled (the fallback); other locales are lazy chunks of their own.
          if (id.endsWith('/src/i18n/locales/en.json')) return 'locale-en';
          if (id.includes('/src/views/')) return 'views';
          return undefined;
        }
      }
    }
  },
  test: {
    environment: 'jsdom',
    include: ['src/**/*.test.{js,jsx}'],
    globals: false
  }
});
