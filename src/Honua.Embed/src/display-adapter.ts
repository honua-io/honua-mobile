import type { Layer } from '@deck.gl/core';
import { GeoJsonLayer, type GeoJsonLayerProps } from '@deck.gl/layers';
import { MapboxOverlay, type MapboxOverlayProps } from '@deck.gl/mapbox';
import type {
  Feature,
  FeatureCollection,
  GeoJsonProperties,
  Geometry,
} from 'geojson';

export const HONUA_FEATURE_METADATA_PROPERTY = '__honua';

export interface HonuaDisplayBounds {
  minLongitude: number;
  minLatitude: number;
  maxLongitude: number;
  maxLatitude: number;
}

export interface HonuaDisplaySpatialReference {
  authority?: string;
  code?: string | number;
  wkid?: number;
  latestWkid?: number;
  wkt?: string;
}

export interface HonuaDisplaySourceDescriptor {
  id: string;
  title?: string;
  geometryType?: string;
  extent?: HonuaDisplayBounds | null;
  spatialReference?: HonuaDisplaySpatialReference | null;
  schema?: unknown;
  queryCapabilities?: unknown;
  tileUrl?: string | null;
  feedUrl?: string | null;
}

export interface HonuaFeaturePickingMetadata {
  [key: string]: unknown;
  sourceId?: string;
  source?: HonuaDisplaySourceDescriptor | null;
  featureId?: string | number;
  objectId?: string | number;
  streamEventType?: string;
  streamSequence?: string | number;
}

export type HonuaGeoJsonProperties = Record<string, unknown> & {
  [HONUA_FEATURE_METADATA_PROPERTY]?: HonuaFeaturePickingMetadata;
};

export interface HonuaDisplayDataMetadata {
  sourceId?: string;
  source?: HonuaDisplaySourceDescriptor | null;
  schema?: unknown;
  extent?: HonuaDisplayBounds | null;
  spatialReference?: HonuaDisplaySpatialReference | null;
  geometryType?: string | null;
  queryCapabilities?: unknown;
  tileUrl?: string | null;
  feedUrl?: string | null;
  providerName?: string | null;
  objectIdFieldName?: string | null;
  nextPageToken?: string | null;
  totalCount?: number | null;
  numberReturned?: number | null;
  exceededTransferLimit?: boolean | null;
  stream?: HonuaFeatureStreamMetadata | null;
}

export interface HonuaDisplayFeatureCollection
  extends FeatureCollection<Geometry, HonuaGeoJsonProperties> {
  honua?: HonuaDisplayDataMetadata;
}

export interface HonuaFeatureRecord {
  id?: string | number;
  objectId?: string | number;
  geometry?: Geometry | null;
  geoJson?: Geometry | Feature<Geometry, GeoJsonProperties> | null;
  geoJsonGeometry?: Geometry | null;
  attributes?: Record<string, unknown> | null;
  properties?: Record<string, unknown> | null;
}

export interface HonuaFeatureQueryResult {
  providerName?: string | null;
  source?: HonuaDisplaySourceDescriptor | null;
  objectIdFieldName?: string | null;
  geometryType?: string | null;
  fields?: unknown;
  features?: HonuaFeatureRecord[] | null;
  items?: HonuaFeatureRecord[] | null;
  spatialReference?: HonuaDisplaySpatialReference | null;
  nextPageToken?: string | null;
  totalCount?: number | null;
  count?: number | null;
  numberReturned?: number | null;
  exceededTransferLimit?: boolean | null;
}

export interface HonuaFeatureStreamMetadata {
  eventType?: string;
  sequence?: string | number | null;
  deletedIds?: Array<string | number>;
  objectIds?: Array<string | number>;
  metadata?: Record<string, unknown> | null;
}

