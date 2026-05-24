import type {
  HonuaMapConfig,
  HonuaMapElement,
  HonuaMapIdentifyDetail,
  HonuaMapSearchDetail,
} from './map';

export type HonuaMapGovernanceEventType =
  | 'view'
  | 'config-change'
  | 'search'
  | 'identify'
  | 'policy-denied';

export interface HonuaMapGovernanceEvent {
  target: 'map';
  type: HonuaMapGovernanceEventType;
  occurredAt: string;
  integrationId: string | null;
  origin: string | null;
  serviceUrl: string | null;
  serviceOrigin: string | null;
  layerIds: string[];
  apiKeyPresent: boolean;
  searchQuery?: string;
  identifyPoint?: { x: number; y: number };
  policyDecision?: HonuaMapPolicyDecision;
  metadata?: Record<string, unknown>;
}

export interface HonuaMapAnalyticsSink {
  record(event: HonuaMapGovernanceEvent): void | Promise<void>;
}

export interface HonuaMapRateLimitState {
  remaining?: number | null;
  resetAt?: string | null;
}

export interface HonuaMapEmbedPolicy {
  requiredApiKey?: boolean | null;
  allowedOrigins?: readonly string[] | null;
  allowedServiceOrigins?: readonly string[] | null;
  allowedLayerIds?: readonly string[] | null;
  rateLimit?: HonuaMapRateLimitState | null;
}

export interface HonuaMapPolicyContext {
  origin?: string | null;
}

export interface HonuaMapPolicyDecision {
  allowed: boolean;
  reasons: string[];
  warnings: string[];
}

export interface HonuaMapGovernanceBindingOptions {
  sink?: HonuaMapAnalyticsSink | ((event: HonuaMapGovernanceEvent) => void | Promise<void>) | null;
  policy?: HonuaMapEmbedPolicy | null;
  integrationId?: string | null;
  origin?: string | null;
  metadata?: Record<string, unknown> | null;
  clock?: () => Date;
}

export type HonuaMapGovernanceFetch = (
  input: RequestInfo | URL,
  init?: RequestInit,
) => Promise<Response>;

export interface HonuaMapGovernanceHttpSinkOptions {
  url: string | URL;
  fetch?: HonuaMapGovernanceFetch;
  headers?: HeadersInit | ((event: HonuaMapGovernanceEvent) => HeadersInit);
  credentials?: RequestCredentials;
  keepalive?: boolean;
  signal?: AbortSignal | null;
  onError?: (error: unknown, event: HonuaMapGovernanceEvent) => void;
}

export interface HonuaMapEmbedPolicyFetchOptions {
  url: string | URL;
  fetch?: HonuaMapGovernanceFetch;
  headers?: HeadersInit;
  credentials?: RequestCredentials;
  signal?: AbortSignal | null;
}

export interface HonuaMapRemoteGovernanceBindingOptions
  extends Omit<HonuaMapGovernanceBindingOptions, 'policy' | 'sink'> {
  policy?: HonuaMapEmbedPolicy | null;
  policyUrl?: string | URL | null;
  policyHeaders?: HeadersInit;
  analyticsUrl?: string | URL | null;
  analyticsHeaders?: HeadersInit | ((event: HonuaMapGovernanceEvent) => HeadersInit);
  sink?: HonuaMapGovernanceBindingOptions['sink'];
  fetch?: HonuaMapGovernanceFetch;
  credentials?: RequestCredentials;
  policyCredentials?: RequestCredentials;
  analyticsCredentials?: RequestCredentials;
  signal?: AbortSignal | null;
  keepalive?: boolean;
  onAnalyticsError?: (error: unknown, event: HonuaMapGovernanceEvent) => void;
}

export interface HonuaMapGovernanceBinding {
  evaluate(): HonuaMapPolicyDecision;
  disconnect(): void;
}

export interface HonuaMapPolicyDeniedDetail {
  decision: HonuaMapPolicyDecision;
  event: HonuaMapGovernanceEvent;
}

