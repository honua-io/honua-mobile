import { afterEach, describe, expect, it, vi } from 'vitest';
import type { HonuaSceneConfig } from '../src/index';
import {
  createCacheStorageScenePackageAssetUrl,
  createCacheStorageScenePackageResolver,
  HonuaScenePackageCacheError,
  isCacheStorageScenePackageRequest,
  matchCacheStorageScenePackageRequest,
  normalizeScenePackageAssetPath,
  resolveScenePackageAsset,
} from '../src/index';

const ORIGINAL_CACHES = globalThis.caches;
const ORIGINAL_CREATE_OBJECT_URL = URL.createObjectURL;
const ORIGINAL_REVOKE_OBJECT_URL = URL.revokeObjectURL;

describe('scene package cache adapter', () => {
  afterEach(() => {
    restoreGlobalCacheStorage();
    setUrlStatic('createObjectURL', ORIGINAL_CREATE_OBJECT_URL);
    setUrlStatic('revokeObjectURL', ORIGINAL_REVOKE_OBJECT_URL);
    vi.restoreAllMocks();
  });

  it('normalizes package-local asset paths and rejects non-package URLs', () => {
    expect(normalizeScenePackageAssetPath(' tilesets//buildings/tileset.json ')).toBe(
      'tilesets/buildings/tileset.json',
    );

    expect(() => normalizeScenePackageAssetPath('https://example.test/tileset.json'))
      .toThrow(HonuaScenePackageCacheError);
    expect(() => normalizeScenePackageAssetPath('../tileset.json'))
      .toThrow(HonuaScenePackageCacheError);
    expect(() => normalizeScenePackageAssetPath('/tileset.json'))
      .toThrow(HonuaScenePackageCacheError);
    expect(() => normalizeScenePackageAssetPath('%2e%2e/tileset.json'))
      .toThrow(HonuaScenePackageCacheError);
  });

  it('builds stable package cache URLs for service workers and WebView routes', () => {
    expect(createCacheStorageScenePackageAssetUrl({
      baseUrl: 'https://app.example.test/viewer/index.html',
      packageId: 'pkg/downtown 42',
      path: 'tilesets/Main Hall/tileset.json',
    })).toBe(
      'https://app.example.test/honua-scene-packages/pkg%2Fdowntown%2042/tilesets/Main%20Hall/tileset.json',
    );

    expect(createCacheStorageScenePackageAssetUrl({
      baseUrl: 'https://app.example.test/viewer/index.html',
      packageId: 'pkg-downtown',
      path: 'terrain/layer.json',
      urlPrefix: 'https://cache.example.test/scenes/',
    })).toBe('https://cache.example.test/scenes/pkg-downtown/terrain/layer.json');
  });

  it('preserves literal package asset path characters in cache URLs', () => {
    expect(createCacheStorageScenePackageAssetUrl({
      baseUrl: 'https://app.example.test/viewer/index.html',
      packageId: 'pkg-downtown',
      path: 'tilesets/a+b.b3dm',
    })).toBe('https://app.example.test/honua-scene-packages/pkg-downtown/tilesets/a+b.b3dm');

    expect(createCacheStorageScenePackageAssetUrl({
      baseUrl: 'https://app.example.test/viewer/index.html',
      packageId: 'pkg-downtown',
      path: 'tilesets/already%20encoded%2Ftile.b3dm',
    })).toBe(
      'https://app.example.test/honua-scene-packages/pkg-downtown/tilesets/already%20encoded%2Ftile.b3dm',
    );

    expect(createCacheStorageScenePackageAssetUrl({
      baseUrl: 'https://app.example.test/viewer/index.html',
      packageId: 'pkg-downtown',
      path: 'tilesets/query?and#fragment.b3dm',
    })).toBe(
      'https://app.example.test/honua-scene-packages/pkg-downtown/tilesets/query%3Fand%23fragment.b3dm',
    );
  });

  it('resolves cached assets as stable cache URLs when requested', async () => {
    const assetUrl = createCacheStorageScenePackageAssetUrl({
      baseUrl: 'https://app.example.test/viewer/index.html',
      packageId: 'pkg-downtown',
      path: 'tilesets/buildings/tileset.json',
    });
    const cache = installCacheStorage({
      [assetUrl]: new Response('{}', { headers: { 'content-type': 'application/json' } }),
    });
    const resolver = createCacheStorageScenePackageResolver({
      cacheName: 'honua-scene-packages',
      baseUrl: 'https://app.example.test/viewer/index.html',
      responseMode: 'cache-url',
    });

    await expect(resolveScenePackageAsset(resolver, {
      packageId: 'pkg-downtown',
      path: 'tilesets/buildings/tileset.json',
      kind: 'tileset',
      config: sceneConfig(),
    })).resolves.toBe(assetUrl);

    expect(cache.open).toHaveBeenCalledWith('honua-scene-packages');
    expect(cache.match).toHaveBeenCalledWith(assetUrl);
  });

  it('creates and revokes object URLs for standalone cached assets', async () => {
    const assetUrl = createCacheStorageScenePackageAssetUrl({
      baseUrl: 'https://app.example.test/',
      packageId: 'pkg-downtown',
      path: 'metadata/scene.json',
    });
    installCacheStorage({
      [assetUrl]: new Response('{"id":"scene"}', {
        headers: { 'content-type': 'application/json' },
      }),
    });
    const createObjectURL = vi.fn(() => 'blob:https://app.example.test/scene-metadata');
    const revokeObjectURL = vi.fn();
    setUrlStatic('createObjectURL', createObjectURL);
    setUrlStatic('revokeObjectURL', revokeObjectURL);
    const resolver = createCacheStorageScenePackageResolver({
      cacheName: 'honua-scene-packages',
      baseUrl: 'https://app.example.test/',
      responseMode: 'object-url',
    });

    await expect(resolveScenePackageAsset(resolver, {
      packageId: 'pkg-downtown',
      path: 'metadata/scene.json',
      kind: 'metadata',
      config: sceneConfig(),
    })).resolves.toBe('blob:https://app.example.test/scene-metadata');

    expect(createObjectURL).toHaveBeenCalledOnce();
    resolver.dispose?.();
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:https://app.example.test/scene-metadata');
  });

  it('surfaces cache misses and unsupported Cache Storage through typed errors', async () => {
    installCacheStorage({});
    const resolver = createCacheStorageScenePackageResolver({
      cacheName: 'honua-scene-packages',
      baseUrl: 'https://app.example.test/',
      responseMode: 'cache-url',
    });

    await expect(resolveScenePackageAsset(resolver, {
      packageId: 'pkg-downtown',
      path: 'tilesets/missing/tileset.json',
      kind: 'tileset',
      config: sceneConfig(),
    })).rejects.toMatchObject({ code: 'cache-miss' });

    restoreGlobalCacheStorage();
    await expect(resolveScenePackageAsset(resolver, {
      packageId: 'pkg-downtown',
      path: 'tilesets/buildings/tileset.json',
      kind: 'tileset',
      config: sceneConfig(),
    })).rejects.toMatchObject({ code: 'unsupported-browser-storage' });
  });

  it('matches package cache requests for service worker fetch handlers', async () => {
    const assetUrl = createCacheStorageScenePackageAssetUrl({
      baseUrl: 'https://app.example.test/viewer/',
      packageId: 'pkg-downtown',
      path: 'tilesets/buildings/0/0.b3dm',
    });
    installCacheStorage({
      [assetUrl]: new Response('tile-binary'),
    });

    expect(isCacheStorageScenePackageRequest(assetUrl, {
      baseUrl: 'https://app.example.test/viewer/',
    })).toBe(true);
    expect(isCacheStorageScenePackageRequest('https://app.example.test/assets/0.b3dm', {
      baseUrl: 'https://app.example.test/viewer/',
    })).toBe(false);

    const response = await matchCacheStorageScenePackageRequest(new Request(assetUrl), {
      cacheName: 'honua-scene-packages',
      baseUrl: 'https://app.example.test/viewer/',
    });
    expect(await response?.text()).toBe('tile-binary');

    await expect(matchCacheStorageScenePackageRequest('https://app.example.test/assets/0.b3dm', {
      cacheName: 'honua-scene-packages',
      baseUrl: 'https://app.example.test/viewer/',
    })).resolves.toBeNull();
  });
});

