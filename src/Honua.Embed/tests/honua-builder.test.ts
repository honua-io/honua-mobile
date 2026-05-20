import { describe, expect, it } from 'vitest';
import {
  applyHonuaMapBuilderState,
  createHonuaMapBuilderSnippet,
  createHonuaMapBuilderState,
} from '../src/index';

describe('honua map builder', () => {
  it('normalizes layer selections and generates a credential-safe snippet', () => {
    const state = createHonuaMapBuilderState({
      serviceUrl: ' https://services.example.test/FeatureServer ',
      apiKey: 'secret-browser-key',
      availableLayers: [
        { id: ' assets ', label: 'Assets', defaultSelected: true },
        { id: 'work-orders', label: 'Work orders' },
        { id: 'drafts', label: 'Draft layers', disabled: true },
      ],
      selectedLayerIds: ['work-orders', 'missing', 'drafts', 'work-orders'],
      center: { latitude: 21.3069, longitude: -157.8583 },
      zoom: 12,
      basemap: 'streets',
      interactive: true,
      search: true,
      identify: true,
      label: 'Field operations',
    }, {
      elementName: 'field-ops-map',
    });

    expect(state.canGenerateSnippet).toBe(true);
    expect(state.options.serviceUrl).toBe('https://services.example.test/FeatureServer');
    expect(state.selectedLayerIds).toEqual(['work-orders']);
    expect(state.selectedLayers).toEqual([
      expect.objectContaining({ id: 'work-orders', label: 'Work orders' }),
    ]);
    expect(state.issues.map((issue) => issue.code)).toEqual([
      'unavailable-layer',
      'disabled-layer',
      'credentials-omitted',
    ]);
    expect(state.snippet).toContain('defineHonuaMapElement(\'field-ops-map\')');
    expect(state.snippet).toContain('layer-ids="work-orders"');
    expect(state.snippet).not.toContain('secret-browser-key');
  });

  it('surfaces validation errors before generating snippets', () => {
    const state = createHonuaMapBuilderState({
      serviceUrl: 'ftp://services.example.test/FeatureServer',
      center: { latitude: 121, longitude: -157.8583 },
      bounds: {
        minLongitude: -157.6,
        minLatitude: 21.6,
        maxLongitude: -158.3,
        maxLatitude: 21.2,
      },
      zoom: 30,
    });

    expect(state.canGenerateSnippet).toBe(false);
    expect(state.snippet).toBeNull();
    expect(state.issues.filter((issue) => issue.severity === 'error').map((issue) => issue.code)).toEqual([
      'unsupported-service-url-scheme',
      'invalid-zoom',
      'invalid-coordinate',
      'invalid-bounds',
    ]);
  });

  it('applies builder preview state to an existing map element', () => {
    const element = document.createElement('honua-map');
    const state = createHonuaMapBuilderState({
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets'],
      interactive: true,
      search: false,
      style: {
        accent: '#0f766e',
      },
    }, {
      includeScript: false,
    });

    applyHonuaMapBuilderState(element, state);

    expect(element.getAttribute('service-url')).toBe('https://services.example.test/FeatureServer');
    expect(element.getAttribute('layer-ids')).toBe('assets');
    expect(element.hasAttribute('interactive')).toBe(true);
    expect(element.hasAttribute('search')).toBe(false);
    expect(element.style.getPropertyValue('--honua-map-accent')).toBe('#0f766e');
  });

  it('throws a compact summary when required builder fields are invalid', () => {
    expect(() => createHonuaMapBuilderSnippet({
      serviceUrl: '',
    })).toThrow('A service URL is required before generating an embed snippet.');
  });
});