export function bindHonuaMapGovernance(
  element: HonuaMapElement,
  options: HonuaMapGovernanceBindingOptions = {},
): HonuaMapGovernanceBinding {
  const sink = normalizeSink(options.sink);
  const clock = options.clock ?? (() => new Date());
  const context = { origin: normalizeOptionalString(options.origin) ?? currentOrigin() };
  const metadata = options.metadata ? { ...options.metadata } : undefined;

  const createEvent = (
    type: HonuaMapGovernanceEventType,
    detail?: HonuaMapSearchDetail | HonuaMapIdentifyDetail,
    decision?: HonuaMapPolicyDecision,
  ): HonuaMapGovernanceEvent => createHonuaMapGovernanceEvent(element.config, type, {
    occurredAt: clock().toISOString(),
    integrationId: normalizeOptionalString(options.integrationId),
    origin: context.origin,
    metadata,
    searchQuery: isSearchDetail(detail) ? detail.query : undefined,
    identifyPoint: isIdentifyDetail(detail) ? { x: detail.x, y: detail.y } : undefined,
    policyDecision: decision,
  });

  const evaluate = (): HonuaMapPolicyDecision =>
    evaluateHonuaMapPolicy(element.config, options.policy, context);

  const recordIfAllowed = (
    type: HonuaMapGovernanceEventType,
    detail?: HonuaMapSearchDetail | HonuaMapIdentifyDetail,
  ): void => {
    const decision = evaluate();
    if (!decision.allowed) {
      const deniedEvent = createEvent('policy-denied', detail, decision);
      safeRecord(sink, deniedEvent);
      element.dispatchEvent(new CustomEvent<HonuaMapPolicyDeniedDetail>('honua-map-policy-denied', {
        bubbles: true,
        composed: true,
        detail: {
          decision,
          event: deniedEvent,
        },
      }));
      return;
    }

    safeRecord(sink, createEvent(type, detail, decision));
  };

  let viewRecorded = false;
  const ready = (): void => {
    if (viewRecorded) {
      return;
    }

    viewRecorded = true;
    recordIfAllowed('view');
  };
  const configChange = (): void => recordIfAllowed('config-change');
  const search = (event: Event): void =>
    recordIfAllowed('search', (event as CustomEvent<HonuaMapSearchDetail>).detail);
  const identify = (event: Event): void =>
    recordIfAllowed('identify', (event as CustomEvent<HonuaMapIdentifyDetail>).detail);

  element.addEventListener('honua-map-ready', ready);
  element.addEventListener('honua-map-config-change', configChange);
  element.addEventListener('honua-map-search', search);
  element.addEventListener('honua-map-identify', identify);

  if (element.isConnected) {
    ready();
  }

  return {
    evaluate,
    disconnect(): void {
      element.removeEventListener('honua-map-ready', ready);
      element.removeEventListener('honua-map-config-change', configChange);
      element.removeEventListener('honua-map-search', search);
      element.removeEventListener('honua-map-identify', identify);
    },
  };
}

export function createHonuaMapGovernanceHttpSink(
  options: HonuaMapGovernanceHttpSinkOptions,
): HonuaMapAnalyticsSink {
  const fetchImpl = resolveFetch(options.fetch);
  const url = normalizeRequiredUrl(options.url, 'Analytics URL');

  return {
    async record(event: HonuaMapGovernanceEvent): Promise<void> {
      try {
        const response = await fetchImpl(url, {
          method: 'POST',
          headers: createJsonHeaders(resolveHeaders(options.headers, event)),
          body: JSON.stringify(event),
          credentials: options.credentials,
          keepalive: options.keepalive,
          signal: options.signal ?? undefined,
        });

        if (!response.ok) {
          throw new Error(`Honua map analytics request failed with HTTP ${response.status}.`);
        }
      } catch (error) {
        options.onError?.(error, event);
      }
    },
  };
}

export async function fetchHonuaMapEmbedPolicy(
  options: HonuaMapEmbedPolicyFetchOptions,
): Promise<HonuaMapEmbedPolicy | null> {
  const fetchImpl = resolveFetch(options.fetch);
  const url = normalizeRequiredUrl(options.url, 'Policy URL');
  const response = await fetchImpl(url, {
    method: 'GET',
    headers: options.headers,
    credentials: options.credentials,
    signal: options.signal ?? undefined,
  });

  if (response.status === 204) {
    return null;
  }

  if (!response.ok) {
    throw new Error(`Honua map policy request failed with HTTP ${response.status}.`);
  }

  return normalizeHonuaMapEmbedPolicy(await response.json());
}

export async function bindHonuaMapRemoteGovernance(
  element: HonuaMapElement,
  options: HonuaMapRemoteGovernanceBindingOptions = {},
): Promise<HonuaMapGovernanceBinding> {
  const policy = options.policyUrl !== undefined && options.policyUrl !== null
    ? await fetchHonuaMapEmbedPolicy({
      url: options.policyUrl,
      fetch: options.fetch,
      headers: options.policyHeaders,
      credentials: options.policyCredentials ?? options.credentials,
      signal: options.signal,
    })
    : options.policy ?? null;

  const analyticsSink = options.analyticsUrl !== undefined && options.analyticsUrl !== null
    ? createHonuaMapGovernanceHttpSink({
      url: options.analyticsUrl,
      fetch: options.fetch,
      headers: options.analyticsHeaders,
      credentials: options.analyticsCredentials ?? options.credentials,
      keepalive: options.keepalive,
      signal: options.signal,
      onError: options.onAnalyticsError,
    })
    : null;

  return bindHonuaMapGovernance(element, {
    sink: combineSinks(options.sink, analyticsSink),
    policy,
    integrationId: options.integrationId,
    origin: options.origin,
    metadata: options.metadata,
    clock: options.clock,
  });
}

