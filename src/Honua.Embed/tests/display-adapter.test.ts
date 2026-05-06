import { describe, expect, it, vi } from 'vitest';
import {
  appendFeatureQueryResultToGeoJson,
  applyFeatureStreamEventToGeoJson,
  createHonuaGeoJsonLayer,
  createHonuaGeoJsonLayerData,
  featureQueryResultToGeoJson,
  HONUA_FEATURE_METADATA_PROPERTY,
  HonuaWebDisplayAdapter,
  type HonuaDisplayFeatureCollection,
  type HonuaDisplaySourceDescriptor,
} from '../src/index';

const fieldAssetSource: HonuaDisplaySourceDescriptor = {
  id: 'field-assets',
  title: 'Field assets',
  geometryType: 'Point',
  extent: {
    minLongitude: -157.9,
    minLatitude: 21.2,
    maxLongitude: -157.7,
    maxLatitude: 21.4,
  },
  spatialReference: { wkid: 4326 },
  schema: {
    fields: ['objectid', 'assetType', 'status'],
  },
  queryCapabilities: {
    supportsPagination: true,
  },
  tileUrl: 'https://tiles.example/assets/{z}/{x}/{y}.pbf',
  feedUrl: 'https://stream.example/assets',
};

describe('display adapter', () => {
  it('converts feature query pages into deck.gl-ready GeoJSON with page metadata', () => {
    const geoJson = featureQueryResultToGeoJson({
      providerName: 'sdk-fixture',
      source: fieldAssetSource,
      objectIdFieldName: 'objectid',
      geometryType: 'Point',
      nextPageToken: 'next-page',
      totalCount: 12,
      numberReturned: 2,
      features: [
        {
          id: 'asset-42',
          geometry: {
            type: 'Point',
            coordinates: [-157.8583, 21.3069],
          },
          attributes: {
            objectid: 42,
            assetType: 'hydrant',
          },
          properties: {
            status: 'active',
          },
        },
        {
          id: 'asset-43',
          geometry: null,
          attributes: {
            status: 'missing geometry',
          },
        },
      ],
    });

    expect(geoJson.honua).toMatchObject({
      sourceId: 'field-assets',
      source: fieldAssetSource,
      spatialReference: { wkid: 4326 },
      geometryType: 'Point',
      providerName: 'sdk-fixture',
      objectIdFieldName: 'objectid',
      nextPageToken: 'next-page',
      totalCount: 12,
      numberReturned: 2,
    });
    expect(geoJson.features).toHaveLength(1);
    expect(geoJson.features[0]).toMatchObject({
      type: 'Feature',
      id: 'asset-42',
      geometry: {
        type: 'Point',
        coordinates: [-157.8583, 21.3069],
      },
      properties: {
        objectid: 42,
        assetType: 'hydrant',
        status: 'active',
        [HONUA_FEATURE_METADATA_PROPERTY]: {
          sourceId: 'field-assets',
          source: fieldAssetSource,
          featureId: 'asset-42',
          objectId: 42,
        },
      },
    });
  });

  it('applies streaming feature events as GeoJSON upserts and deletes', () => {
    const initial = featureQueryResultToGeoJson({
      source: fieldAssetSource,
      objectIdFieldName: 'objectid',
      features: [
        {
          id: 'asset-1',
          geometry: {
            type: 'Point',
            coordinates: [-157.85, 21.3],
          },
          attributes: {
            objectid: 1,
            name: 'Pump 1',
          },
        },
      ],
    });

    const upserted = applyFeatureStreamEventToGeoJson({
      type: 'upsert',
      sequence: 2,
      source: fieldAssetSource,
      feature: {
        objectId: 2,
        geometry: {
          type: 'Point',
          coordinates: [-157.86, 21.31],
        },
        attributes: {
          objectid: 2,
          name: 'Pump 2',
        },
      },
    }, initial);

    expect(upserted.features.map((feature) => feature.properties.name)).toEqual([
      'Pump 1',
      'Pump 2',
    ]);
    expect(upserted.features[1].properties[HONUA_FEATURE_METADATA_PROPERTY]).toMatchObject({
      sourceId: 'field-assets',
      featureId: 2,
      objectId: 2,
      streamEventType: 'upsert',
      streamSequence: 2,
    });

    const deleted = applyFeatureStreamEventToGeoJson({
      type: 'delete',
      sequence: 3,
      source: fieldAssetSource,
      objectIds: [1],
    }, upserted);

    expect(deleted.features.map((feature) => feature.properties.name)).toEqual(['Pump 2']);
    expect(deleted.honua?.stream).toMatchObject({
      eventType: 'delete',
      sequence: 3,
      objectIds: [1],
    });
  });

  it('preserves picking metadata while enabling deck.gl highlighting', () => {
    const layer = createHonuaGeoJsonLayer({
      source: fieldAssetSource,
      features: [
        {
          objectId: 99,
          geoJson: {
            type: 'Feature',
            id: 'work-order-99',
            geometry: {
              type: 'Point',
              coordinates: [-157.84, 21.32],
            },
            properties: {
              status: 'open',
              [HONUA_FEATURE_METADATA_PROPERTY]: {
                pickingToken: 'host-selection-key',
              },
            },
          },
          attributes: {
            priority: 'high',
          },
        },
      ],
    }, {
      highlightColor: [255, 190, 80, 220],
    });
    const data = layer.props.data as HonuaDisplayFeatureCollection;

    expect(layer.props.pickable).toBe(true);
    expect(layer.props.autoHighlight).toBe(true);
    expect(layer.props.highlightColor).toEqual([255, 190, 80, 220]);
    expect(data.features[0].properties).toMatchObject({
      priority: 'high',
      status: 'open',
      [HONUA_FEATURE_METADATA_PROPERTY]: {
        pickingToken: 'host-selection-key',
        sourceId: 'field-assets',
        source: fieldAssetSource,
        featureId: 'work-order-99',
        objectId: 99,
      },
    });
  });

  it('builds deck.gl GeoJsonLayer data without dropping renderer-neutral descriptors', () => {
    const layerData = createHonuaGeoJsonLayerData({
      source: fieldAssetSource,
      features: [
        {
          id: 'asset-101',
          geometry: {
            type: 'Point',
            coordinates: [-157.83, 21.33],
          },
          attributes: {
            objectid: 101,
          },
        },
      ],
    });

    expect(layerData.id).toBe('honua-field-assets');
    expect(layerData.source).toBe(fieldAssetSource);
    expect(layerData.data.honua).toMatchObject({
      source: fieldAssetSource,
      sourceId: 'field-assets',
      schema: fieldAssetSource.schema,
      extent: fieldAssetSource.extent,
      spatialReference: fieldAssetSource.spatialReference,
      geometryType: fieldAssetSource.geometryType,
      queryCapabilities: fieldAssetSource.queryCapabilities,
      tileUrl: fieldAssetSource.tileUrl,
      feedUrl: fieldAssetSource.feedUrl,
    });
  });

  it('appends feature query pages into stable GeoJSON layer data', () => {
    const firstPage = featureQueryResultToGeoJson({
      source: fieldAssetSource,
      objectIdFieldName: 'objectid',
      nextPageToken: 'page-2',
      totalCount: 3,
      numberReturned: 2,
      features: [
        {
          id: 'asset-1',
          geometry: {
            type: 'Point',
            coordinates: [-157.85, 21.3],
          },
          attributes: {
            objectid: 1,
            status: 'active',
          },
        },
        {
          id: 'asset-2',
          geometry: {
            type: 'Point',
            coordinates: [-157.86, 21.31],
          },
          attributes: {
            objectid: 2,
            status: 'planned',
          },
        },
      ],
    });

    const appended = appendFeatureQueryResultToGeoJson({
      source: fieldAssetSource,
      objectIdFieldName: 'objectid',
      nextPageToken: null,
      totalCount: 3,
      numberReturned: 2,
      features: [
        {
          id: 'asset-2',
          geometry: {
            type: 'Point',
            coordinates: [-157.861, 21.311],
          },
          attributes: {
            objectid: 2,
            status: 'complete',
          },
        },
        {
          id: 'asset-3',
          geometry: {
            type: 'Point',
            coordinates: [-157.87, 21.32],
          },
          attributes: {
            objectid: 3,
            status: 'active',
          },
        },
      ],
    }, firstPage);

    expect(appended.features.map((feature) => feature.id)).toEqual([
      'asset-1',
      'asset-2',
      'asset-3',
    ]);
    expect(appended.features[1].properties.status).toBe('complete');
    expect(appended.honua).toMatchObject({
      source: fieldAssetSource,
      sourceId: 'field-assets',
      nextPageToken: null,
      totalCount: 3,
      numberReturned: 3,
    });
  });

  it('keeps renderer-neutral descriptors on layer data and MapLibre overlay updates', () => {
    const controls: unknown[] = [];
    const map = {
      addControl: vi.fn((control: unknown) => {
        controls.push(control);
      }),
      removeControl: vi.fn((control: unknown) => {
        const index = controls.indexOf(control);
        if (index >= 0) {
          controls.splice(index, 1);
        }
      }),
    };
    const adapter = new HonuaWebDisplayAdapter(map, { controlPosition: 'top-right' });

    const layer = adapter.setFeatureQueryResult({
      source: fieldAssetSource,
      features: [
        {
          geometry: {
            type: 'LineString',
            coordinates: [
              [-157.86, 21.3],
              [-157.85, 21.31],
            ],
          },
          attributes: {
            material: 'ductile iron',
          },
        },
      ],
    });
    const data = layer.props.data as HonuaDisplayFeatureCollection;

    expect(map.addControl).toHaveBeenCalledWith(adapter.overlay, 'top-right');
    expect(layer.id).toBe('honua-field-assets');
    expect(data.honua).toMatchObject({
      source: fieldAssetSource,
      sourceId: 'field-assets',
      schema: fieldAssetSource.schema,
      extent: fieldAssetSource.extent,
      queryCapabilities: fieldAssetSource.queryCapabilities,
      tileUrl: fieldAssetSource.tileUrl,
      feedUrl: fieldAssetSource.feedUrl,
    });
    expect(adapter.layers).toEqual([layer]);

    adapter.destroy();

    expect(map.removeControl).toHaveBeenCalledWith(adapter.overlay);
    expect(controls).toHaveLength(0);
  });

  it('applies MapLibre camera helpers from SDK extents and centers', () => {
    const map = {
      addControl: vi.fn(),
      fitBounds: vi.fn(),
      jumpTo: vi.fn(),
    };
    const adapter = new HonuaWebDisplayAdapter(map);

    expect(adapter.setView({
      bounds: fieldAssetSource.extent,
      fitOptions: {
        padding: 24,
        maxZoom: 15,
      },
    })).toBe(true);
    expect(map.fitBounds).toHaveBeenCalledWith(
      [[-157.9, 21.2], [-157.7, 21.4]],
      {
        padding: 24,
        maxZoom: 15,
      },
    );

    expect(adapter.setView({
      center: { longitude: -157.8583, latitude: 21.3069 },
      zoom: 13,
      jumpOptions: {
        duration: 0,
      },
    })).toBe(true);
    expect(map.jumpTo).toHaveBeenCalledWith({
      duration: 0,
      center: [-157.8583, 21.3069],
      zoom: 13,
    });

    const noCameraAdapter = new HonuaWebDisplayAdapter({ addControl: vi.fn() });

    expect(noCameraAdapter.setView({
      bounds: fieldAssetSource.extent,
      center: [-157.8583, 21.3069],
      zoom: 13,
    })).toBe(false);
  });

  it('fits MapLibre to source descriptors and cached feature collections', () => {
    const map = {
      addControl: vi.fn(),
      fitBounds: vi.fn(),
    };
    const adapter = new HonuaWebDisplayAdapter(map);
    const collection = featureQueryResultToGeoJson({
      source: fieldAssetSource,
      features: [],
    });

    expect(adapter.fitToSource(fieldAssetSource, { padding: 32 })).toBe(true);
    expect(adapter.fitToSource(collection, { maxZoom: 16 })).toBe(true);
    expect(map.fitBounds).toHaveBeenNthCalledWith(
      1,
      [[-157.9, 21.2], [-157.7, 21.4]],
      { padding: 32 },
    );
    expect(map.fitBounds).toHaveBeenNthCalledWith(
      2,
      [[-157.9, 21.2], [-157.7, 21.4]],
      { maxZoom: 16 },
    );
    expect(adapter.fitToSource({
      ...fieldAssetSource,
      id: 'missing-extent',
      extent: null,
    })).toBe(false);
  });

  it('manages feature query layer batches without removing host-owned layers', () => {
    const workOrderSource: HonuaDisplaySourceDescriptor = {
      ...fieldAssetSource,
      id: 'work-orders',
      title: 'Work orders',
    };
    const externalLayer = createHonuaGeoJsonLayer([], {
      id: 'external-overlay',
    });
    const adapter = new HonuaWebDisplayAdapter({ addControl: vi.fn() }, {
      layers: [externalLayer],
    });

    const layers = adapter.setFeatureQueryResults([
      {
        source: fieldAssetSource,
        features: [
          {
            id: 'asset-1',
            geometry: {
              type: 'Point',
              coordinates: [-157.85, 21.3],
            },
          },
        ],
      },
      {
        features: [
          {
            id: 'work-order-1',
            geometry: {
              type: 'Point',
              coordinates: [-157.86, 21.31],
            },
          },
        ],
      },
    ], (_result, index) => ({
      source: index === 0 ? fieldAssetSource : workOrderSource,
    }));

    expect(layers.map((layer) => layer.id)).toEqual([
      'honua-field-assets',
      'honua-work-orders',
    ]);
    expect(adapter.layers.map((layer) => layer.id)).toEqual([
      'external-overlay',
      'honua-field-assets',
      'honua-work-orders',
    ]);
    expect(adapter.getFeatureCollection('honua-field-assets')?.honua).toMatchObject({
      sourceId: 'field-assets',
      source: fieldAssetSource,
    });

    expect(adapter.removeLayer('honua-field-assets')).toBe(true);
    expect(adapter.getFeatureCollection('honua-field-assets')).toBeUndefined();
    expect(adapter.removeLayer('missing-layer')).toBe(false);
    expect(adapter.layers.map((layer) => layer.id)).toEqual([
      'external-overlay',
      'honua-work-orders',
    ]);

    adapter.clearFeatureLayers();

    expect(adapter.layers.map((layer) => layer.id)).toEqual(['external-overlay']);
    expect(adapter.getFeatureCollection('honua-work-orders')).toBeUndefined();
  });

  it('appends query pages through the adapter without replacing external layers', () => {
    const externalLayer = createHonuaGeoJsonLayer([], {
      id: 'external-overlay',
    });
    const adapter = new HonuaWebDisplayAdapter({ addControl: vi.fn() }, {
      layers: [externalLayer],
    });

    adapter.appendFeatureQueryResult({
      source: fieldAssetSource,
      objectIdFieldName: 'objectid',
      nextPageToken: 'page-2',
      features: [
        {
          id: 'asset-1',
          geometry: {
            type: 'Point',
            coordinates: [-157.85, 21.3],
          },
          attributes: {
            objectid: 1,
          },
        },
      ],
    });
    const layer = adapter.appendFeatureQueryResult([
      {
        id: 'asset-2',
        geometry: {
          type: 'Point',
          coordinates: [-157.86, 21.31],
        },
        attributes: {
          objectid: 2,
        },
      },
    ]);

    expect(adapter.layers.map((candidate) => candidate.id)).toEqual([
      'external-overlay',
      'honua-field-assets',
    ]);
    expect(layer.props.data).toBe(adapter.getFeatureCollection('honua-field-assets'));
    expect(adapter.getFeatureCollection('honua-field-assets')?.features).toHaveLength(2);
    expect(adapter.getFeatureCollection('honua-field-assets')?.honua).toMatchObject({
      source: fieldAssetSource,
      sourceId: 'field-assets',
      nextPageToken: 'page-2',
    });
  });
});
