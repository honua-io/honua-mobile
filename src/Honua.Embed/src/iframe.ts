import { defineHonuaMapElement, type HonuaMapElement } from './map';
import { defineHonuaSceneElement, type HonuaSceneElement } from './scene';
import {
  applyHonuaMapOptions,
  applyHonuaSceneOptions,
  type HonuaMapEmbedOptions,
  type HonuaMapThemeOptions,
  type HonuaSceneEmbedOptions,
} from './snippets';

export interface HonuaMapIframeConfig {
  options: HonuaMapEmbedOptions;
  parentOrigin: string | null;
}

export interface HonuaSceneIframeConfig {
  options: HonuaSceneEmbedOptions;
  parentOrigin: string | null;
}

export interface HonuaMapIframeMessage {
  source: 'honua-map-iframe';
  version: 1;
  type: HonuaMapIframeEventType;
  detail: unknown;
}

export interface HonuaSceneIframeMessage {
  source: 'honua-scene-iframe';
  version: 1;
  type: HonuaSceneIframeEventType;
  detail: unknown;
}

export interface HonuaMapIframeConfigureCommand {
  source: 'honua-map-host';
  version: 1;
  type: 'honua-map-configure';
  options: HonuaMapEmbedOptions;
}

export interface HonuaSceneIframeConfigureCommand {
  source: 'honua-scene-host';
  version: 1;
  type: 'honua-scene-configure';
  options: HonuaSceneEmbedOptions;
}

export type HonuaMapIframeCommand = HonuaMapIframeConfigureCommand;
export type HonuaSceneIframeCommand = HonuaSceneIframeConfigureCommand;

export type HonuaMapIframeMessageListener = (
  message: HonuaMapIframeMessage,
  event: MessageEvent<HonuaMapIframeMessage>,
) => void;

export type HonuaSceneIframeMessageListener = (
  message: HonuaSceneIframeMessage,
  event: MessageEvent<HonuaSceneIframeMessage>,
) => void;

export interface HonuaMapIframeMessageTarget {
  postMessage(message: HonuaMapIframeMessage, targetOrigin: string): void;
}

export interface HonuaSceneIframeMessageTarget {
  postMessage(message: HonuaSceneIframeMessage, targetOrigin: string): void;
}

export interface HonuaMapIframeCommandTarget {
  postMessage(message: HonuaMapIframeCommand, targetOrigin: string): void;
}

export interface HonuaSceneIframeCommandTarget {
  postMessage(message: HonuaSceneIframeCommand, targetOrigin: string): void;
}

export interface HonuaMapIframeMessageListenerOptions {
  window?: Window;
  origin?: string | readonly string[] | null;
  source?: MessageEventSource | null;
}

export interface HonuaMapIframeHydrateOptions {
  window?: Window;
  element?: HonuaMapElement | null;
  parent?: HonuaMapIframeMessageTarget | null;
  targetOrigin?: string | null;
}

export interface HonuaMapIframeRuntime {
  element: HonuaMapElement;
  config: HonuaMapIframeConfig;
  disconnect(): void;
}

export interface HonuaSceneIframeHydrateOptions {
  window?: Window;
  element?: HonuaSceneElement | null;
  parent?: HonuaSceneIframeMessageTarget | null;
  targetOrigin?: string | null;
}

export interface HonuaSceneIframeRuntime {
  element: HonuaSceneElement;
  config: HonuaSceneIframeConfig;
  disconnect(): void;
}

const MAP_FORWARDED_EVENTS = [
  'honua-map-ready',
  'honua-map-config-change',
  'honua-map-search',
  'honua-map-identify',
] as const;

const SCENE_FORWARDED_EVENTS = [
  'honua-scene-ready',
  'honua-scene-config-change',
  'honua-scene-load-error',
  'honua-scene-camera-change',
  'honua-scene-identify',
  'honua-scene-metadata-change',
  'honua-scene-metadata-error',
  'honua-scene-layer-change',
] as const;

export type HonuaMapIframeEventType = (typeof MAP_FORWARDED_EVENTS)[number];
export type HonuaSceneIframeEventType = (typeof SCENE_FORWARDED_EVENTS)[number];