export interface HonuaFeatureStreamEvent {
  type?: string | null;
  eventType?: string | null;
  sequence?: string | number | null;
  source?: HonuaDisplaySourceDescriptor | null;
  page?: HonuaFeatureQueryResult | null;
  result?: HonuaFeatureQueryResult | null;
  feature?: HonuaFeatureRecord | null;
  features?: HonuaFeatureRecord[] | null;
  items?: HonuaFeatureRecord[] | null;
  id?: string | number | null;
  objectId?: string | number | null;
  featureIds?: Array<string | number> | null;
  deletedIds?: Array<string | number> | null;
  objectIds?: Array<string | number> | null;
  metadata?: Record<string, unknown> | null;
}

export interface HonuaFeatureConversionOptions {
  source?: HonuaDisplaySourceDescriptor | null;
  streamEventType?: string | null;
  streamSequence?: string | number | null;
}

export interface HonuaGeoJsonLayerOptions
  extends Omit<GeoJsonLayerProps<HonuaGeoJsonProperties>, 'data' | 'id'> {
  id?: string;
  source?: HonuaDisplaySourceDescriptor | null;
}

export interface HonuaDeckGeoJsonLayerData {
  id: string;
  source: HonuaDisplaySourceDescriptor | null;
  data: HonuaDisplayFeatureCollection;
}

export interface HonuaDeckOverlayOptions
  extends Omit<MapboxOverlayProps, 'layers'> {
  layers?: Layer[];
}

export type HonuaMapViewCenter =
  | [number, number]
  | { longitude: number; latitude: number }
  | { lng: number; lat: number };

export interface HonuaMapViewOptions {
  center?: HonuaMapViewCenter | null;
  zoom?: number | null;
  bounds?: HonuaDisplayBounds | null;
  fitOptions?: HonuaMapFitBoundsOptions;
  jumpOptions?: HonuaMapJumpToOptions;
}

export interface HonuaMapFitBoundsOptions {
  padding?: number | {
    top?: number;
    right?: number;
    bottom?: number;
    left?: number;
  };
  maxZoom?: number;
  duration?: number;
  essential?: boolean;
  [key: string]: unknown;
}

export interface HonuaMapJumpToOptions {
  center?: [number, number];
  zoom?: number;
  [key: string]: unknown;
}

export interface HonuaMapLibreLike {
  addControl(control: unknown, position?: string): unknown;
  removeControl?(control: unknown): unknown;
  fitBounds?(
    bounds: [[number, number], [number, number]],
    options?: HonuaMapFitBoundsOptions,
  ): unknown;
  jumpTo?(options: HonuaMapJumpToOptions): unknown;
}

export interface HonuaWebDisplayAdapterOptions
  extends HonuaDeckOverlayOptions {
  controlPosition?: string;
}

export type HonuaFeatureQueryLayerOptions =
  | HonuaGeoJsonLayerOptions
  | ((
    result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
    index: number,
  ) => HonuaGeoJsonLayerOptions);

export class HonuaWebDisplayAdapter {
  readonly #map: HonuaMapLibreLike;
  readonly #overlay: MapboxOverlay;
  #layers: Layer[];
  readonly #featureCollections = new Map<string, HonuaDisplayFeatureCollection>();

  constructor(map: HonuaMapLibreLike, options: HonuaWebDisplayAdapterOptions = {}) {
    const { controlPosition, layers = [], ...overlayOptions } = options;
    this.#map = map;
    this.#layers = [...layers];
    this.#overlay = createHonuaDeckOverlay(this.#layers, overlayOptions);
    this.#map.addControl(this.#overlay, controlPosition);
  }

  get overlay(): MapboxOverlay {
    return this.#overlay;
  }

  get layers(): readonly Layer[] {
    return this.#layers;
  }

  setLayers(layers: Layer[]): void {
    this.#layers = [...layers];
    this.#overlay.setProps({ layers: this.#layers });
  }

