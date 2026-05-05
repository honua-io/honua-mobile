import { describe, expect, it, vi } from 'vitest';
import {
  applyFeatureStreamEventToGeoJson,
  createHonuaGeoJsonLayer,
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
});
