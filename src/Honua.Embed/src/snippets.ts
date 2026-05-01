import type { HonuaMapBounds, HonuaMapCoordinate } from './map';

export interface HonuaMapThemeOptions {
  accent?: string | null;
  background?: string | null;
  foreground?: string | null;
  muted?: string | null;
  surface?: string | null;
  border?: string | null;
  fontFamily?: string | null;
  controlSize?: string | null;
}

export interface HonuaMapEmbedOptions {
  serviceUrl?: string | null;
  layerIds?: readonly string[] | null;
  apiKey?: string | null;
  center?: HonuaMapCoordinate | null;
  zoom?: number | null;
  bounds?: HonuaMapBounds | null;
  basemap?: string | null;
  interactive?: boolean | null;
  search?: boolean | null;
  identify?: boolean | null;
  attribution?: string | null;
  theme?: 'light' | 'dark' | null;
  label?: string | null;
  style?: HonuaMapThemeOptions | null;
}

export interface HonuaMapSnippetOptions {
  packageName?: string;
  elementName?: string;
  includeScript?: boolean;
  includeCredentials?: boolean;
  indent?: string;
}

export function applyHonuaMapOptions(
  element: HTMLElement,
  options: HonuaMapEmbedOptions,
): void {
  setOptionalAttribute(element, 'service-url', options.serviceUrl);
  setOptionalAttribute(element, 'layer-ids', serializeList(options.layerIds));
  setOptionalAttribute(element, 'api-key', options.apiKey);
  setOptionalAttribute(element, 'center', serializeCoordinate(options.center));
  setOptionalAttribute(element, 'zoom', serializeNumber(options.zoom));
  setOptionalAttribute(element, 'bbox', serializeBounds(options.bounds));
  setOptionalAttribute(element, 'basemap', options.basemap);
  setBooleanAttribute(element, 'interactive', options.interactive);
  setBooleanAttribute(element, 'search', options.search);
  setBooleanAttribute(element, 'identify', options.identify);
  setOptionalAttribute(element, 'attribution', options.attribution);
  setOptionalAttribute(element, 'theme', options.theme);
  setOptionalAttribute(element, 'label', options.label);

  if (options.style !== undefined) {
    applyHonuaMapTheme(element, options.style);
  }
}

export function applyHonuaMapTheme(element: HTMLElement, theme: HonuaMapThemeOptions | null): void {
  for (const [property, value] of Object.entries(mapThemeVariables(theme))) {
    if (value === undefined) {
      continue;
    }

    if (value === null) {
      element.style.removeProperty(property);
      continue;
    }

    element.style.setProperty(property, value);
  }
}

export function createHonuaMapSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapSnippetOptions = {},
): string {
  const elementName = snippetOptions.elementName ?? 'honua-map';
  assertCustomElementName(elementName);

  const includeScript = snippetOptions.includeScript ?? true;
  const packageName = snippetOptions.packageName ?? '@honua-io/embed';
  const indent = snippetOptions.indent ?? '  ';
  const element = createElementMarkup(elementName, options, {
    includeCredentials: snippetOptions.includeCredentials ?? false,
    indent,
  });

  if (!includeScript) {
    return element;
  }

  const script = elementName === 'honua-map'
    ? [
      '<script type="module">',
      `${indent}import '${escapeJsString(packageName)}';`,
      '</script>',
    ].join('\n')
    : [
      '<script type="module">',
      `${indent}import { defineHonuaMapElement } from '${escapeJsString(packageName)}';`,
      `${indent}defineHonuaMapElement('${escapeJsString(elementName)}');`,
      '</script>',
    ].join('\n');

  return `${script}\n\n${element}`;
}

function createElementMarkup(
  elementName: string,
  options: HonuaMapEmbedOptions,
  config: Required<Pick<HonuaMapSnippetOptions, 'includeCredentials' | 'indent'>>,
): string {
  const attributes = mapAttributes(options, config.includeCredentials);
  const style = serializeTheme(options.style);
  if (style) {
    attributes.push(['style', style]);
  }

  if (attributes.length === 0) {
    return `<${elementName}></${elementName}>`;
  }

  const lines = attributes.map(([name, value]) => value === true
    ? `${config.indent}${name}`
    : `${config.indent}${name}="${escapeHtmlAttribute(value)}"`);

  return `<${elementName}\n${lines.join('\n')}>\n</${elementName}>`;
}