  setView(view: HonuaMapViewOptions): boolean {
    if (view.bounds && this.#map.fitBounds) {
      this.#map.fitBounds(boundsToLngLatBounds(view.bounds), view.fitOptions);
      return true;
    }

    const jumpOptions: HonuaMapJumpToOptions = {
      ...(view.jumpOptions ?? {}),
    };
    const center = normalizeCenter(view.center);

    if (center) {
      jumpOptions.center = center;
    }

    if (typeof view.zoom === 'number' && Number.isFinite(view.zoom)) {
      jumpOptions.zoom = view.zoom;
    }

    if ((jumpOptions.center || jumpOptions.zoom !== undefined) && this.#map.jumpTo) {
      this.#map.jumpTo(jumpOptions);
      return true;
    }

    return false;
  }

  fitToSource(
    source: HonuaDisplaySourceDescriptor | HonuaDisplayFeatureCollection,
    fitOptions?: HonuaMapFitBoundsOptions,
  ): boolean {
    const bounds = isFeatureCollection(source)
      ? (source as HonuaDisplayFeatureCollection).honua?.extent
      : source.extent;

    if (!bounds) {
      return false;
    }

    return this.setView({ bounds, fitOptions });
  }

  setFeatureQueryResult(
    result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
    options: HonuaGeoJsonLayerOptions = {},
  ): Layer {
    const layer = createHonuaGeoJsonLayer(result, options);
    this.#featureCollections.set(layer.id, layer.props.data as HonuaDisplayFeatureCollection);
    this.setLayers([
      ...this.#layers.filter((existing) => existing.id !== layer.id),
      layer,
    ]);

    return layer;
  }

  setFeatureQueryResults(
    results: Array<HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>>,
    options: HonuaFeatureQueryLayerOptions = {},
  ): Layer[] {
    return results.map((result, index) => this.setFeatureQueryResult(
      result,
      typeof options === 'function' ? options(result, index) : options,
    ));
  }

  appendFeatureQueryResult(
    result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
    options: HonuaGeoJsonLayerOptions = {},
  ): Layer {
    const source = resolveQuerySource(result, options.source ?? this.#singleFeatureCollectionSource());
    const id = options.id ?? buildLayerId(source);
    const data = appendFeatureQueryResultToGeoJson(
      result,
      this.#featureCollections.get(id),
      { source },
    );
    const layer = createHonuaGeoJsonLayer(data, { ...options, id, source });

    this.#featureCollections.set(id, layer.props.data as HonuaDisplayFeatureCollection);
    this.setLayers([
      ...this.#layers.filter((existing) => existing.id !== layer.id),
      layer,
    ]);

    return layer;
  }

  #singleFeatureCollectionSource(): HonuaDisplaySourceDescriptor | null | undefined {
    if (this.#featureCollections.size !== 1) {
      return undefined;
    }

    return this.#featureCollections.values().next().value?.honua?.source ?? undefined;
  }

  setFeatureStreamEvent(
    event: HonuaFeatureStreamEvent,
    options: HonuaGeoJsonLayerOptions = {},
  ): Layer {
    const source = resolveStreamSource(event, options.source);
    const id = options.id ?? buildLayerId(source);
    const data = applyFeatureStreamEventToGeoJson(
      event,
      this.#featureCollections.get(id),
      { source },
    );
    const layer = createHonuaGeoJsonLayer(data, { ...options, id, source });

    this.#featureCollections.set(id, layer.props.data as HonuaDisplayFeatureCollection);
    this.setLayers([
      ...this.#layers.filter((existing) => existing.id !== layer.id),
      layer,
    ]);

    return layer;
  }

  getFeatureCollection(id: string): HonuaDisplayFeatureCollection | undefined {
    return this.#featureCollections.get(id);
  }

  removeLayer(id: string): boolean {
    const nextLayers = this.#layers.filter((layer) => layer.id !== id);
    const removed = nextLayers.length !== this.#layers.length;

    if (!removed) {
      return false;
    }

    this.#featureCollections.delete(id);
    this.setLayers(nextLayers);
    return true;
  }

