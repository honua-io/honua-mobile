import { createHash } from 'node:crypto';
import { readdir, readFile, stat, writeFile } from 'node:fs/promises';
import { dirname, join, relative, sep } from 'node:path';
import { fileURLToPath } from 'node:url';

const requiredEntries = ['embed.js', 'index.js', 'iframe.js', 'snippets.js'];
const defaultBaseUrl = 'https://cdn.honua.dev/';

export async function createCdnManifest(options = {}) {
  const packageRoot = options.packageRoot ?? dirname(dirname(fileURLToPath(import.meta.url)));
  const distRoot = options.distRoot ?? join(packageRoot, 'dist');
  const outputPath = options.outputPath ?? join(distRoot, 'cdn-manifest.json');
  const baseUrl = normalizeBaseUrl(options.baseUrl ?? defaultBaseUrl);
  const packageJson = JSON.parse(await readFile(join(packageRoot, 'package.json'), 'utf8'));
  const files = await collectCdnFiles(distRoot, baseUrl);

  const manifest = {
    schemaVersion: 1,
    package: {
      name: packageJson.name,
      version: packageJson.version,
    },
    defaultBaseUrl: baseUrl,
    defaultUrls: {
      embed: new URL('embed.js', baseUrl).toString(),
      iframe: new URL('embed/map.html', baseUrl).toString(),
      manifest: new URL('cdn-manifest.json', baseUrl).toString(),
    },
    files,
  };

  await writeFile(outputPath, `${JSON.stringify(manifest, null, 2)}\n`);
  return manifest;
}

export async function collectCdnFiles(distRoot, baseUrl = defaultBaseUrl) {
  const entries = await readdir(distRoot, { withFileTypes: true });
  const topLevelJsFiles = entries
    .filter((entry) => entry.isFile() && entry.name.endsWith('.js'))
    .map((entry) => entry.name)
    .sort(compareFileNames);

  const missingEntries = requiredEntries.filter((entry) => !topLevelJsFiles.includes(entry));
  if (missingEntries.length > 0) {
    throw new Error(`Missing required CDN entries in dist: ${missingEntries.join(', ')}`);
  }

  return Promise.all(topLevelJsFiles.map(async (name) => {
    const path = join(distRoot, name);
    const [metadata, content] = await Promise.all([stat(path), readFile(path)]);
    const relativePath = relative(distRoot, path).split(sep).join('/');

    return {
      path: relativePath,
      bytes: metadata.size,
      sha256: createHash('sha256').update(content).digest('hex'),
      integrity: `sha384-${createHash('sha384').update(content).digest('base64')}`,
      url: new URL(relativePath, baseUrl).toString(),
    };
  }));
}

export function normalizeBaseUrl(value) {
  const url = new URL(value);
  if (!url.pathname.endsWith('/')) {
    url.pathname = `${url.pathname}/`;
  }

  return url.toString();
}

function readCliOptions(argv) {
  const options = {};

  for (let index = 0; index < argv.length; index += 1) {
    const arg = argv[index];
    const next = argv[index + 1];

    if (arg === '--base-url' && isValue(next)) {
      options.baseUrl = next;
      index += 1;
    } else if (arg === '--package-root' && isValue(next)) {
      options.packageRoot = next;
      index += 1;
    } else if (arg === '--dist-root' && isValue(next)) {
      options.distRoot = next;
      index += 1;
    } else if (arg === '--output' && isValue(next)) {
      options.outputPath = next;
      index += 1;
    } else {
      throw new Error(`Unknown or incomplete argument: ${arg}`);
    }
  }

  return options;
}

function compareFileNames(left, right) {
  if (left < right) {
    return -1;
  }

  if (left > right) {
    return 1;
  }

  return 0;
}

function isValue(value) {
  return typeof value === 'string' && !value.startsWith('--');
}

if (process.argv[1] === fileURLToPath(import.meta.url)) {
  createCdnManifest(readCliOptions(process.argv.slice(2)))
    .then((manifest) => {
      console.log(`Wrote ${manifest.defaultUrls.manifest}`);
    })
    .catch((error) => {
      console.error(error instanceof Error ? error.message : error);
      process.exitCode = 1;
    });
}
