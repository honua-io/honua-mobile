import { cpSync, mkdirSync, rmSync } from 'node:fs';
import { dirname, join } from 'node:path';
import { fileURLToPath } from 'node:url';

const packageRoot = dirname(dirname(fileURLToPath(import.meta.url)));
const cesiumRoot = join(packageRoot, 'node_modules', 'cesium', 'Build', 'Cesium');
const targetRoot = join(packageRoot, 'dist', 'cesium');

rmSync(targetRoot, { recursive: true, force: true });
mkdirSync(targetRoot, { recursive: true });

for (const name of ['Assets', 'ThirdParty', 'Workers', 'Widgets']) {
  cpSync(join(cesiumRoot, name), join(targetRoot, name), { recursive: true });
}
