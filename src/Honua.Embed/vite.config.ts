import { resolve } from 'node:path';
import { defineConfig } from 'vite';

export default defineConfig({
  build: {
    lib: {
      entry: {
        index: resolve(__dirname, 'src/index.ts'),
        iframe: resolve(__dirname, 'src/iframe.ts'),
        react: resolve(__dirname, 'src/react.ts'),
        snippets: resolve(__dirname, 'src/snippets.ts'),
        vue: resolve(__dirname, 'src/vue.ts'),
      },
      formats: ['es'],
      fileName: (_format, entryName) => `${entryName}.js`,
    },
    rollupOptions: {
      external: ['react', 'vue'],
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
