import { rename, rm } from 'node:fs/promises';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';
import { build } from 'vite';

const packageRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const angularDist = join(packageRoot, 'dist', 'angular');
const entryPath = join(angularDist, 'angular.js');
const bundleDist = join(angularDist, '.bundle');
const bundlePath = join(bundleDist, 'angular.js');

await rm(bundleDist, { recursive: true, force: true });

await build({
  configFile: false,
  logLevel: 'silent',
  build: {
    emptyOutDir: true,
    lib: {
      entry: entryPath,
      formats: ['es'],
      fileName: () => 'angular.js',
    },
    minify: false,
    outDir: bundleDist,
    rollupOptions: {
      external: (id) => id.startsWith('@angular/'),
    },
    sourcemap: false,
    target: 'es2022',
  },
});

await rename(bundlePath, entryPath);
await rm(bundleDist, { recursive: true, force: true });
