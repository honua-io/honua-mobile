import type { HonuaSceneConfig } from './scene';

export type HonuaScenePackageAssetKind = 'tileset' | 'terrain' | 'metadata' | 'asset';

export type HonuaScenePackageCacheErrorCode =
  | 'cache-miss'
  | 'unsupported-browser-storage'
  | 'expired-package'
  | 'invalid-package';

export type HonuaScenePackageCacheStorageResponseMode = 'cache-url' | 'object-url';

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

export interface CacheStorageScenePackageAssetUrlOptions {
  packageId: string;
  path: string;
  urlPrefix?: string;
  baseUrl?: string | URL;
}

export interface CacheStorageScenePackageResolverOptions {
  cacheName: string;
  urlPrefix?: string;
  baseUrl?: string | URL;
  responseMode?: HonuaScenePackageCacheStorageResponseMode;
  createObjectUrls?: boolean;
}

export interface CacheStorageScenePackageRequestOptions {
  cacheName: string;
  urlPrefix?: string;
  baseUrl?: string | URL;
}

const DEFAULT_CACHE_STORAGE_URL_PREFIX = '/honua-scene-packages/';
const UNSAFE_CACHE_STORAGE_URL_PATH_CHARACTER_PATTERN =
  /[\u0000-\u0020"#<>?`{}^\u007f-\u{10ffff}]/gu;

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
  const cacheName = normalizeRequiredText(
    options.cacheName,
    'A Cache Storage cache name is required.',
  );
  const responseMode = normalizeCacheStorageResponseMode(options);

  const objectUrls = new Set<string>();

  return {
    async resolveAsset(request) {
      const path = normalizeScenePackageAssetPath(request.path);
      const cacheUrl = createCacheStorageScenePackageAssetUrl({
        packageId: request.packageId,
        path,
        urlPrefix: options.urlPrefix,
        baseUrl: options.baseUrl,
      });
      const cache = await getCacheStorage().open(cacheName);
      const response = await cache.match(cacheUrl);
      if (!response) {
        throw new HonuaScenePackageCacheError(
          'cache-miss',
          `Scene package asset '${path}' was not found in cache '${cacheName}'.`,
        );
      }

      if (responseMode === 'cache-url') {
        return cacheUrl;
      }

      if (typeof URL.createObjectURL !== 'function') {
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

export function createCacheStorageScenePackageAssetUrl(
  options: CacheStorageScenePackageAssetUrlOptions,
): string {
  const packageId = normalizeRequiredText(
    options.packageId,
    'A scene package ID is required.',
  );
  const path = normalizeScenePackageAssetPath(options.path);
  const prefix = createCacheStorageScenePackageUrlPrefix(options.urlPrefix, options.baseUrl);
  validateCacheStorageUrlPathSegment(packageId, 'scene package ID');
  const packageUrl = new URL(`${encodeURIComponent(packageId)}/`, prefix).toString();

  return `${packageUrl}${encodeCacheStorageAssetPath(path)}`;
}

export function isCacheStorageScenePackageRequest(
  request: RequestInfo | URL,
  options: Omit<CacheStorageScenePackageRequestOptions, 'cacheName'> = {},
): boolean {
  const requestUrl = requestInfoToUrl(request, options.baseUrl);
  const prefix = createCacheStorageScenePackageUrlPrefix(options.urlPrefix, options.baseUrl);

  return requestUrl.startsWith(prefix);
}

export async function matchCacheStorageScenePackageRequest(
  request: RequestInfo | URL,
  options: CacheStorageScenePackageRequestOptions,
): Promise<Response | null> {
  const cacheName = normalizeRequiredText(
    options.cacheName,
    'A Cache Storage cache name is required.',
  );

  if (!isCacheStorageScenePackageRequest(request, options)) {
    return null;
  }

  const cache = await getCacheStorage().open(cacheName);
  return await cache.match(requestInfoToCacheMatchInput(request, options.baseUrl)) ?? null;
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

  if (segments.some((segment) => isCacheStorageUrlDotSegment(segment))) {
    throw new HonuaScenePackageCacheError(
      'invalid-package',
      `Scene package asset path '${path}' must stay under the package root.`,
    );
  }

  return segments.join('/');
}

function encodeCacheStorageAssetPath(path: string): string {
  return path.replace(
    UNSAFE_CACHE_STORAGE_URL_PATH_CHARACTER_PATTERN,
    (character) => encodeURIComponent(character),
  );
}

function validateCacheStorageUrlPathSegment(segment: string, label: string): void {
  if (!isCacheStorageUrlDotSegment(segment)) {
    return;
  }

  throw new HonuaScenePackageCacheError(
    'invalid-package',
    `The ${label} '${segment}' must not be a URL dot segment.`,
  );
}

function isCacheStorageUrlDotSegment(segment: string): boolean {
  const urlNormalizedSegment = segment.replace(/%2e/gi, '.');
  return urlNormalizedSegment === '.' || urlNormalizedSegment === '..';
}

function normalizeCacheStorageResponseMode(
  options: CacheStorageScenePackageResolverOptions,
): HonuaScenePackageCacheStorageResponseMode {
  const mode = options.responseMode
    ?? (options.createObjectUrls === false ? 'cache-url' : 'object-url');
  if (mode !== 'cache-url' && mode !== 'object-url') {
    throw new HonuaScenePackageCacheError(
      'invalid-package',
      `Unsupported Cache Storage response mode '${mode}'.`,
    );
  }

  return mode;
}

function createCacheStorageScenePackageUrlPrefix(
  urlPrefix = DEFAULT_CACHE_STORAGE_URL_PREFIX,
  baseUrl?: string | URL,
): string {
  const rawPrefix = urlPrefix.trim() || DEFAULT_CACHE_STORAGE_URL_PREFIX;
  const slashTerminated = rawPrefix.endsWith('/') ? rawPrefix : `${rawPrefix}/`;

  if (/^[a-z][a-z0-9+.-]*:/i.test(slashTerminated)) {
    return new URL(slashTerminated).toString();
  }

  const rootRelativePrefix = slashTerminated.startsWith('/')
    ? slashTerminated
    : `/${slashTerminated}`;

  return new URL(rootRelativePrefix, defaultBaseUrl(baseUrl)).toString();
}

function requestInfoToUrl(request: RequestInfo | URL, baseUrl?: string | URL): string {
  if (request instanceof URL) {
    return request.toString();
  }

  if (typeof request === 'string') {
    return new URL(request, defaultBaseUrl(baseUrl)).toString();
  }

  return request.url;
}

function requestInfoToCacheMatchInput(
  request: RequestInfo | URL,
  baseUrl?: string | URL,
): RequestInfo {
  if (request instanceof URL || typeof request === 'string') {
    return requestInfoToUrl(request, baseUrl);
  }

  return request;
}

function defaultBaseUrl(baseUrl?: string | URL): string {
  if (baseUrl) {
    return baseUrl.toString();
  }

  if (typeof location !== 'undefined') {
    return location.href;
  }

  return 'http://localhost/';
}

function getCacheStorage(): CacheStorage {
  if (!('caches' in globalThis) || !globalThis.caches) {
    throw new HonuaScenePackageCacheError(
      'unsupported-browser-storage',
      'Cache Storage is not available in this browser or WebView.',
    );
  }

  return globalThis.caches;
}

function normalizeRequiredText(value: string, message: string): string {
  const normalized = value.trim();
  if (normalized.length === 0) {
    throw new HonuaScenePackageCacheError('invalid-package', message);
  }

  return normalized;
}
