import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  hydrateHonuaMapIframe,
  parseHonuaMapIframeConfig,
  type HonuaMapIframeMessage,
} from '../src/iframe';

describe('honua map iframe fallback', () => {
  beforeEach(() => {
    document.body.replaceChildren();
    window.history.replaceState(null, '', '/embed/map.html');
  });

  it('parses map options and parent origin from iframe query parameters', () => {
    const url = new URL('/embed/map.html', window.location.href);
    url.searchParams.set('service-url', 'https://services.example.test/FeatureServer');
    url.searchParams.set('layer-ids', 'assets, work-orders');
    url.searchParams.set('api-key', 'public-browser-key');
    url.searchParams.set('center', '21.3069,-157.8583');
    url.searchParams.set('zoom', '12');
    url.searchParams.set('bbox', '-158.3,21.2,-157.6,21.6');
    url.searchParams.set('basemap', 'dark');
    url.searchParams.set('interactive', 'true');
    url.searchParams.set('search', 'true');
    url.searchParams.set('identify', 'true');
    url.searchParams.set('attribution', 'City GIS');
    url.searchParams.set('theme', 'dark');
    url.searchParams.set('label', 'City asset map');
    url.searchParams.set('--honua-map-accent', '#0f766e');
    url.searchParams.set('--honua-map-font-family', 'Aptos, sans-serif');
    url.searchParams.set('parent-origin', 'https://portal.example.test/admin');

    const config = parseHonuaMapIframeConfig(url);

    expect(config.parentOrigin).toBe('https://portal.example.test');
    expect(config.options).toMatchObject({
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets', 'work-orders'],
      apiKey: 'public-browser-key',
      center: { latitude: 21.3069, longitude: -157.8583 },
      zoom: 12,
      bounds: {
        minLongitude: -158.3,
        minLatitude: 21.2,
        maxLongitude: -157.6,
        maxLatitude: 21.6,
      },
      basemap: 'dark',
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
    });

    const flagConfig = parseHonuaMapIframeConfig(new URLSearchParams('search&interactive=false'));
    expect(flagConfig.options.search).toBe(true);
    expect(flagConfig.options.interactive).toBe(false);
  });

  it('hydrates a full-frame map element from the current URL', () => {
    const mount = document.createElement('div');
    mount.id = 'honua-map-frame-root';
    document.body.append(mount);
    const url = new URL('/embed/map.html', window.location.href);
    url.searchParams.set('service-url', 'https://services.example.test/FeatureServer');
    url.searchParams.set('layer-ids', 'assets');
    url.searchParams.set('search', 'true');
    url.searchParams.set('--honua-map-accent', '#0f766e');
    window.history.replaceState(null, '', url);

    const runtime = hydrateHonuaMapIframe({ parent: null });

    expect(mount.querySelector('honua-map')).toBe(runtime.element);
    expect(runtime.element.getAttribute('service-url')).toBe('https://services.example.test/FeatureServer');
    expect(runtime.element.getAttribute('layer-ids')).toBe('assets');
    expect(runtime.element.hasAttribute('search')).toBe(true);
    expect(runtime.element.style.getPropertyValue('--honua-map-accent')).toBe('#0f766e');
    expect(runtime.element.style.height).toBe('100%');
  });

  it('forwards map events to the parent frame with the scoped target origin', () => {
    const url = new URL('/embed/map.html', window.location.href);
    url.searchParams.set('label', 'City asset map');
    url.searchParams.set('parent-origin', 'https://portal.example.test');
    window.history.replaceState(null, '', url);
    const parent = {
      postMessage: vi.fn<(message: HonuaMapIframeMessage, targetOrigin: string) => void>(),
    };

    const runtime = hydrateHonuaMapIframe({ parent });
    runtime.element.dispatchEvent(new CustomEvent('honua-map-search', {
      bubbles: true,
      composed: true,
      detail: {
        query: 'hydrants',
      },
    }));

    expect(parent.postMessage).toHaveBeenLastCalledWith({
      source: 'honua-map-iframe',
      version: 1,
      type: 'honua-map-search',
      detail: {
        query: 'hydrants',
      },
    }, 'https://portal.example.test');

    runtime.disconnect();
    runtime.element.dispatchEvent(new CustomEvent('honua-map-identify', {
      detail: { x: 12, y: 34 },
    }));

    expect(parent.postMessage).toHaveBeenCalledTimes(2);
  });
});