const MAP_FORWARDED_EVENT_SET = new Set<string>(MAP_FORWARDED_EVENTS);
const SCENE_FORWARDED_EVENT_SET = new Set<string>(SCENE_FORWARDED_EVENTS);

const THEME_PARAMETERS: Array<[keyof HonuaMapThemeOptions, string]> = [
  ['accent', '--honua-map-accent'],
  ['background', '--honua-map-background'],
  ['foreground', '--honua-map-foreground'],
  ['muted', '--honua-map-muted'],
  ['surface', '--honua-map-surface'],
  ['border', '--honua-map-border'],
  ['fontFamily', '--honua-map-font-family'],
  ['controlSize', '--honua-map-control-size'],
];

const EMBED_OPTION_KEYS = [
  'serviceUrl',
  'layerIds',
  'apiKey',
  'center',
  'zoom',
  'bounds',
  'basemap',
  'interactive',
  'search',
  'identify',
  'attribution',
  'theme',
  'label',
] as const;

const SCENE_EMBED_OPTION_KEYS = [
  'tilesetUrl',
  'terrainUrl',
  'metadataUrl',
  'ionToken',
  'cesiumBaseUrl',
  'center',
  'height',
  'heading',
  'pitch',
  'roll',
  'theme',
  'autoload',
] as const;

const THEME_OPTION_KEYS = [
  'accent',
  'background',
  'foreground',
  'muted',
  'surface',
  'border',
  'fontFamily',
  'controlSize',
] as const;

export function parseHonuaMapIframeConfig(
  input: URL | URLSearchParams | string | null | undefined = defaultMapLocation(),
): HonuaMapIframeConfig {
  const params = searchParamsFrom(input, defaultMapLocation());
  const style = parseThemeOptions(params);
  const options: HonuaMapEmbedOptions = {
    serviceUrl: param(params, 'service-url'),
    layerIds: parseList(param(params, 'layer-ids')),
    apiKey: param(params, 'api-key'),
    center: parseCoordinate(param(params, 'center')),
    zoom: parseNumber(param(params, 'zoom')),
    bounds: parseBounds(param(params, 'bbox')),
    basemap: param(params, 'basemap'),
    interactive: parseBoolean(params, 'interactive'),
    search: parseBoolean(params, 'search'),
    identify: parseBoolean(params, 'identify'),
    attribution: param(params, 'attribution'),
    theme: parseTheme(param(params, 'theme')),
    label: param(params, 'label'),
    style: Object.keys(style).length > 0 ? style : undefined,
  };

  return {
    options,
    parentOrigin: parseParentOrigin(param(params, 'parent-origin')),
  };
}

export function parseHonuaSceneIframeConfig(
  input: URL | URLSearchParams | string | null | undefined = defaultSceneLocation(),
): HonuaSceneIframeConfig {
  const params = searchParamsFrom(input, defaultSceneLocation());
  const options: HonuaSceneEmbedOptions = {
    tilesetUrl: param(params, 'tileset-url'),
    terrainUrl: param(params, 'terrain-url'),
    metadataUrl: param(params, 'metadata-url'),
    ionToken: param(params, 'ion-token'),
    cesiumBaseUrl: param(params, 'cesium-base-url'),
    center: parseCoordinate(param(params, 'center')),
    height: parseNumber(param(params, 'height')),
    heading: parseNumber(param(params, 'heading')),
    pitch: parseNumber(param(params, 'pitch')),
    roll: parseNumber(param(params, 'roll')),
    theme: parseTheme(param(params, 'theme')),
    autoload: parseBoolean(params, 'autoload'),
  };

  return {
    options,
    parentOrigin: parseParentOrigin(param(params, 'parent-origin')),
  };
}

export function hydrateHonuaMapIframe(
  options: HonuaMapIframeHydrateOptions = {},
): HonuaMapIframeRuntime {
  const targetWindow = options.window ?? window;
  const document = targetWindow.document;
  defineHonuaMapElement();

  const config = parseHonuaMapIframeConfig(targetWindow.location.href);
  const element = options.element
    ?? document.querySelector<HonuaMapElement>('honua-map')
    ?? document.createElement('honua-map') as HonuaMapElement;

  applyHonuaMapOptions(element, config.options);
  applyFrameSizing(element);

  const parent = options.parent === undefined ? targetWindow.parent : options.parent;
  const targetOrigin = options.targetOrigin?.trim() || config.parentOrigin;
  const disconnect = parent && parent !== targetWindow && targetOrigin
    ? composeDisconnects(
      bridgeMapEvents(element, parent, targetOrigin),
      listenForConfigureCommands(targetWindow, element, config, parent, targetOrigin),
    )
    : () => {};

  if (!element.isConnected) {
    const mount = document.getElementById('honua-map-frame-root')
      ?? document.body
      ?? document.documentElement;
    mount.append(element);
  }

  return { element, config, disconnect };
}

