import { describe, expect, it, vi } from 'vitest';
import {
  createHonuaGeoJsonLayer,
  featureQueryResultToGeoJson,
  HonuaWebDisplayAdapter,
} from '../src/index';

describe('display adapter', () => {
  it('converts feature query results into deck.gl-ready GeoJSON', () => {
    const geoJson = featureQueryResultToGeoJson({
      source: {
        id: 'field-assets',
        spatialReference: { wkid: 4326 },
      },
      features: [
        {
          id: 42,
          geometry: {
            type: 'Point',
            coordinates: [-157.8583, 21.3069],
          },
          attributes: {
            assetType: 'hydrant',
          },
          properties: {
            status: 'active',
          },
        },
        {
          id: 43,
          geometry: null,
          attributes: {
            status: 'missing geometry',
          },
        },
      ],
    });

    expect(geoJson).toEqual({
      type: 'FeatureCollection',
      features: [
        {
          type: 'Feature',
          id: 42,
          geometry: {
            type: 'Point',
            coordinates: [-157.8583, 21.3069],
          },
          properties: {
            assetType: 'hydrant',
            status: 'active',
          },
        },
      ],
    });
  });

  it('creates a GeoJsonLayer with stable Honua defaults', () => {
    const layer = createHonuaGeoJsonLayer({
      source: { id: 'utility lines' },
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

    expect(layer.id).toBe('honua-utility-lines');
    expect(layer.props.pickable).toBe(true);
    expect(layer.props.autoHighlight).toBe(true);
    expect(layer.props.data).toMatchObject({
      type: 'FeatureCollection',
      features: [
        {
          geometry: {
            type: 'LineString',
          },
        },
      ],
    });
  });

  it('attaches and updates deck.gl overlays on a MapLibre-compatible map', () => {
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
    const adapter = new HonuaWebDisplayAdapter(map);

    const layer = adapter.setFeatureQueryResult([
      {
        geometry: {
          type: 'Point',
          coordinates: [-157.8583, 21.3069],
        },
      },
    ]);

    expect(map.addControl).toHaveBeenCalledOnce();
    expect(adapter.layers).toEqual([layer]);

    adapter.destroy();

    expect(map.removeControl).toHaveBeenCalledWith(adapter.overlay);
    expect(controls).toHaveLength(0);
  });
});
