import type { HonuaSceneConfig } from './scene';

export type HonuaScenePackageAssetKind = 'tileset' | 'terrain' | 'metadata' | 'asset';

export type HonuaScenePackageCacheErrorCode =
  | 'cache-miss'
  | 'unsupported-browser-storage'
  | 'expired-package'
  | 'invalid-package';

export interface HonuaScenePackageAssetResolverRequest {
  packageId: string;
  path: string;
  kind: HonuaScenePackageAssetKind;
  config: HonuaSceneConfig;
}

export interface HonuaScenePackageAssetResolver {
  resolveAsset(
    request: HonuaScenePackageAssetResolverRequest,
  ): Promise<string | URL> | string | URL;
  dispose?(): void;
}

export type HonuaScenePackageAssetResolverInput =
  | HonuaScenePackageAssetResolver
  | ((request: HonuaScenePackageAssetResolverRequest) => Promise<string | URL> | string | URL);

export interface CacheStorageScenePackageResolverOptions {
  cacheName: string;
  urlPrefix?: string;
  createObjectUrls?: boolean;
}

export class HonuaScenePackageCacheError extends Error {
  readonly code: HonuaScenePackageCacheErrorCode;

  constructor(code: HonuaScenePackageCacheErrorCode, message: string) {
    super(message);
    this.name = 'HonuaScenePackageCacheError';
    this.code = code;
  }
}

export async function resolveScenePackageAsset(
  resolver: HonuaScenePackageAssetResolverInput,
  request: HonuaScenePackageAssetResolverRequest,
): Promise<string> {
  const result = typeof resolver === 'function'
    ? await resolver(request)
    : await resolver.resolveAsset(request);
  const url = result instanceof URL ? result.toString() : result.trim();

  if (url.length === 0) {
    throw new HonuaScenePackageCacheError(
      'cache-miss',
      `Scene package asset '${request.path}' was not found in the browser cache.`,
    );
  }

  return url;
}

export function createCacheStorageScenePackageResolver(
  options: CacheStorageScenePackageResolverOptions,
): HonuaScenePackageAssetResolver {
  if (!options.cacheName.trim()) {
    throw new HonuaScenePackageCacheError(
      'invalid-package',
      'A Cache Storage cache name is required.',
    );
  }

  const objectUrls = new Set<string>();
  const createObjectUrls = options.createObjectUrls ?? true;

  return {
    async resolveAsset(request) {
      if (!('caches' in globalThis)) {
        throw new HonuaScenePackageCacheError(
          'unsupported-browser-storage',
          'Cache Storage is not available in this browser or WebView.',
        );
      }

      const path = normalizeScenePackageAssetPath(request.path);
      const cache = await globalThis.caches.open(options.cacheName);
      const cacheUrl = buildCacheStorageAssetUrl(
        options.urlPrefix ?? '/honua-scene-packages/',
        request.packageId,
        path,
      );
      const response = await cache.match(cacheUrl);
      if (!response) {
        throw new HonuaScenePackageCacheError(
          'cache-miss',
          `Scene package asset '${path}' was not found in cache '${options.cacheName}'.`,
        );
      }

      if (!createObjectUrls) {
        return cacheUrl;
      }

      if (!URL.createObjectURL) {
        throw new HonuaScenePackageCacheError(
          'unsupported-browser-storage',
          'Object URLs are not available in this browser or WebView.',
        );
      }

      const objectUrl = URL.createObjectURL(await response.blob());
      objectUrls.add(objectUrl);
      return objectUrl;
    },
    dispose() {
      for (const objectUrl of objectUrls) {
        URL.revokeObjectURL(objectUrl);
      }

      objectUrls.clear();
    },
  };
}

export function normalizeScenePackageAssetPath(path: string): string {
  const trimmed = path.trim();
  if (
    trimmed.length === 0 ||
    trimmed.startsWith('/') ||
    trimmed.includes('\\') ||
    /^[a-z][a-z0-9+.-]*:/i.test(trimmed)
  ) {
    throw new HonuaScenePackageCacheError(
      'invalid-package',
      `Scene package asset path '${path}' must be package-local and relative.`,
    );
  }

  const segments = trimmed
    .split('/')
    .filter((segment) => segment.length > 0);

  if (segments.some((segment) => segment === '.' || segment === '..')) {
    throw new HonuaScenePackageCacheError(
      'invalid-package',
      `Scene package asset path '${path}' must stay under the package root.`,
    );
  }

  return segments.join('/');
}

function buildCacheStorageAssetUrl(
  urlPrefix: string,
  packageId: string,
  path: string,
): string {
  const origin = typeof location === 'undefined' ? 'http://localhost' : location.origin;
  const prefix = urlPrefix.trim().replace(/^\/?/, '/').replace(/\/?$/, '/');
  return new URL(`${prefix}${encodeURIComponent(packageId)}/${path}`, origin).toString();
}