export function hydrateHonuaSceneIframe(
  options: HonuaSceneIframeHydrateOptions = {},
): HonuaSceneIframeRuntime {
  const targetWindow = options.window ?? window;
  const document = targetWindow.document;
  defineHonuaSceneElement();

  const config = parseHonuaSceneIframeConfig(targetWindow.location.href);
  const element = options.element
    ?? document.querySelector<HonuaSceneElement>('honua-scene')
    ?? document.createElement('honua-scene') as HonuaSceneElement;

  applyHonuaSceneOptions(element, config.options);
  applyFrameSizing(element);

  const parent = options.parent === undefined ? targetWindow.parent : options.parent;
  const targetOrigin = options.targetOrigin?.trim() || config.parentOrigin;
  const disconnect = parent && parent !== targetWindow && targetOrigin
    ? composeDisconnects(
      bridgeSceneEvents(element, parent, targetOrigin),
      listenForSceneConfigureCommands(targetWindow, element, config, parent, targetOrigin),
    )
    : () => {};

  if (!element.isConnected) {
    const mount = document.getElementById('honua-scene-frame-root')
      ?? document.body
      ?? document.documentElement;
    mount.append(element);
  }

  return { element, config, disconnect };
}

export function postHonuaMapIframeConfigure(
  target: HonuaMapIframeCommandTarget,
  options: HonuaMapEmbedOptions,
  targetOrigin: string,
): void {
  target.postMessage({
    source: 'honua-map-host',
    version: 1,
    type: 'honua-map-configure',
    options,
  }, targetOrigin);
}

export function postHonuaSceneIframeConfigure(
  target: HonuaSceneIframeCommandTarget,
  options: HonuaSceneEmbedOptions,
  targetOrigin: string,
): void {
  target.postMessage({
    source: 'honua-scene-host',
    version: 1,
    type: 'honua-scene-configure',
    options,
  }, targetOrigin);
}

export function isHonuaMapIframeMessage(value: unknown): value is HonuaMapIframeMessage {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<HonuaMapIframeMessage>;
  return candidate.source === 'honua-map-iframe'
    && candidate.version === 1
    && typeof candidate.type === 'string'
    && MAP_FORWARDED_EVENT_SET.has(candidate.type);
}

export function isHonuaSceneIframeMessage(value: unknown): value is HonuaSceneIframeMessage {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<HonuaSceneIframeMessage>;
  return candidate.source === 'honua-scene-iframe'
    && candidate.version === 1
    && typeof candidate.type === 'string'
    && SCENE_FORWARDED_EVENT_SET.has(candidate.type);
}

export function isHonuaMapIframeCommand(value: unknown): value is HonuaMapIframeCommand {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<HonuaMapIframeCommand>;
  return candidate.source === 'honua-map-host'
    && candidate.version === 1
    && candidate.type === 'honua-map-configure'
    && typeof candidate.options === 'object'
    && candidate.options !== null;
}

export function isHonuaSceneIframeCommand(value: unknown): value is HonuaSceneIframeCommand {
  if (typeof value !== 'object' || value === null) {
    return false;
  }

  const candidate = value as Partial<HonuaSceneIframeCommand>;
  return candidate.source === 'honua-scene-host'
    && candidate.version === 1
    && candidate.type === 'honua-scene-configure'
    && typeof candidate.options === 'object'
    && candidate.options !== null;
}

