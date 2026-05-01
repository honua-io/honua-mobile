import type { Layer } from '@deck.gl/core';
import { GeoJsonLayer, type GeoJsonLayerProps } from '@deck.gl/layers';
import { MapboxOverlay, type MapboxOverlayProps } from '@deck.gl/mapbox';
import type {
  Feature,
  FeatureCollection,
  GeoJsonProperties,
  Geometry,
} from 'geojson';

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
  source?: HonuaDisplaySourceDescriptor | null;
  features?: HonuaFeatureRecord[] | null;
  items?: HonuaFeatureRecord[] | null;
  spatialReference?: HonuaDisplaySpatialReference | null;
  nextPageToken?: string | null;
  totalCount?: number | null;
}

export interface HonuaGeoJsonLayerOptions
  extends Omit<GeoJsonLayerProps<Record<string, unknown>>, 'data' | 'id'> {
  id?: string;
  source?: HonuaDisplaySourceDescriptor | null;
}

export interface HonuaDeckOverlayOptions
  extends Omit<MapboxOverlayProps, 'layers'> {
  layers?: Layer[];
}

export interface HonuaMapLibreLike {
  addControl(control: unknown, position?: string): unknown;
  removeControl?(control: unknown): unknown;
}

export interface HonuaWebDisplayAdapterOptions
  extends HonuaDeckOverlayOptions {
  controlPosition?: string;
}

export class HonuaWebDisplayAdapter {
  readonly #map: HonuaMapLibreLike;
  readonly #overlay: MapboxOverlay;
  #layers: Layer[];

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

  setFeatureQueryResult(
    result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
    options: HonuaGeoJsonLayerOptions = {},
  ): Layer {
    const layer = createHonuaGeoJsonLayer(result, options);
    this.setLayers([
      ...this.#layers.filter((existing) => existing.id !== layer.id),
      layer,
    ]);

    return layer;
  }

  destroy(): void {
    this.#map.removeControl?.(this.#overlay);
    this.#overlay.finalize();
    this.#layers = [];
  }
}

export function featureQueryResultToGeoJson(
  result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
): FeatureCollection<Geometry, GeoJsonProperties> {
  if (isFeatureCollection(result)) {
    return result;
  }

  const records = Array.isArray(result)
    ? result
    : result.features ?? result.items ?? [];

  return {
    type: 'FeatureCollection',
    features: records
      .map(recordToFeature)
      .filter((feature): feature is Feature<Geometry, GeoJsonProperties> => feature !== null),
  };
}

export function createHonuaGeoJsonLayer(
  result: HonuaFeatureQueryResult | HonuaFeatureRecord[] | FeatureCollection<Geometry, GeoJsonProperties>,
  options: HonuaGeoJsonLayerOptions = {},
): Layer {
  const source = options.source ?? (!Array.isArray(result) && !isFeatureCollection(result)
    ? result.source
    : null);
  const id = options.id ?? buildLayerId(source);
  const featureCollection = featureQueryResultToGeoJson(result);
  const { source: _source, ...layerOptions } = options;

  return new GeoJsonLayer<Record<string, unknown>>({
    id,
    data: featureCollection,
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

function recordToFeature(record: HonuaFeatureRecord): Feature<Geometry, GeoJsonProperties> | null {
  const geometryOrFeature = record.geoJson ?? record.geoJsonGeometry ?? record.geometry ?? null;
  if (geometryOrFeature === null) {
    return null;
  }

  if (isFeature(geometryOrFeature)) {
    return {
      ...geometryOrFeature,
      properties: {
        ...record.attributes,
        ...geometryOrFeature.properties,
        ...record.properties,
      },
    };
  }

  if (!isGeometry(geometryOrFeature)) {
    return null;
  }

  return {
    type: 'Feature',
    id: record.id ?? record.objectId,
    geometry: geometryOrFeature,
    properties: {
      ...record.attributes,
      ...record.properties,
    },
  };
}

function buildLayerId(source: HonuaDisplaySourceDescriptor | null | undefined): string {
  if (!source?.id) {
    return 'honua-features';
  }

  return `honua-${source.id.trim().replace(/[^a-z0-9_-]+/gi, '-').replace(/^-+|-+$/g, '') || 'features'}`;
}

function isFeatureCollection(
  value: unknown,
): value is FeatureCollection<Geometry, GeoJsonProperties> {
  return isRecord(value) && value.type === 'FeatureCollection' && Array.isArray(value.features);
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
