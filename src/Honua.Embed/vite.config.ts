import { resolve } from 'node:path';
import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    lib: {
      entry: {
        index: resolve(__dirname, 'src/index.ts'),
        snippets: resolve(__dirname, 'src/snippets.ts'),
      },
      formats: ['es'],
      fileName: (_format, entryName) => `${entryName}.js`,
    },
    rollupOptions: {
      output: {
        assetFileNames: '[name][extname]',
        chunkFileNames: '[name]-[hash].js',
        entryFileNames: '[name].js',
      },
    },
    sourcemap: false,
    target: 'es2022',
  },
});