export function addHonuaMapIframeMessageListener(
  listener: HonuaMapIframeMessageListener,
  options: HonuaMapIframeMessageListenerOptions = {},
): () => void {
  const targetWindow = options.window ?? defaultWindow();
  const expectedOrigins = normalizeExpectedOrigins(options.origin);
  const expectedSource = options.source;
  const messageListener = (event: MessageEvent) => {
    if (expectedSource !== undefined && event.source !== expectedSource) {
      return;
    }

    if (!originMatches(event.origin, expectedOrigins)) {
      return;
    }

    if (!isHonuaMapIframeMessage(event.data)) {
      return;
    }

    listener(event.data, event as MessageEvent<HonuaMapIframeMessage>);
  };

  targetWindow.addEventListener('message', messageListener);
  return () => targetWindow.removeEventListener('message', messageListener);
}

export function addHonuaSceneIframeMessageListener(
  listener: HonuaSceneIframeMessageListener,
  options: HonuaMapIframeMessageListenerOptions = {},
): () => void {
  const targetWindow = options.window ?? defaultWindow();
  const expectedOrigins = normalizeExpectedOrigins(options.origin);
  const expectedSource = options.source;
  const messageListener = (event: MessageEvent) => {
    if (expectedSource !== undefined && event.source !== expectedSource) {
      return;
    }

    if (!originMatches(event.origin, expectedOrigins)) {
      return;
    }

    if (!isHonuaSceneIframeMessage(event.data)) {
      return;
    }

    listener(event.data, event as MessageEvent<HonuaSceneIframeMessage>);
  };

  targetWindow.addEventListener('message', messageListener);
  return () => targetWindow.removeEventListener('message', messageListener);
}

function bridgeMapEvents(
  element: HonuaMapElement,
  parent: HonuaMapIframeMessageTarget,
  targetOrigin: string,
): () => void {
  const listeners = MAP_FORWARDED_EVENTS.map((type) => {
    const listener: EventListener = (event) => {
      parent.postMessage({
        source: 'honua-map-iframe',
        version: 1,
        type,
        detail: 'detail' in event ? event.detail : undefined,
      }, targetOrigin);
    };

    element.addEventListener(type, listener);
    return () => element.removeEventListener(type, listener);
  });

  return () => {
    for (const remove of listeners) {
      remove();
    }
  };
}

function bridgeSceneEvents(
  element: HonuaSceneElement,
  parent: HonuaSceneIframeMessageTarget,
  targetOrigin: string,
): () => void {
  const listeners = SCENE_FORWARDED_EVENTS.map((type) => {
    const listener: EventListener = (event) => {
      parent.postMessage({
        source: 'honua-scene-iframe',
        version: 1,
        type,
        detail: sanitizeSceneEventDetail(type, 'detail' in event ? event.detail : undefined),
      }, targetOrigin);
    };

    element.addEventListener(type, listener);
    return () => element.removeEventListener(type, listener);
  });

  return () => {
    for (const remove of listeners) {
      remove();
    }
  };
}

function listenForConfigureCommands(
  targetWindow: Window,
  element: HonuaMapElement,
  config: HonuaMapIframeConfig,
  parent: HonuaMapIframeMessageTarget,
  parentOrigin: string,
): () => void {
  const expectedOrigins = normalizeExpectedOrigins(parentOrigin);
  const messageListener = (event: MessageEvent) => {
    if (event.source !== parent) {
      return;
    }

    if (!originMatches(event.origin, expectedOrigins)) {
      return;
    }

    if (!isHonuaMapIframeCommand(event.data)) {
      return;
    }

    config.options = mergeHonuaMapOptions(config.options, event.data.options);
    applyHonuaMapOptions(element, event.data.options);
  };

  targetWindow.addEventListener('message', messageListener);
  return () => targetWindow.removeEventListener('message', messageListener);
}

function listenForSceneConfigureCommands(
  targetWindow: Window,
  element: HonuaSceneElement,
  config: HonuaSceneIframeConfig,
  parent: HonuaSceneIframeMessageTarget,
  parentOrigin: string,
): () => void {
  const expectedOrigins = normalizeExpectedOrigins(parentOrigin);
  const messageListener = (event: MessageEvent) => {
    if (event.source !== parent) {
      return;
    }

    if (!originMatches(event.origin, expectedOrigins)) {
      return;
    }

    if (!isHonuaSceneIframeCommand(event.data)) {
      return;
    }

    config.options = mergeHonuaSceneOptions(config.options, event.data.options);
    applyHonuaSceneOptions(element, config.options);
  };

  targetWindow.addEventListener('message', messageListener);
  return () => targetWindow.removeEventListener('message', messageListener);
}

