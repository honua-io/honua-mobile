import { describe, expect, it, vi } from 'vitest';
import {
  applyHonuaMapBuilderState,
  createHonuaMapBuilderSnippet,
  createHonuaMapBuilderState,
  defineHonuaMapBuilderElement,
  type HonuaMapBuilderElement,
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
      webComponent: {
        elementName: 'field-ops-map',
      },
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

  it('supports existing embed-builder snippet targets', () => {
    const state = createHonuaMapBuilderState({
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets'],
      label: 'CDN map',
    }, {
      target: 'cdn',
      cdn: {
        scriptUrl: 'https://cdn.example.test/embed.js',
        scriptAttributes: {
          integrity: 'sha384-test',
          crossOrigin: 'anonymous',
        },
      },
    });

    expect(state.canGenerateSnippet).toBe(true);
    expect(state.snippet).toContain('src="https://cdn.example.test/embed.js"');
    expect(state.snippet).toContain('integrity="sha384-test"');
    expect(state.snippet).toContain('<honua-map');
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
      webComponent: {
        includeScript: false,
      },
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

  it('renders an admin builder element with live preview and credential-safe output', () => {
    defineHonuaMapBuilderElement();
    const element = document.createElement('honua-map-builder') as HonuaMapBuilderElement;
    element.availableLayers = [
      { id: 'assets', label: 'Assets', description: 'Field assets', defaultSelected: true },
      { id: 'drafts', label: 'Drafts', disabled: true },
    ];
    element.setAttribute('service-url', 'https://services.example.test/FeatureServer');
    element.setAttribute('api-key', 'secret-browser-key');
    element.setAttribute('layer-ids', 'assets,drafts');
    element.setAttribute('interactive', '');
    element.setAttribute('search', '');
    document.body.append(element);

    expect(element.state.canGenerateSnippet).toBe(true);
    expect(element.state.selectedLayerIds).toEqual(['assets']);
    expect(element.state.issues.map((issue) => issue.code)).toContain('credentials-omitted');
    expect(element.shadowRoot!.querySelector('honua-map')!.getAttribute('layer-ids')).toBe('assets');
    expect(element.shadowRoot!.querySelector<HTMLTextAreaElement>('[name="snippet"]')!.value)
      .not.toContain('secret-browser-key');
  });

  it('initializes catalog default selections when layer ids are not explicit', () => {
    defineHonuaMapBuilderElement();
    const element = document.createElement('honua-map-builder') as HonuaMapBuilderElement;
    element.availableLayers = [
      { id: 'assets', label: 'Assets', defaultSelected: true },
      { id: 'work-orders', label: 'Work orders' },
    ];
    element.setAttribute('service-url', 'https://services.example.test/FeatureServer');
    document.body.append(element);

    expect(element.state.selectedLayerIds).toEqual(['assets']);
    expect(element.shadowRoot!.querySelector<HTMLInputElement>('input[name="selectedLayer"]')!.checked)
      .toBe(true);
    expect(element.shadowRoot!.querySelector<HTMLTextAreaElement>('[name="snippet"]')!.value)
      .toContain('layer-ids="assets"');
  });

  it('preserves catalog metadata when restoring a captured builder value', () => {
    defineHonuaMapBuilderElement();
    const element = document.createElement('honua-map-builder') as HonuaMapBuilderElement;
    element.value = {
      serviceUrl: 'https://services.example.test/FeatureServer',
      availableLayers: [
        { id: 'assets', label: 'Assets', description: 'Field assets' },
        { id: 'drafts', label: 'Drafts', disabled: true },
      ],
      selectedLayerIds: ['assets'],
    };
    document.body.append(element);

    expect(element.availableLayers).toEqual([
      expect.objectContaining({ id: 'assets', label: 'Assets' }),
      expect.objectContaining({ id: 'drafts', disabled: true }),
    ]);
    expect(element.shadowRoot!.querySelector('[data-layer-text]')!.hasAttribute('hidden')).toBe(true);
    expect([...element.shadowRoot!.querySelectorAll('.layer-option')].map((item) => item.textContent))
      .toEqual(['AssetsField assets', 'Drafts']);
  });

  it('updates builder output target from the element form', () => {
    defineHonuaMapBuilderElement();
    const element = document.createElement('honua-map-builder') as HonuaMapBuilderElement;
    element.value = {
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets'],
      label: 'Asset map',
    };
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-map-builder-change', listener);

    const target = element.shadowRoot!.querySelector<HTMLSelectElement>('[name="target"]')!;
    const iframeUrl = element.shadowRoot!.querySelector<HTMLInputElement>('[name="iframeUrl"]')!;
    target.value = 'iframe';
    iframeUrl.value = 'https://cdn.example.test/embed/map.html';
    target.dispatchEvent(new Event('change', { bubbles: true }));

    expect(element.state.snippet).toContain('<iframe');
    expect(element.state.snippet).toContain('https://cdn.example.test/embed/map.html');
    expect(listener).toHaveBeenCalled();
  });
});