function installCacheStorage(entries: Record<string, Response>): {
  open: ReturnType<typeof vi.fn>;
  match: ReturnType<typeof vi.fn>;
} {
  const match = vi.fn(async (request: RequestInfo) => {
    const url = typeof request === 'string' ? request : request.url;
    return entries[url] ?? undefined;
  });
  const open = vi.fn(async () => ({ match }) as unknown as Cache);

  Object.defineProperty(globalThis, 'caches', {
    configurable: true,
    value: { open } as unknown as CacheStorage,
  });

  return { open, match };
}

function restoreGlobalCacheStorage(): void {
  if (ORIGINAL_CACHES === undefined) {
    Reflect.deleteProperty(globalThis, 'caches');
    return;
  }

  Object.defineProperty(globalThis, 'caches', {
    configurable: true,
    value: ORIGINAL_CACHES,
  });
}

function setUrlStatic(
  key: 'createObjectURL' | 'revokeObjectURL',
  value: typeof URL.createObjectURL | typeof URL.revokeObjectURL,
): void {
  Object.defineProperty(URL, key, {
    configurable: true,
    value,
  });
}

function sceneConfig(): HonuaSceneConfig {
  return {
    tilesetUrl: null,
    terrainUrl: null,
    packageId: 'pkg-downtown',
    tilesetAssetPath: null,
    terrainAssetPath: null,
    packageExpiresAtUtc: null,
    ionToken: null,
    cesiumBaseUrl: null,
    center: null,
    height: 1200,
    orientation: {
      heading: 0,
      pitch: -45,
      roll: 0,
    },
    theme: 'dark',
    autoload: false,
  };
}