function composeDisconnects(...disconnects: Array<() => void>): () => void {
  return () => {
    for (const disconnect of disconnects) {
      disconnect();
    }
  };
}

function mergeHonuaSceneOptions(
  current: HonuaSceneEmbedOptions,
  updates: HonuaSceneEmbedOptions,
): HonuaSceneEmbedOptions {
  const merged: HonuaSceneEmbedOptions = { ...current };
  for (const key of SCENE_EMBED_OPTION_KEYS) {
    assignDefinedSceneOption(merged, updates, key);
  }

  return merged;
}

function assignDefinedSceneOption<TKey extends keyof HonuaSceneEmbedOptions>(
  target: HonuaSceneEmbedOptions,
  source: HonuaSceneEmbedOptions,
  key: TKey,
): void {
  const value = source[key];
  if (value !== undefined) {
    target[key] = value;
  }
}

function mergeHonuaMapOptions(
  current: HonuaMapEmbedOptions,
  updates: HonuaMapEmbedOptions,
): HonuaMapEmbedOptions {
  const merged: HonuaMapEmbedOptions = { ...current };
  for (const key of EMBED_OPTION_KEYS) {
    assignDefinedOption(merged, updates, key);
  }

  if (updates.style !== undefined) {
    merged.style = mergeHonuaMapThemeOptions(current.style, updates.style);
  }

  return merged;
}

function assignDefinedOption<TKey extends keyof HonuaMapEmbedOptions>(
  target: HonuaMapEmbedOptions,
  source: HonuaMapEmbedOptions,
  key: TKey,
): void {
  const value = source[key];
  if (value !== undefined) {
    target[key] = value;
  }
}

function mergeHonuaMapThemeOptions(
  current: HonuaMapThemeOptions | null | undefined,
  updates: HonuaMapThemeOptions | null,
): HonuaMapThemeOptions | null {
  if (updates === null) {
    return null;
  }

  const merged: HonuaMapThemeOptions = current ? { ...current } : {};
  for (const key of THEME_OPTION_KEYS) {
    const value = updates[key];
    if (value !== undefined) {
      merged[key] = value;
    }
  }

  return merged;
}

function sanitizeSceneEventDetail(type: HonuaSceneIframeEventType, detail: unknown): unknown {
  if (type === 'honua-scene-ready' && isObject(detail)) {
    return {
      config: cloneableValue(detail.config),
      widget: null,
      tileset: null,
    };
  }

  if (type === 'honua-scene-load-error' && isObject(detail)) {
    return {
      config: cloneableValue(detail.config),
      source: cloneableValue(detail.source),
      code: cloneableValue(detail.code),
      message: cloneableValue(detail.message),
      error: sanitizeError(detail.error),
    };
  }

  if (type === 'honua-scene-identify' && isObject(detail)) {
    return {
      config: cloneableValue(detail.config),
      x: cloneableValue(detail.x),
      y: cloneableValue(detail.y),
      picked: cloneableValue(detail.picked),
      position: cloneableValue(detail.position),
    };
  }

  return cloneableValue(detail);
}

function sanitizeError(value: unknown): unknown {
  if (value instanceof Error) {
    return {
      name: value.name,
      message: value.message,
    };
  }

  return cloneableValue(value);
}

function cloneableValue(value: unknown, seen = new WeakSet<object>()): unknown {
  if (
    value === null
    || typeof value === 'string'
    || typeof value === 'number'
    || typeof value === 'boolean'
  ) {
    return value;
  }

  if (typeof value === 'bigint') {
    return value.toString();
  }

  if (typeof value !== 'object') {
    return undefined;
  }

  if (seen.has(value)) {
    return undefined;
  }
  seen.add(value);

  if (value instanceof Date) {
    return value.toISOString();
  }

  if (Array.isArray(value)) {
    return value.map((item) => cloneableValue(item, seen));
  }

  const output: Record<string, unknown> = {};
  for (const [key, item] of Object.entries(value)) {
    const sanitized = cloneableValue(item, seen);
    if (sanitized !== undefined) {
      output[key] = sanitized;
    }
  }

  return output;
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}

