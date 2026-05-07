import { defineHonuaMapElement, type HonuaMapElement } from './map';
import {
  applyHonuaMapOptions,
  type HonuaMapEmbedOptions,
  type HonuaMapThemeOptions,
} from './snippets';

export interface HonuaMapIframeConfig {
  options: HonuaMapEmbedOptions;
  parentOrigin: string;
}

export interface HonuaMapIframeMessage {
  source: 'honua-map-iframe';
  version: 1;
  type: string;
  detail: unknown;
}

export interface HonuaMapIframeMessageTarget {
  postMessage(message: HonuaMapIframeMessage, targetOrigin: string): void;
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

const FORWARDED_EVENTS = [
  'honua-map-ready',
  'honua-map-config-change',
  'honua-map-search',
  'honua-map-identify',
] as const;

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

export function parseHonuaMapIframeConfig(
  input: URL | URLSearchParams | string | null | undefined = defaultLocation(),
): HonuaMapIframeConfig {
  const params = searchParamsFrom(input);
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
  const disconnect = parent && parent !== targetWindow
    ? bridgeMapEvents(element, parent, targetOrigin)
    : () => {};

  if (!element.isConnected) {
    const mount = document.getElementById('honua-map-frame-root')
      ?? document.body
      ?? document.documentElement;
    mount.append(element);
  }

  return { element, config, disconnect };
}

function bridgeMapEvents(
  element: HonuaMapElement,
  parent: HonuaMapIframeMessageTarget,
  targetOrigin: string,
): () => void {
  const listeners = FORWARDED_EVENTS.map((type) => {
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

function applyFrameSizing(element: HTMLElement): void {
  element.style.display = 'block';
  element.style.width = '100%';
  element.style.height = '100%';
  element.style.minHeight = '100vh';
}

function searchParamsFrom(
  input: URL | URLSearchParams | string | null | undefined,
): URLSearchParams {
  if (input instanceof URLSearchParams) {
    return new URLSearchParams(input);
  }

  if (input instanceof URL) {
    return new URLSearchParams(input.searchParams);
  }

  const value = input ?? 'https://cdn.honua.dev/embed/map.html';
  return new URL(value, 'https://cdn.honua.dev').searchParams;
}

function defaultLocation(): string {
  if (typeof location === 'undefined') {
    return 'https://cdn.honua.dev/embed/map.html';
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

function parseParentOrigin(value: string | undefined): string {
  if (value === undefined || value === '*') {
    return '*';
  }

  try {
    return new URL(value).origin;
  } catch {
    return '*';
  }
}
