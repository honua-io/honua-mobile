import { resolve } from 'node:path';
import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    lib: {
      entry: resolve(__dirname, 'src/index.ts'),
      formats: ['es'],
      fileName: () => 'index.js',
    },
    rollupOptions: {
      external: [
        'cesium',
        'maplibre-gl',
        /^@deck\.gl\//,
      ],
      output: {
        assetFileNames: '[name][extname]',
        chunkFileNames: '[name]-[hash].js',
        entryFileNames: 'index.js',
      },
    },
    sourcemap: false,
    target: 'es2022',
  },
});