function defaultWindow(): Window {
  if (typeof window === 'undefined') {
    throw new Error('Honua map iframe message listeners require a browser Window.');
  }

  return window;
}

function normalizeExpectedOrigins(origin: string | readonly string[] | null | undefined): Set<string> | null {
  if (origin === undefined || origin === null) {
    return null;
  }

  const values = Array.isArray(origin) ? origin : [origin];
  const normalized = new Set<string>();
  for (const value of values) {
    const parsed = parseParentOrigin(value);
    if (parsed) {
      normalized.add(parsed);
    }
  }

  return normalized;
}

function originMatches(origin: string, expectedOrigins: Set<string> | null): boolean {
  if (expectedOrigins === null || expectedOrigins.has('*')) {
    return true;
  }

  return expectedOrigins.has(origin);
}

function applyFrameSizing(element: HTMLElement): void {
  element.style.display = 'block';
  element.style.width = '100%';
  element.style.height = '100%';
  element.style.minHeight = '100vh';
}

function searchParamsFrom(
  input: URL | URLSearchParams | string | null | undefined,
  fallbackUrl: string,
): URLSearchParams {
  if (input instanceof URLSearchParams) {
    return new URLSearchParams(input);
  }

  if (input instanceof URL) {
    return new URLSearchParams(input.searchParams);
  }

  const value = input ?? fallbackUrl;
  return new URL(value, 'https://cdn.honua.dev').searchParams;
}

function defaultMapLocation(): string {
  if (typeof location === 'undefined') {
    return 'https://cdn.honua.dev/embed/map.html';
  }

  return location.href;
}

function defaultSceneLocation(): string {
  if (typeof location === 'undefined') {
    return 'https://cdn.honua.dev/embed/scene.html';
  }

  return location.href;
}

function param(params: URLSearchParams, name: string): string | undefined {
  const value = params.get(name)?.trim();
  return value ? value : undefined;
}

function parseList(value: string | undefined): string[] | undefined {
  if (value === undefined) {
    return undefined;
  }

  const items = value
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);
  return items.length > 0 ? items : undefined;
}

function parseCoordinate(value: string | undefined): HonuaMapEmbedOptions['center'] {
  const [latitude, longitude] = parseNumberList(value, 2);
  if (latitude === undefined || longitude === undefined) {
    return undefined;
  }

  return { latitude, longitude };
}

function parseBounds(value: string | undefined): HonuaMapEmbedOptions['bounds'] {
  const [minLongitude, minLatitude, maxLongitude, maxLatitude] = parseNumberList(value, 4);
  if (
    minLongitude === undefined
    || minLatitude === undefined
    || maxLongitude === undefined
    || maxLatitude === undefined
  ) {
    return undefined;
  }

  return { minLongitude, minLatitude, maxLongitude, maxLatitude };
}

function parseNumberList(value: string | undefined, expectedLength: number): number[] {
  const values = parseList(value);
  if (values?.length !== expectedLength) {
    return [];
  }

  const parsed = values.map(Number);
  return parsed.every(Number.isFinite) ? parsed : [];
}

function parseNumber(value: string | undefined): number | undefined {
  if (value === undefined) {
    return undefined;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : undefined;
}

function parseBoolean(params: URLSearchParams, name: string): boolean | undefined {
  if (!params.has(name)) {
    return undefined;
  }

  const value = params.get(name)?.trim() ?? '';
  if (value === '') {
    return true;
  }

  return !['false', '0', 'no'].includes(value.toLowerCase());
}

function parseTheme(value: string | undefined): HonuaMapEmbedOptions['theme'] {
  return value === 'dark' || value === 'light' ? value : undefined;
}

function parseThemeOptions(params: URLSearchParams): HonuaMapThemeOptions {
  const style: HonuaMapThemeOptions = {};
  for (const [key, parameter] of THEME_PARAMETERS) {
    const value = param(params, parameter);
    if (value !== undefined) {
      style[key] = value;
    }
  }

  return style;
}

function parseParentOrigin(value: string | undefined): string | null {
  if (value === undefined || value === '*') {
    return '*';
  }

  try {
    return new URL(value).origin;
  } catch {
    return null;
  }
}