function mapAttributes(
  options: HonuaMapEmbedOptions,
  includeCredentials: boolean,
): Array<[string, string | true]> {
  return [
    ['service-url', options.serviceUrl],
    ['layer-ids', serializeList(options.layerIds)],
    ['api-key', includeCredentials ? options.apiKey : undefined],
    ['center', serializeCoordinate(options.center)],
    ['zoom', serializeNumber(options.zoom)],
    ['bbox', serializeBounds(options.bounds)],
    ['basemap', options.basemap],
    ['interactive', serializeBoolean(options.interactive)],
    ['search', serializeBoolean(options.search)],
    ['identify', serializeBoolean(options.identify)],
    ['attribution', options.attribution],
    ['theme', options.theme],
    ['label', options.label],
  ].filter((entry): entry is [string, string | true] => {
    const value = entry[1];
    return value !== undefined && value !== null && value !== '';
  });
}

function serializeTheme(theme: HonuaMapThemeOptions | null | undefined): string | null {
  const declarations = Object.entries(mapThemeVariables(theme))
    .filter((entry): entry is [string, string] => typeof entry[1] === 'string')
    .map(([property, value]) => `${property}: ${value}`);

  return declarations.length === 0 ? null : declarations.join('; ');
}

function mapThemeVariables(theme: HonuaMapThemeOptions | null | undefined): Record<string, string | null | undefined> {
  if (theme === null) {
    return {
      '--honua-map-accent': null,
      '--honua-map-background': null,
      '--honua-map-foreground': null,
      '--honua-map-muted': null,
      '--honua-map-surface': null,
      '--honua-map-border': null,
      '--honua-map-font-family': null,
      '--honua-map-control-size': null,
    };
  }

  return {
    '--honua-map-accent': theme?.accent,
    '--honua-map-background': theme?.background,
    '--honua-map-foreground': theme?.foreground,
    '--honua-map-muted': theme?.muted,
    '--honua-map-surface': theme?.surface,
    '--honua-map-border': theme?.border,
    '--honua-map-font-family': theme?.fontFamily,
    '--honua-map-control-size': theme?.controlSize,
  };
}

function setOptionalAttribute(element: HTMLElement, name: string, value: string | null | undefined): void {
  if (value === undefined) {
    return;
  }

  if (value === null || value === '') {
    element.removeAttribute(name);
    return;
  }

  element.setAttribute(name, value);
}

function setBooleanAttribute(element: HTMLElement, name: string, value: boolean | null | undefined): void {
  if (value === undefined) {
    return;
  }

  if (value) {
    element.setAttribute(name, '');
    return;
  }

  element.removeAttribute(name);
}

function serializeBoolean(value: boolean | null | undefined): true | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  return value ? true : null;
}

function serializeList(value: readonly string[] | null | undefined): string | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  const serialized = value
    .map((item) => item.trim())
    .filter(Boolean)
    .join(',');
  return serialized || null;
}

function serializeCoordinate(value: HonuaMapCoordinate | null | undefined): string | null | undefined {
  if (value === undefined || value === null) {
    return value;
  }

  return `${value.latitude},${value.longitude}`;
}

function serializeBounds(value: HonuaMapBounds | null | undefined): string | null | undefined {
  if (value === undefined || value === null) {
    return value;
  }

  return `${value.minLongitude},${value.minLatitude},${value.maxLongitude},${value.maxLatitude}`;
}

function serializeNumber(value: number | null | undefined): string | null | undefined {
  if (value === undefined || value === null) {
    return value;
  }

  return String(value);
}

function escapeHtmlAttribute(value: string | true): string {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('"', '&quot;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;');
}

function escapeJsString(value: string): string {
  return value
    .replaceAll('\\', '\\\\')
    .replaceAll('\'', '\\\'')
    .replaceAll('<', '\\x3C')
    .replaceAll('\n', '\\n')
    .replaceAll('\r', '\\r');
}

function assertCustomElementName(name: string): void {
  if (!/^[a-z][.0-9_a-z-]*-[.0-9_a-z-]*$/.test(name)) {
    throw new Error(`Invalid custom element name: ${name}`);
  }
}
