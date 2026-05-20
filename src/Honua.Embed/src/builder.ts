import type { HonuaMapBounds, HonuaMapCoordinate } from './map';
import {
  applyHonuaMapOptions,
  createHonuaMapSnippet,
  type HonuaMapEmbedOptions,
  type HonuaMapSnippetOptions,
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
  snippetOptions: HonuaMapSnippetOptions = {},
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
      snippet = createHonuaMapSnippet(options, snippetOptions);
    } catch (error) {
      issues.push({
        severity: 'error',
        code: 'invalid-element-name',
        field: 'elementName',
        message: error instanceof Error ? error.message : 'Invalid custom element name.',
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
  snippetOptions: HonuaMapSnippetOptions = {},
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
  snippetOptions: HonuaMapSnippetOptions,
  issues: HonuaMapBuilderIssue[],
): void {
  validateServiceUrl(options.serviceUrl, issues);

  if (options.apiKey && !snippetOptions.includeCredentials) {
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