export function createHonuaMapGovernanceEvent(
  config: HonuaMapConfig,
  type: HonuaMapGovernanceEventType,
  options: {
    occurredAt?: string;
    integrationId?: string | null;
    origin?: string | null;
    searchQuery?: string;
    identifyPoint?: { x: number; y: number };
    policyDecision?: HonuaMapPolicyDecision;
    metadata?: Record<string, unknown>;
  } = {},
): HonuaMapGovernanceEvent {
  return {
    target: 'map',
    type,
    occurredAt: options.occurredAt ?? new Date().toISOString(),
    integrationId: normalizeOptionalString(options.integrationId),
    origin: normalizeOptionalString(options.origin),
    serviceUrl: config.serviceUrl,
    serviceOrigin: serviceOrigin(config.serviceUrl),
    layerIds: [...config.layerIds],
    apiKeyPresent: Boolean(config.apiKey),
    searchQuery: normalizeOptionalString(options.searchQuery) ?? undefined,
    identifyPoint: options.identifyPoint,
    policyDecision: options.policyDecision,
    metadata: options.metadata,
  };
}

export function normalizeHonuaMapEmbedPolicy(value: unknown): HonuaMapEmbedPolicy | null {
  if (!isRecord(value)) {
    return null;
  }

  const policy: HonuaMapEmbedPolicy = {};

  if (typeof value.requiredApiKey === 'boolean' || value.requiredApiKey === null) {
    policy.requiredApiKey = value.requiredApiKey;
  }

  const allowedOrigins = normalizeStringArray(value.allowedOrigins);
  if (allowedOrigins) {
    policy.allowedOrigins = allowedOrigins;
  }

  const allowedServiceOrigins = normalizeStringArray(value.allowedServiceOrigins);
  if (allowedServiceOrigins) {
    policy.allowedServiceOrigins = allowedServiceOrigins;
  }

  const allowedLayerIds = normalizeStringArray(value.allowedLayerIds);
  if (allowedLayerIds) {
    policy.allowedLayerIds = allowedLayerIds;
  }

  const rateLimit = normalizeRateLimit(value.rateLimit);
  if (rateLimit) {
    policy.rateLimit = rateLimit;
  }

  return policy;
}

export function evaluateHonuaMapPolicy(
  config: HonuaMapConfig,
  policy: HonuaMapEmbedPolicy | null | undefined,
  context: HonuaMapPolicyContext = {},
): HonuaMapPolicyDecision {
  const reasons: string[] = [];
  const warnings: string[] = [];

  if (!policy) {
    return { allowed: true, reasons, warnings };
  }

  if (policy.requiredApiKey && !config.apiKey) {
    reasons.push('API key is required for this embed.');
  }

  const origin = normalizeOptionalString(context.origin);
  const allowedOrigins = normalizeSet(policy.allowedOrigins);
  if (allowedOrigins.size > 0) {
    if (!origin) {
      reasons.push('Embed origin is unavailable for policy evaluation.');
    } else if (!allowedOrigins.has(origin)) {
      reasons.push('Embed origin is not allowed for this API key.');
    }
  }

  const allowedServiceOrigins = normalizeSet(policy.allowedServiceOrigins);
  const configServiceOrigin = serviceOrigin(config.serviceUrl);
  if (allowedServiceOrigins.size > 0) {
    if (!configServiceOrigin) {
      reasons.push('Service URL origin is unavailable for policy evaluation.');
    } else if (!allowedServiceOrigins.has(configServiceOrigin)) {
      reasons.push('Service URL origin is not allowed for this API key.');
    }
  }

  const allowedLayerIds = normalizeSet(policy.allowedLayerIds);
  if (allowedLayerIds.size > 0) {
    const deniedLayers = config.layerIds.filter((layerId) => !allowedLayerIds.has(layerId));
    if (deniedLayers.length > 0) {
      reasons.push(`Layer ids are not allowed for this API key: ${deniedLayers.join(', ')}`);
    }
  }

  const remaining = policy.rateLimit?.remaining;
  if (remaining !== undefined && remaining !== null) {
    if (!Number.isFinite(remaining)) {
      warnings.push('Rate limit remaining value is not finite.');
    } else if (remaining <= 0) {
      reasons.push(policy.rateLimit?.resetAt
        ? `API key rate limit is exhausted until ${policy.rateLimit.resetAt}.`
        : 'API key rate limit is exhausted.');
    }
  }

  return {
    allowed: reasons.length === 0,
    reasons,
    warnings,
  };
}

