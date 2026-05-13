import { describe, expect, it, beforeEach } from 'vitest';
import type { HonuaMapBuilderElement } from '../src/builder-element';
import {
  addHonuaMapBuilderElementEventListeners,
  addHonuaMapElementEventListeners,
  applyHonuaMapBuilderElementOptions,
  applyHonuaMapElementOptions,
  defineHonuaFrameworkElements,
} from '../src/framework';
import type { HonuaMapElement, HonuaMapSearchDetail } from '../src/map';

describe('framework wrapper bindings', () => {
  beforeEach(() => {
    document.body.replaceChildren();
    defineHonuaFrameworkElements();
  });

  it('applies typed map wrapper options to the custom element', () => {
    const element = document.createElement('honua-map') as HonuaMapElement;
    document.body.append(element);

    applyHonuaMapElementOptions(element, {
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets', 'work-orders'],
      apiKey: 'public-browser-key',
      center: { latitude: 21.3069, longitude: -157.8583 },
      zoom: 12,
      basemap: 'dark',
      interactive: true,
      search: true,
      identify: false,
      label: 'City asset map',
      themeStyle: {
        accent: '#0f766e',
      },
    });

    expect(element.getAttribute('service-url')).toBe('https://services.example.test/FeatureServer');
    expect(element.getAttribute('layer-ids')).toBe('assets,work-orders');
    expect(element.getAttribute('api-key')).toBe('public-browser-key');
    expect(element.getAttribute('center')).toBe('21.3069,-157.8583');
    expect(element.getAttribute('zoom')).toBe('12');
    expect(element.getAttribute('basemap')).toBe('dark');
    expect(element.hasAttribute('interactive')).toBe(true);
    expect(element.hasAttribute('search')).toBe(true);
    expect(element.hasAttribute('identify')).toBe(false);
    expect(element.getAttribute('label')).toBe('City asset map');
    expect(element.style.getPropertyValue('--honua-map-accent')).toBe('#0f766e');
  });

  it('clears map wrapper options when props are removed', () => {
    const element = document.createElement('honua-map') as HonuaMapElement;
    document.body.append(element);

    applyHonuaMapElementOptions(element, {
      serviceUrl: 'https://services.example.test/FeatureServer',
      search: true,
      label: 'City asset map',
      themeStyle: {
        accent: '#0f766e',
      },
    });
    applyHonuaMapElementOptions(element, {});

    expect(element.hasAttribute('service-url')).toBe(false);
    expect(element.hasAttribute('search')).toBe(false);
    expect(element.hasAttribute('label')).toBe(false);
    expect(element.style.getPropertyValue('--honua-map-accent')).toBe('');
  });

  it('wires map custom events to typed handlers', () => {
    const element = document.createElement('honua-map') as HonuaMapElement;
    const observed: HonuaMapSearchDetail[] = [];
    const disconnect = addHonuaMapElementEventListeners(element, {
      search: (detail) => observed.push(detail),
    });
    const detail: HonuaMapSearchDetail = {
      query: 'hydrant',
      config: element.config,
    };

    element.dispatchEvent(new CustomEvent('honua-map-search', { detail }));
    disconnect();
    element.dispatchEvent(new CustomEvent('honua-map-search', { detail }));

    expect(observed).toEqual([detail]);
  });

  it('applies typed builder wrapper options to the builder element', () => {
    const element = document.createElement('honua-map-builder') as HonuaMapBuilderElement;
    document.body.append(element);

    applyHonuaMapBuilderElementOptions(element, {
      serviceUrl: 'https://services.example.test/FeatureServer',
      availableLayers: [
        { id: 'assets', label: 'Assets', defaultSelected: true },
        { id: 'work-orders', label: 'Work orders' },
      ],
      selectedLayerIds: ['assets'],
      target: 'cdn',
      elementName: 'city-asset-map',
      includeCredentials: true,
      scriptUrl: 'https://cdn.example.test/embed.js',
      themeStyle: {
        accent: '#0f766e',
      },
    });

    expect(element.getAttribute('target')).toBe('cdn');
    expect(element.getAttribute('element-name')).toBe('city-asset-map');
    expect(element.hasAttribute('include-credentials')).toBe(true);
    expect(element.getAttribute('script-url')).toBe('https://cdn.example.test/embed.js');
    expect(element.value.serviceUrl).toBe('https://services.example.test/FeatureServer');
    expect(element.value.selectedLayerIds).toEqual(['assets']);
    expect(element.availableLayers.map((layer) => layer.id)).toEqual(['assets', 'work-orders']);
    expect(element.state.snippet).toContain('https://cdn.example.test/embed.js');
  });

  it('wires builder custom events to typed handlers', () => {
    const element = document.createElement('honua-map-builder') as HonuaMapBuilderElement;
    document.body.append(element);
    const observed: string[] = [];
    const disconnect = addHonuaMapBuilderElementEventListeners(element, {
      change: (detail) => observed.push(detail.target),
    });

    applyHonuaMapBuilderElementOptions(element, {
      serviceUrl: 'https://services.example.test/FeatureServer',
      target: 'iframe',
    });
    disconnect();
    applyHonuaMapBuilderElementOptions(element, {
      serviceUrl: 'https://services.example.test/FeatureServer',
      target: 'cdn',
    });

    expect(observed).toContain('iframe');
    expect(observed).not.toContain('cdn');
  });

  it('exports framework adapter entry points', async () => {
    const [react, vue, angular] = await Promise.all([
      import('../src/react'),
      import('../src/vue'),
      import('../src/angular'),
    ]);

    expect(react.HonuaMap).toBeTruthy();
    expect(react.HonuaMapBuilder).toBeTruthy();
    expect(vue.HonuaMap.name).toBe('HonuaMap');
    expect(vue.HonuaMapBuilder.name).toBe('HonuaMapBuilder');
    expect(angular.HonuaMapComponent).toBeTypeOf('function');
    expect(angular.HonuaMapBuilderComponent).toBeTypeOf('function');
    expect(angular.HonuaEmbedModule).toBeTypeOf('function');
  });
});
