import type { HonuaMapBounds, HonuaMapCoordinate } from './map';
import {
  applyHonuaMapOptions,
  createHonuaMapEmbedBuilderSnippet,
  type HonuaMapEmbedBuilderSnippetOptions,
  type HonuaMapEmbedOptions,
  type HonuaMapThemeOptions,
} from './snippets';

export type HonuaMapBuilderIssueSeverity = 'error' | 'warning';

export interface HonuaMapBuilderLayerOption {
  id: string;
  label?: string | null;
  description?: string | null;
  defaultSelected?: boolean | null;
  disabled?: boolean | null;
}

export interface HonuaMapBuilderInput {
  serviceUrl?: string | null;
  availableLayers?: readonly HonuaMapBuilderLayerOption[] | null;
  selectedLayerIds?: readonly string[] | null;
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

export interface HonuaMapBuilderIssue {
  severity: HonuaMapBuilderIssueSeverity;
  code: string;
  field: string;
  message: string;
}

export interface HonuaMapBuilderState {
  options: HonuaMapEmbedOptions;
  availableLayers: HonuaMapBuilderLayerOption[];
  selectedLayers: HonuaMapBuilderLayerOption[];
  selectedLayerIds: string[];
  issues: HonuaMapBuilderIssue[];
  canGenerateSnippet: boolean;
  snippet: string | null;
}

export function createHonuaMapBuilderState(
  input: HonuaMapBuilderInput,
  snippetOptions: HonuaMapEmbedBuilderSnippetOptions = {},
): HonuaMapBuilderState {
  const issues: HonuaMapBuilderIssue[] = [];
  const availableLayers = normalizeLayerOptions(input.availableLayers, issues);
  const selectedLayerIds = normalizeSelectedLayerIds(input, availableLayers, issues);
  const selectedLayers = selectedLayerIds.map((id) => {
    return availableLayers.find((layer) => layer.id === id) ?? { id };
  });
  const options: HonuaMapEmbedOptions = {
    serviceUrl: normalizeString(input.serviceUrl),
    layerIds: selectedLayerIds,
    apiKey: normalizeString(input.apiKey),
    center: input.center ?? null,
    zoom: input.zoom ?? null,
    bounds: input.bounds ?? null,
    basemap: normalizeString(input.basemap),
    interactive: input.interactive ?? null,
    search: input.search ?? null,
    identify: input.identify ?? null,
    attribution: normalizeString(input.attribution),
    theme: input.theme ?? null,
    label: normalizeString(input.label),
    style: input.style ?? null,
  };

  validateOptions(options, snippetOptions, issues);

  let snippet: string | null = null;
  if (!hasErrors(issues)) {
    try {
      snippet = createHonuaMapEmbedBuilderSnippet(options, snippetOptions);
    } catch (error) {
      issues.push({
        severity: 'error',
        code: 'invalid-snippet-options',
        field: 'snippetOptions',
        message: error instanceof Error ? error.message : 'Invalid snippet options.',
      });
    }
  }

  return {
    options,
    availableLayers,
    selectedLayers,
    selectedLayerIds,
    issues,
    canGenerateSnippet: snippet !== null && !hasErrors(issues),
    snippet,
  };
}

export function createHonuaMapBuilderSnippet(
  input: HonuaMapBuilderInput,
  snippetOptions: HonuaMapEmbedBuilderSnippetOptions = {},
): string {
  const state = createHonuaMapBuilderState(input, snippetOptions);
  if (!state.canGenerateSnippet || state.snippet === null) {
    const summary = state.issues
      .filter((issue) => issue.severity === 'error')
      .map((issue) => issue.message)
      .join(' ');
    throw new Error(`Cannot create Honua map snippet. ${summary}`.trim());
  }

  return state.snippet;
}

export function applyHonuaMapBuilderState(
  element: HTMLElement,
  state: HonuaMapBuilderState,
): void {
  applyHonuaMapOptions(element, state.options);
}

function normalizeLayerOptions(
  layers: readonly HonuaMapBuilderLayerOption[] | null | undefined,
  issues: HonuaMapBuilderIssue[],
): HonuaMapBuilderLayerOption[] {
  const normalized: HonuaMapBuilderLayerOption[] = [];
  const seen = new Set<string>();

  for (const layer of layers ?? []) {
    const id = layer.id.trim();
    if (!id) {
      issues.push({
        severity: 'warning',
        code: 'empty-layer-id',
        field: 'availableLayers',
        message: 'A layer option with an empty id was ignored.',
      });
      continue;
    }

    if (seen.has(id)) {
      issues.push({
        severity: 'warning',
        code: 'duplicate-layer-id',
        field: 'availableLayers',
        message: `Duplicate layer option "${id}" was ignored.`,
      });
      continue;
    }

    seen.add(id);
    normalized.push({
      ...layer,
      id,
      label: normalizeString(layer.label),
      description: normalizeString(layer.description),
    });
  }

  return normalized;
}

function normalizeSelectedLayerIds(
  input: HonuaMapBuilderInput,
  availableLayers: readonly HonuaMapBuilderLayerOption[],
  issues: HonuaMapBuilderIssue[],
): string[] {
  const selectedIds = input.selectedLayerIds ??
    input.layerIds ??
    availableLayers
      .filter((layer) => layer.defaultSelected)
      .map((layer) => layer.id);
  const requested = normalizeStringList(selectedIds);
  const availableById = new Map(availableLayers.map((layer) => [layer.id, layer]));
  const selected: string[] = [];
  const seen = new Set<string>();

  for (const id of requested) {
    if (seen.has(id)) {
      continue;
    }

    seen.add(id);
    const layer = availableById.get(id);
    if (availableLayers.length > 0 && !layer) {
      issues.push({
        severity: 'warning',
        code: 'unavailable-layer',
        field: 'selectedLayerIds',
        message: `Layer "${id}" is not in the available layer list and was ignored.`,
      });
      continue;
    }

    if (layer?.disabled) {
      issues.push({
        severity: 'warning',
        code: 'disabled-layer',
        field: 'selectedLayerIds',
        message: `Layer "${id}" is disabled and was ignored.`,
      });
      continue;
    }

    selected.push(id);
  }

  return selected;
}

function validateOptions(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapEmbedBuilderSnippetOptions,
  issues: HonuaMapBuilderIssue[],
): void {
  validateServiceUrl(options.serviceUrl, issues);

  const apiKeyValid = validateApiKeyFormat(options.apiKey, issues);

  if (options.apiKey && apiKeyValid && !snippetIncludesCredentials(snippetOptions)) {
    issues.push({
      severity: 'warning',
      code: 'credentials-omitted',
      field: 'apiKey',
      message: 'The API key is available for preview but omitted from generated snippets by default.',
    });
  }

  if (options.zoom !== null && options.zoom !== undefined) {
    if (!Number.isFinite(options.zoom) || options.zoom < 0 || options.zoom > 24) {
      issues.push({
        severity: 'error',
        code: 'invalid-zoom',
        field: 'zoom',
        message: 'Zoom must be a finite value from 0 through 24.',
      });
    }
  }

  if (options.center) {
    validateCoordinate(options.center, 'center', issues);
  }

  if (options.bounds) {
    validateBounds(options.bounds, issues);
  }
}

function validateServiceUrl(
  serviceUrl: string | null | undefined,
  issues: HonuaMapBuilderIssue[],
): void {
  if (!serviceUrl) {
    issues.push({
      severity: 'error',
      code: 'missing-service-url',
      field: 'serviceUrl',
      message: 'A service URL is required before generating an embed snippet.',
    });
    return;
  }

  let parsed: URL;
  try {
    parsed = new URL(serviceUrl);
  } catch {
    issues.push({
      severity: 'error',
      code: 'invalid-service-url',
      field: 'serviceUrl',
      message: 'Service URL must be an absolute URL.',
    });
    return;
  }

  if (parsed.protocol !== 'https:' && parsed.protocol !== 'http:') {
    issues.push({
      severity: 'error',
      code: 'unsupported-service-url-scheme',
      field: 'serviceUrl',
      message: 'Service URL must use HTTP or HTTPS.',
    });
    return;
  }

  if (parsed.protocol === 'http:' && parsed.hostname !== 'localhost' && parsed.hostname !== '127.0.0.1') {
    issues.push({
      severity: 'warning',
      code: 'insecure-service-url',
      field: 'serviceUrl',
      message: 'Non-local embed service URLs should use HTTPS.',
    });
  }
}

const API_KEY_MIN_LENGTH = 8;
const API_KEY_MAX_LENGTH = 256;
const API_KEY_ALLOWED_PATTERN = /^[A-Za-z0-9._\-:]+$/;
const API_KEY_HEADER_PREFIX_PATTERN = /^(bearer|basic|token|apikey|api[-_]key)\s/i;

/**
 * Validates the shape of an embed API key without contacting any server.
 *
 * Server-issued keys are opaque, so this performs only format checks that catch
 * common copy/paste mistakes (whitespace, surrounding quotes, accidentally
 * pasted `Authorization` header prefixes, length, and unexpected characters).
 *
 * Returns `true` when the key is absent or appears well-formed and `false`
 * when an error-level issue was appended.
 */
function validateApiKeyFormat(
  apiKey: string | null | undefined,
  issues: HonuaMapBuilderIssue[],
): boolean {
  if (!apiKey) {
    return true;
  }

  if (API_KEY_HEADER_PREFIX_PATTERN.test(apiKey)) {
    issues.push({
      severity: 'error',
      code: 'invalid-api-key',
      field: 'apiKey',
      message: 'API key must be the raw key value, without an "Authorization" header prefix.',
    });
    return false;
  }

  if (/\s/.test(apiKey)) {
    issues.push({
      severity: 'error',
      code: 'invalid-api-key',
      field: 'apiKey',
      message: 'API key must not contain whitespace.',
    });
    return false;
  }

  if (
    (apiKey.startsWith('"') && apiKey.endsWith('"')) ||
    (apiKey.startsWith("'") && apiKey.endsWith("'"))
  ) {
    issues.push({
      severity: 'error',
      code: 'invalid-api-key',
      field: 'apiKey',
      message: 'API key must not be wrapped in quotes.',
    });
    return false;
  }

  if (apiKey.length < API_KEY_MIN_LENGTH || apiKey.length > API_KEY_MAX_LENGTH) {
    issues.push({
      severity: 'error',
      code: 'invalid-api-key',
      field: 'apiKey',
      message: `API key must be between ${API_KEY_MIN_LENGTH} and ${API_KEY_MAX_LENGTH} characters.`,
    });
    return false;
  }

  if (!API_KEY_ALLOWED_PATTERN.test(apiKey)) {
    issues.push({
      severity: 'error',
      code: 'invalid-api-key',
      field: 'apiKey',
      message: 'API key may only contain letters, digits, and the characters . _ - :',
    });
    return false;
  }

  return true;
}

function validateCoordinate(
  coordinate: HonuaMapCoordinate,
  field: string,
  issues: HonuaMapBuilderIssue[],
): void {
  if (!isValidLatitude(coordinate.latitude) || !isValidLongitude(coordinate.longitude)) {
    issues.push({
      severity: 'error',
      code: 'invalid-coordinate',
      field,
      message: 'Center must contain latitude from -90 through 90 and longitude from -180 through 180.',
    });
  }
}

function validateBounds(
  bounds: HonuaMapBounds,
  issues: HonuaMapBuilderIssue[],
): void {
  if (
    !isValidLongitude(bounds.minLongitude) ||
    !isValidLongitude(bounds.maxLongitude) ||
    !isValidLatitude(bounds.minLatitude) ||
    !isValidLatitude(bounds.maxLatitude) ||
    bounds.minLongitude >= bounds.maxLongitude ||
    bounds.minLatitude >= bounds.maxLatitude
  ) {
    issues.push({
      severity: 'error',
      code: 'invalid-bounds',
      field: 'bounds',
      message: 'Bounds must be ordered as minLon,minLat,maxLon,maxLat within valid coordinate ranges.',
    });
  }
}

function snippetIncludesCredentials(snippetOptions: HonuaMapEmbedBuilderSnippetOptions): boolean {
  switch (snippetOptions.target ?? 'web-component') {
    case 'web-component':
      return snippetOptions.webComponent?.includeCredentials === true;
    case 'cdn':
      return snippetOptions.cdn?.includeCredentials === true;
    case 'iframe':
      return snippetOptions.iframe?.includeCredentials === true;
    case 'react':
      return snippetOptions.react?.includeCredentials === true;
    case 'react-iframe':
      return snippetOptions.reactIframe?.includeCredentials === true;
    case 'vue':
      return snippetOptions.vue?.includeCredentials === true;
    case 'vue-iframe':
      return snippetOptions.vueIframe?.includeCredentials === true;
    case 'angular':
      return snippetOptions.angular?.includeCredentials === true;
    case 'angular-iframe':
      return snippetOptions.angularIframe?.includeCredentials === true;
    default:
      return false;
  }
}

function normalizeString(value: string | null | undefined): string | null {
  if (value === undefined || value === null) {
    return null;
  }

  const normalized = value.trim();
  return normalized || null;
}

function normalizeStringList(value: readonly string[] | null | undefined): string[] {
  return (value ?? [])
    .map((item) => item.trim())
    .filter(Boolean);
}

function isValidLatitude(value: number): boolean {
  return Number.isFinite(value) && value >= -90 && value <= 90;
}

function isValidLongitude(value: number): boolean {
  return Number.isFinite(value) && value >= -180 && value <= 180;
}

function hasErrors(issues: readonly HonuaMapBuilderIssue[]): boolean {
  return issues.some((issue) => issue.severity === 'error');
}
