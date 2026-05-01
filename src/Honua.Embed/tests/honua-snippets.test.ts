import { beforeEach, describe, expect, it } from 'vitest';
import {
  applyHonuaMapOptions,
  createHonuaMapSnippet,
  defineHonuaMapElement,
} from '../src/index';

describe('honua map snippets', () => {
  beforeEach(() => {
    defineHonuaMapElement();
    document.body.replaceChildren();
  });

  it('generates a white-label custom-element snippet without credentials by default', () => {
    const snippet = createHonuaMapSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets', 'work-orders'],
      apiKey: 'secret-key',
      center: { latitude: 21.3069, longitude: -157.8583 },
      zoom: 12,
      bounds: {
        minLongitude: -158.3,
        minLatitude: 21.2,
        maxLongitude: -157.6,
        maxLatitude: 21.6,
      },
      basemap: 'streets',
      interactive: true,
      search: true,
      identify: true,
      attribution: 'City GIS',
      theme: 'dark',
      label: 'City asset map',
      style: {
        accent: '#0f766e',
        fontFamily: 'Aptos, sans-serif',
      },
    }, {
      elementName: 'city-asset-map',
    });

    expect(snippet).toContain('defineHonuaMapElement(\'city-asset-map\')');
    expect(snippet).toContain('<city-asset-map');
    expect(snippet).toContain('service-url="https://services.example.test/FeatureServer"');
    expect(snippet).toContain('layer-ids="assets,work-orders"');
    expect(snippet).toContain('bbox="-158.3,21.2,-157.6,21.6"');
    expect(snippet).toContain('interactive');
    expect(snippet).toContain('label="City asset map"');
    expect(snippet).toContain('style="--honua-map-accent: #0f766e; --honua-map-font-family: Aptos, sans-serif"');
    expect(snippet).not.toContain('secret-key');
  });

  it('includes credentials only when explicitly requested', () => {
    const snippet = createHonuaMapSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      apiKey: 'public-browser-key',
    }, {
      includeCredentials: true,
      includeScript: false,
    });

    expect(snippet).toContain('api-key="public-browser-key"');
    expect(snippet).not.toContain('<script');
  });

  it('applies map options to an existing element and removes null values', () => {
    const element = document.createElement('honua-map');
    element.setAttribute('service-url', 'https://old.example.test');
    element.setAttribute('interactive', '');
    element.style.setProperty('--honua-map-accent', '#123456');
    element.style.setProperty('--honua-map-background', '#eeeeee');
    document.body.append(element);

    applyHonuaMapOptions(element, {
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets', ''],
      interactive: false,
      search: true,
      style: {
        accent: null,
        surface: '#ffffff',
      },
    });

    expect(element.getAttribute('service-url')).toBe('https://services.example.test/FeatureServer');
    expect(element.getAttribute('layer-ids')).toBe('assets');
    expect(element.hasAttribute('interactive')).toBe(false);
    expect(element.hasAttribute('search')).toBe(true);
    expect(element.style.getPropertyValue('--honua-map-accent')).toBe('');
    expect(element.style.getPropertyValue('--honua-map-background')).toBe('#eeeeee');
    expect(element.style.getPropertyValue('--honua-map-surface')).toBe('#ffffff');
  });
});