  clearFeatureLayers(): void {
    const featureLayerIds = new Set(this.#featureCollections.keys());

    if (featureLayerIds.size === 0) {
      return;
    }

    this.#featureCollections.clear();
    this.setLayers(this.#layers.filter((layer) => !featureLayerIds.has(layer.id)));
  }

  destroy(): void {
    this.#map.removeControl?.(this.#overlay);
    this.#overlay.finalize();
    this.#layers = [];
    this.#featureCollections.clear();
  }
}

export function featureQueryResultToGeoJson(
  result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
  options: HonuaFeatureConversionOptions = {},
): HonuaDisplayFeatureCollection {
  const source = resolveQuerySource(result, options.source);
  const records = Array.isArray(result)
    ? result
    : isFeatureCollection(result)
      ? result.features
      : result.features ?? result.items ?? [];
  const page = !Array.isArray(result) && !isFeatureCollection(result) ? result : null;
  const metadata = buildCollectionMetadata(result, source, records.length, options);

  if (isFeatureCollection(result)) {
    return {
      ...result,
      features: result.features
        .map((feature) => recordToFeature(feature, {
          source,
          objectIdFieldName: metadata.objectIdFieldName,
          streamEventType: options.streamEventType,
          streamSequence: options.streamSequence,
        }))
        .filter((feature): feature is Feature<Geometry, HonuaGeoJsonProperties> => feature !== null),
      honua: {
        ...((result as HonuaDisplayFeatureCollection).honua ?? {}),
        ...metadata,
      },
    };
  }

  return {
    type: 'FeatureCollection',
    features: records
      .map((record) => recordToFeature(record, {
        source,
        objectIdFieldName: page?.objectIdFieldName ?? null,
        streamEventType: options.streamEventType,
        streamSequence: options.streamSequence,
      }))
      .filter((feature): feature is Feature<Geometry, HonuaGeoJsonProperties> => feature !== null),
    honua: metadata,
  };
}

export function featureStreamEventToGeoJson(
  event: HonuaFeatureStreamEvent,
  options: HonuaFeatureConversionOptions = {},
): HonuaDisplayFeatureCollection {
  return applyFeatureStreamEventToGeoJson(event, null, options);
}

export function appendFeatureQueryResultToGeoJson(
  result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
  previous: HonuaDisplayFeatureCollection | null | undefined = null,
  options: HonuaFeatureConversionOptions = {},
): HonuaDisplayFeatureCollection {
  const source = resolveQuerySource(result, options.source ?? previous?.honua?.source);
  const incoming = featureQueryResultToGeoJson(result, { ...options, source });

  if (!previous) {
    return incoming;
  }

  const features = mergeStreamFeatures(previous.features, incoming.features, new Set());
  const metadata: HonuaDisplayDataMetadata = removeUndefinedProperties({
    ...(previous.honua ?? {}),
    ...(incoming.honua ?? {}),
    source,
    sourceId: source?.id,
    nextPageToken: hasExplicitNextPageToken(result)
      ? incoming.honua?.nextPageToken ?? null
      : previous.honua?.nextPageToken ?? incoming.honua?.nextPageToken ?? null,
    numberReturned: features.length,
    stream: incoming.honua?.stream ?? previous.honua?.stream ?? null,
  });

  return {
    type: 'FeatureCollection',
    features,
    honua: metadata,
  };
}

export function applyFeatureStreamEventToGeoJson(
  event: HonuaFeatureStreamEvent,
  previous: HonuaDisplayFeatureCollection | null | undefined = null,
  options: HonuaFeatureConversionOptions = {},
): HonuaDisplayFeatureCollection {
  const eventType = normalizeStreamEventType(event);
  const source = resolveStreamSource(event, options.source ?? previous?.honua?.source);
  const streamOptions = {
    source,
    streamEventType: eventType,
    streamSequence: event.sequence ?? options.streamSequence,
  };
  const incoming = streamEventPayloadToGeoJson(event, streamOptions);
  const streamMetadata = buildStreamMetadata(event, eventType);
  const metadata: HonuaDisplayDataMetadata = {
    ...(previous?.honua ?? {}),
    ...(incoming.honua ?? {}),
    source,
    sourceId: source?.id,
    stream: streamMetadata,
  };

  if (isClearStreamEvent(eventType)) {
    return {
      type: 'FeatureCollection',
      features: [],
      honua: metadata,
    };
  }

  if (!previous || isReplaceStreamEvent(eventType)) {
    return {
      ...incoming,
      honua: metadata,
    };
  }

  const incomingFeatures = isDeleteStreamEvent(eventType) ? [] : incoming.features;
  const features = mergeStreamFeatures(previous.features, incomingFeatures, streamDeleteKeys(event));

  return {
    type: 'FeatureCollection',
    features,
    honua: metadata,
  };
}

export function createHonuaGeoJsonLayer(
  result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
  options: HonuaGeoJsonLayerOptions = {},
): Layer {
  const layerData = createHonuaGeoJsonLayerData(result, options);
  const { source: _source, ...layerOptions } = options;

  return new GeoJsonLayer<HonuaGeoJsonProperties>({
    id: layerData.id,
    data: layerData.data,
    pickable: true,
    autoHighlight: true,
    stroked: true,
    filled: true,
    pointType: 'circle',
    lineWidthMinPixels: 1,
    getFillColor: [31, 122, 140, 168],
    getLineColor: [19, 33, 44, 220],
    getLineWidth: 1,
    getPointRadius: 6,
    pointRadiusUnits: 'pixels',
    ...layerOptions,
  });
}

export function createHonuaGeoJsonLayerData(
  result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
  options: Pick<HonuaGeoJsonLayerOptions, 'id' | 'source'> = {},
): HonuaDeckGeoJsonLayerData {
  const source = options.source ?? (
    !Array.isArray(result) && !isFeatureCollection(result)
      ? result.source
      : (result as HonuaDisplayFeatureCollection).honua?.source ?? null
  ) ?? null;
  const id = options.id ?? buildLayerId(source);

  return {
    id,
    source,
    data: featureQueryResultToGeoJson(result, { source }),
  };
}

export function createHonuaDeckOverlay(
  layers: Layer[] = [],
  options: Omit<MapboxOverlayProps, 'layers'> = {},
): MapboxOverlay {
  return new MapboxOverlay({
    interleaved: true,
    ...options,
    layers,
  });
}

function recordToFeature(
  record: HonuaFeatureRecord,
  context: HonuaFeatureConversionOptions & { objectIdFieldName?: string | null } = {},
): Feature<Geometry, HonuaGeoJsonProperties> | null {
  const geometryOrFeature = record.geoJson ?? record.geoJsonGeometry ?? record.geometry ?? null;
  if (geometryOrFeature === null) {
    return null;
  }

  const featureId = resolveFeatureId(record, geometryOrFeature);
  const objectId = resolveObjectId(record, context.objectIdFieldName);
  const metadata = buildFeatureMetadata({
    source: context.source,
    featureId,
    objectId,
    streamEventType: context.streamEventType ?? undefined,
    streamSequence: context.streamSequence ?? undefined,
  });

  if (isFeature(geometryOrFeature)) {
    const feature: Feature<Geometry, HonuaGeoJsonProperties> = {
      ...geometryOrFeature,
      properties: mergeFeatureProperties([
        record.attributes,
        geometryOrFeature.properties,
        record.properties,
      ], metadata),
    };

    if (featureId !== undefined) {
      feature.id = featureId;
    }

    return feature;
  }

  if (!isGeometry(geometryOrFeature)) {
    return null;
  }

  const feature: Feature<Geometry, HonuaGeoJsonProperties> = {
    type: 'Feature',
    id: featureId,
    geometry: geometryOrFeature,
    properties: mergeFeatureProperties([record.attributes, record.properties], metadata),
  };

  return feature;
}

function buildLayerId(source: HonuaDisplaySourceDescriptor | null | undefined): string {
  if (!source?.id) {
    return 'honua-features';
  }

  return `honua-${source.id.trim().replace(/[^a-z0-9_-]+/gi, '-').replace(/^-+|-+$/g, '') || 'features'}`;
}

function boundsToLngLatBounds(bounds: HonuaDisplayBounds): [[number, number], [number, number]] {
  return [
    [bounds.minLongitude, bounds.minLatitude],
    [bounds.maxLongitude, bounds.maxLatitude],
  ];
}

function normalizeCenter(center: HonuaMapViewCenter | null | undefined): [number, number] | null {
  if (!center) {
    return null;
  }

  if (Array.isArray(center)) {
    const [longitude, latitude] = center;
    return Number.isFinite(longitude) && Number.isFinite(latitude)
      ? [longitude, latitude]
      : null;
  }

  if ('longitude' in center && Number.isFinite(center.longitude) && Number.isFinite(center.latitude)) {
    return [center.longitude, center.latitude];
  }

  if ('lng' in center && Number.isFinite(center.lng) && Number.isFinite(center.lat)) {
    return [center.lng, center.lat];
  }

  return null;
}

function resolveQuerySource(
  result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
  source: HonuaDisplaySourceDescriptor | null | undefined,
): HonuaDisplaySourceDescriptor | null {
  if (source !== undefined) {
    return source;
  }

  if (!Array.isArray(result) && !isFeatureCollection(result)) {
    return result.source ?? null;
  }

  if (isFeatureCollection(result)) {
    return (result as HonuaDisplayFeatureCollection).honua?.source ?? null;
  }

  return null;
}

function resolveStreamSource(
  event: HonuaFeatureStreamEvent,
  source: HonuaDisplaySourceDescriptor | null | undefined,
): HonuaDisplaySourceDescriptor | null {
  return source ?? event.source ?? event.page?.source ?? event.result?.source ?? null;
}

function buildCollectionMetadata(
  result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
  source: HonuaDisplaySourceDescriptor | null,
  numberReturned: number,
  options: HonuaFeatureConversionOptions,
): HonuaDisplayDataMetadata {
  const page = !Array.isArray(result) && !isFeatureCollection(result) ? result : null;
  const existing = isFeatureCollection(result) ? (result as HonuaDisplayFeatureCollection).honua : undefined;

  return removeUndefinedProperties({
    ...(existing ?? {}),
    source,
    sourceId: source?.id,
    schema: source?.schema ?? page?.fields,
    extent: source?.extent ?? null,
    spatialReference: page?.spatialReference ?? source?.spatialReference ?? null,
    geometryType: page?.geometryType ?? source?.geometryType ?? null,
    queryCapabilities: source?.queryCapabilities,
    tileUrl: source?.tileUrl ?? null,
    feedUrl: source?.feedUrl ?? null,
    providerName: page?.providerName ?? existing?.providerName ?? null,
    objectIdFieldName: page?.objectIdFieldName ?? existing?.objectIdFieldName ?? null,
    nextPageToken: page && 'nextPageToken' in page
      ? page.nextPageToken ?? null
      : existing?.nextPageToken ?? null,
    totalCount: page?.totalCount ?? page?.count ?? existing?.totalCount ?? null,
    numberReturned: page?.numberReturned ?? numberReturned,
    exceededTransferLimit: page?.exceededTransferLimit ?? existing?.exceededTransferLimit ?? null,
    stream: options.streamEventType
      ? {
          eventType: options.streamEventType,
          sequence: options.streamSequence ?? null,
        }
      : existing?.stream ?? null,
  });
}

function buildFeatureMetadata(metadata: HonuaFeaturePickingMetadata): HonuaFeaturePickingMetadata {
  return removeUndefinedProperties({
    sourceId: metadata.source?.id,
    source: metadata.source ?? undefined,
    featureId: metadata.featureId,
    objectId: metadata.objectId,
    streamEventType: metadata.streamEventType ?? undefined,
    streamSequence: metadata.streamSequence ?? undefined,
  });
}

function mergeFeatureProperties(
  values: Array<Record<string, unknown> | GeoJsonProperties | null | undefined>,
  metadata: HonuaFeaturePickingMetadata,
): HonuaGeoJsonProperties {
  const properties: HonuaGeoJsonProperties = {};
  let existingMetadata: HonuaFeaturePickingMetadata = {};

  for (const value of values) {
    if (!isRecord(value)) {
      continue;
    }

    const nextMetadata = value[HONUA_FEATURE_METADATA_PROPERTY];
    Object.assign(properties, value);

    if (isRecord(nextMetadata)) {
      existingMetadata = { ...existingMetadata, ...nextMetadata };
    }
  }

  properties[HONUA_FEATURE_METADATA_PROPERTY] = {
    ...existingMetadata,
    ...metadata,
  };

  return properties;
}

function resolveFeatureId(
  record: HonuaFeatureRecord,
  geometryOrFeature: Geometry | Feature<Geometry, GeoJsonProperties>,
): string | number | undefined {
  return record.id ??
    (isFeature(geometryOrFeature) ? geometryOrFeature.id : undefined) ??
    record.objectId;
}

function resolveObjectId(
  record: HonuaFeatureRecord,
  objectIdFieldName: string | null | undefined,
): string | number | undefined {
  if (record.objectId !== undefined) {
    return record.objectId;
  }

  const attributes = record.attributes ?? record.properties;
  if (!isRecord(attributes)) {
    return undefined;
  }

  const candidates = [
    objectIdFieldName,
    'objectid',
    'OBJECTID',
    'ObjectID',
    'FID',
  ].filter((candidate): candidate is string => Boolean(candidate));

  for (const candidate of candidates) {
    const value = attributes[candidate];
    if (typeof value === 'string' || typeof value === 'number') {
      return value;
    }
  }

  return undefined;
}

function normalizeStreamEventType(event: HonuaFeatureStreamEvent): string {
  return event.type ?? event.eventType ?? (event.page || event.result ? 'page' : 'upsert');
}

function streamEventPayloadToGeoJson(
  event: HonuaFeatureStreamEvent,
  options: HonuaFeatureConversionOptions,
): HonuaDisplayFeatureCollection {
  const page = event.page ?? event.result;
  if (page) {
    return featureQueryResultToGeoJson(page, options);
  }

  const records = event.feature
    ? [event.feature]
    : event.features ?? event.items ?? [];

  return featureQueryResultToGeoJson(records, options);
}

function buildStreamMetadata(
  event: HonuaFeatureStreamEvent,
  eventType: string,
): HonuaFeatureStreamMetadata {
  return removeUndefinedProperties({
    eventType,
    sequence: event.sequence ?? null,
    deletedIds: event.deletedIds ?? event.featureIds ?? undefined,
    objectIds: event.objectIds ??
      (event.objectId !== undefined && event.objectId !== null ? [event.objectId] : undefined),
    metadata: event.metadata ?? undefined,
  });
}

function isClearStreamEvent(eventType: string): boolean {
  return ['clear', 'reset'].includes(eventType.toLowerCase());
}

function isReplaceStreamEvent(eventType: string): boolean {
  return ['snapshot', 'replace'].includes(eventType.toLowerCase());
}

function isDeleteStreamEvent(eventType: string): boolean {
  return ['delete', 'deleted', 'remove', 'removed'].includes(eventType.toLowerCase());
}

function streamDeleteKeys(event: HonuaFeatureStreamEvent): Set<string> {
  const keys = new Set<string>();
  const ids = [
    ...(event.deletedIds ?? []),
    ...(event.featureIds ?? []),
    ...(event.id !== undefined && event.id !== null ? [event.id] : []),
  ];
  const objectIds = [
    ...(event.objectIds ?? []),
    ...(event.objectId !== undefined && event.objectId !== null ? [event.objectId] : []),
  ];

  for (const id of ids) {
    addFeatureKeyAliases(keys, id);
  }

  for (const objectId of objectIds) {
    addObjectKeyAliases(keys, objectId);
  }

  if (isDeleteStreamEvent(normalizeStreamEventType(event))) {
    for (const feature of streamEventPayloadToGeoJson(event, {
      source: resolveStreamSource(event, undefined),
      streamEventType: normalizeStreamEventType(event),
      streamSequence: event.sequence,
    }).features) {
      for (const key of featureKeys(feature)) {
        keys.add(key);
      }
    }
  }

  return keys;
}

function mergeStreamFeatures(
  previous: Feature<Geometry, HonuaGeoJsonProperties>[],
  incoming: Feature<Geometry, HonuaGeoJsonProperties>[],
  deleteKeys: Set<string>,
): Feature<Geometry, HonuaGeoJsonProperties>[] {
  const merged = new Map<string, Feature<Geometry, HonuaGeoJsonProperties>>();
  let anonymousIndex = 0;

  for (const feature of previous) {
    if (featureKeys(feature).some((key) => deleteKeys.has(key))) {
      continue;
    }

    merged.set(primaryFeatureKey(feature) ?? `previous:${anonymousIndex++}`, feature);
  }

  for (const feature of incoming) {
    const keys = featureKeys(feature);
    const existingKey = [...merged.entries()]
      .find(([, existing]) => featureKeys(existing).some((key) => keys.includes(key)))?.[0];

    merged.set(existingKey ?? primaryFeatureKey(feature) ?? `incoming:${anonymousIndex++}`, feature);
  }

  return [...merged.values()];
}

function primaryFeatureKey(feature: Feature<Geometry, HonuaGeoJsonProperties>): string | null {
  return featureKeys(feature)[0] ?? null;
}

function featureKeys(feature: Feature<Geometry, HonuaGeoJsonProperties>): string[] {
  const keys = new Set<string>();
  const metadata = feature.properties?.[HONUA_FEATURE_METADATA_PROPERTY];

  if (metadata?.objectId !== undefined) {
    addObjectKeyAliases(keys, metadata.objectId);
  }

  if (feature.id !== undefined) {
    addFeatureKeyAliases(keys, feature.id);
  }

  if (metadata?.featureId !== undefined) {
    addFeatureKeyAliases(keys, metadata.featureId);
  }

  return [...keys];
}

function addFeatureKeyAliases(keys: Set<string>, id: string | number): void {
  keys.add(`feature:${String(id)}`);
  keys.add(`value:${String(id)}`);
}

function addObjectKeyAliases(keys: Set<string>, objectId: string | number): void {
  keys.add(`object:${String(objectId)}`);
  keys.add(`value:${String(objectId)}`);
}

function removeUndefinedProperties<T extends object>(value: T): T {
  return Object.fromEntries(
    Object.entries(value).filter(([, entry]) => entry !== undefined),
  ) as T;
}

function isFeatureCollection(
  value: unknown,
): value is FeatureCollection<Geometry, GeoJsonProperties> {
  return isRecord(value) && value.type === 'FeatureCollection' && Array.isArray(value.features);
}

function hasExplicitNextPageToken(
  result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
): boolean {
  return !Array.isArray(result) && !isFeatureCollection(result) && 'nextPageToken' in result;
}

function isFeature(value: unknown): value is Feature<Geometry, GeoJsonProperties> {
  return isRecord(value) && value.type === 'Feature' && isGeometry(value.geometry);
}

function isGeometry(value: unknown): value is Geometry {
  return isRecord(value) &&
    typeof value.type === 'string' &&
    ('coordinates' in value || 'geometries' in value);
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null;
}