function normalizeSink(
  sink: HonuaMapGovernanceBindingOptions['sink'],
): ((event: HonuaMapGovernanceEvent) => void | Promise<void>) | null {
  if (!sink) {
    return null;
  }

  if (typeof sink === 'function') {
    return sink;
  }

  return (event) => sink.record(event);
}

function combineSinks(
  first: HonuaMapGovernanceBindingOptions['sink'],
  second: HonuaMapGovernanceBindingOptions['sink'],
): HonuaMapGovernanceBindingOptions['sink'] {
  const sinks = [normalizeSink(first), normalizeSink(second)].filter(isNonNull);

  if (sinks.length === 0) {
    return null;
  }

  if (sinks.length === 1) {
    return sinks[0];
  }

  return (event) => {
    for (const sink of sinks) {
      safeRecord(sink, event);
    }
  };
}

function safeRecord(
  sink: ((event: HonuaMapGovernanceEvent) => void | Promise<void>) | null,
  event: HonuaMapGovernanceEvent,
): void {
  if (!sink) {
    return;
  }

  try {
    void Promise.resolve(sink(event)).catch(() => {
      // Analytics sinks are host-owned and must not break embed interactions.
    });
  } catch {
    // Analytics sinks are host-owned and must not break embed interactions.
  }
}

function resolveFetch(fetchImpl: HonuaMapGovernanceFetch | undefined): HonuaMapGovernanceFetch {
  const resolved = fetchImpl ?? globalThis.fetch?.bind(globalThis);
  if (!resolved) {
    throw new Error('Honua map governance fetch API is not available.');
  }

  return resolved;
}

function normalizeRequiredUrl(url: string | URL, label: string): string {
  const normalized = String(url).trim();
  if (!normalized) {
    throw new Error(`${label} is required for Honua map governance.`);
  }

  return normalized;
}

function resolveHeaders(
  headers: HeadersInit | ((event: HonuaMapGovernanceEvent) => HeadersInit) | undefined,
  event: HonuaMapGovernanceEvent,
): HeadersInit | undefined {
  return typeof headers === 'function' ? headers(event) : headers;
}

function createJsonHeaders(headers: HeadersInit | undefined): Headers {
  const normalized = new Headers(headers);
  if (!normalized.has('content-type')) {
    normalized.set('content-type', 'application/json');
  }

  return normalized;
}

function serviceOrigin(serviceUrl: string | null): string | null {
  if (!serviceUrl) {
    return null;
  }

  try {
    return new URL(serviceUrl).origin;
  } catch {
    return null;
  }
}

function normalizeSet(values: readonly string[] | null | undefined): Set<string> {
  return new Set((values ?? [])
    .map((value) => value.trim())
    .filter(Boolean));
}

function normalizeStringArray(value: unknown): string[] | null {
  if (!Array.isArray(value)) {
    return null;
  }

  return value
    .filter((item): item is string => typeof item === 'string')
    .map((item) => item.trim())
    .filter(Boolean);
}

function normalizeOptionalString(value: string | null | undefined): string | null {
  const normalized = value?.trim();
  return normalized ? normalized : null;
}

function normalizeRateLimit(value: unknown): HonuaMapRateLimitState | null {
  if (!isRecord(value)) {
    return null;
  }

  const rateLimit: HonuaMapRateLimitState = {};
  if (typeof value.remaining === 'number' && Number.isFinite(value.remaining)) {
    rateLimit.remaining = value.remaining;
  } else if (value.remaining === null) {
    rateLimit.remaining = null;
  }

  if (typeof value.resetAt === 'string') {
    rateLimit.resetAt = normalizeOptionalString(value.resetAt);
  } else if (value.resetAt === null) {
    rateLimit.resetAt = null;
  }

  return Object.keys(rateLimit).length > 0 ? rateLimit : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function isNonNull<T>(value: T | null): value is T {
  return value !== null;
}

function currentOrigin(): string | null {
  return globalThis.location?.origin ?? null;
}

function isSearchDetail(
  detail: HonuaMapSearchDetail | HonuaMapIdentifyDetail | undefined,
): detail is HonuaMapSearchDetail {
  return Boolean(detail && 'query' in detail);
}

function isIdentifyDetail(
  detail: HonuaMapSearchDetail | HonuaMapIdentifyDetail | undefined,
): detail is HonuaMapIdentifyDetail {
  return Boolean(detail && 'x' in detail && 'y' in detail);
}
