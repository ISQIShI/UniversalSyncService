import { defineConfig, loadEnv } from 'vite';
import react from '@vitejs/plugin-react';

export default defineConfig(({ mode }) => {
  const env = loadEnv(mode, process.cwd(), '');
  const proxyTarget = env.VITE_DEV_PROXY_TARGET || 'https://localhost:7188';

  return {
    plugins: [react()],
    build: {
      outDir: '../UniversalSyncService.Host/wwwroot',
      emptyOutDir: true,
    },
    server: {
      port: Number(env.VITE_DEV_SERVER_PORT || 5173),
      proxy: {
        '/api': {
          target: proxyTarget,
          changeOrigin: true,
          secure: false,
        },
        '/health': {
          target: proxyTarget,
          changeOrigin: true,
          secure: false,
        }
      }
    }
  };
});
