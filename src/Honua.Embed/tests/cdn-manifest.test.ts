import { createHash } from 'node:crypto';
import { mkdtemp, mkdir, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { afterEach, describe, expect, it } from 'vitest';

import { createCdnManifest } from '../scripts/create-cdn-manifest.mjs';

const tempRoots: string[] = [];

afterEach(async () => {
  await Promise.all(tempRoots.map((root) => rm(root, { recursive: true, force: true })));
  tempRoots.length = 0;
});

describe('CDN manifest generation', () => {
  it('writes deterministic metadata for top-level CDN JavaScript entries', async () => {
    const packageRoot = await createPackageFixture({
      'embed.js': 'export const entry = "embed";\n',
      'index.js': 'export const entry = "index";\n',
      'iframe.js': 'export const entry = "iframe";\n',
      'snippets.js': 'export const entry = "snippets";\n',
      'shared-abc123.js': 'export const shared = true;\n',
    });

    await mkdir(join(packageRoot, 'dist', 'cesium'), { recursive: true });
    await writeFile(join(packageRoot, 'dist', 'style.css'), 'body {}\n');
    await writeFile(join(packageRoot, 'dist', 'cesium', 'Worker.js'), 'ignored\n');

    const manifest = await createCdnManifest({
      packageRoot,
      baseUrl: 'https://cdn.example.test/honua/embed',
    });
    const manifestJson = JSON.parse(await readFile(join(packageRoot, 'dist', 'cdn-manifest.json'), 'utf8'));

    expect(manifestJson).toEqual(manifest);
    expect(manifest).toMatchObject({
      schemaVersion: 1,
      package: {
        name: '@honua-io/embed',
        version: '9.8.7',
      },
      defaultBaseUrl: 'https://cdn.example.test/honua/embed/',
      defaultUrls: {
        embed: 'https://cdn.example.test/honua/embed/embed.js',
        iframe: 'https://cdn.example.test/honua/embed/embed/map.html',
        manifest: 'https://cdn.example.test/honua/embed/cdn-manifest.json',
      },
    });
    expect(manifest.files.map((file) => file.path)).toEqual([
      'embed.js',
      'iframe.js',
      'index.js',
      'shared-abc123.js',
      'snippets.js',
    ]);
    expect(manifest.files).not.toContainEqual(expect.objectContaining({ path: 'cesium/Worker.js' }));

    const embedContent = 'export const entry = "embed";\n';
    expect(manifest.files[0]).toEqual({
      path: 'embed.js',
      bytes: Buffer.byteLength(embedContent),
      sha256: createHash('sha256').update(embedContent).digest('hex'),
      integrity: `sha384-${createHash('sha384').update(embedContent).digest('base64')}`,
      url: 'https://cdn.example.test/honua/embed/embed.js',
    });
  });

  it('fails when a required CDN entry is missing', async () => {
    const packageRoot = await createPackageFixture({
      'embed.js': '',
      'index.js': '',
      'iframe.js': '',
    });

    await expect(createCdnManifest({ packageRoot })).rejects.toThrow(
      'Missing required CDN entries in dist: snippets.js',
    );
  });
});

async function createPackageFixture(files: Record<string, string>) {
  const packageRoot = await mkdtemp(join(tmpdir(), 'honua-embed-cdn-'));
  tempRoots.push(packageRoot);

  await mkdir(join(packageRoot, 'dist'), { recursive: true });
  await writeFile(join(packageRoot, 'package.json'), JSON.stringify({
    name: '@honua-io/embed',
    version: '9.8.7',
  }));

  await Promise.all(Object.entries(files).map(([name, content]) => (
    writeFile(join(packageRoot, 'dist', name), content)
  )));

  return packageRoot;
}
