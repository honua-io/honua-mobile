import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  addHonuaMapIframeMessageListener,
  hydrateHonuaMapIframe,
  isHonuaMapIframeCommand,
  isHonuaMapIframeMessage,
  parseHonuaMapIframeConfig,
  postHonuaMapIframeConfigure,
  type HonuaMapIframeCommand,
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

    const invalidOriginConfig = parseHonuaMapIframeConfig(new URLSearchParams('parent-origin=portal.example.test'));
    expect(invalidOriginConfig.parentOrigin).toBeNull();
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

  it('does not forward events when parent-origin is invalid', () => {
    const url = new URL('/embed/map.html', window.location.href);
    url.searchParams.set('parent-origin', 'portal.example.test');
    window.history.replaceState(null, '', url);
    const parent = {
      postMessage: vi.fn<(message: HonuaMapIframeMessage, targetOrigin: string) => void>(),
    };

    const runtime = hydrateHonuaMapIframe({ parent });
    runtime.element.dispatchEvent(new CustomEvent('honua-map-search', {
      detail: { query: 'hydrants' },
    }));

    expect(runtime.config.parentOrigin).toBeNull();
    expect(parent.postMessage).not.toHaveBeenCalled();
  });

  it('identifies iframe fallback messages by source, version, and forwarded event type', () => {
    expect(isHonuaMapIframeMessage({
      source: 'honua-map-iframe',
      version: 1,
      type: 'honua-map-search',
      detail: { query: 'hydrants' },
    })).toBe(true);

    expect(isHonuaMapIframeMessage({
      source: 'other-frame',
      version: 1,
      type: 'honua-map-search',
      detail: {},
    })).toBe(false);

    expect(isHonuaMapIframeMessage({
      source: 'honua-map-iframe',
      version: 1,
      type: 'unscoped-event',
      detail: {},
    })).toBe(false);
  });

  it('posts typed configure commands to a map iframe target', () => {
    const target = {
      postMessage: vi.fn<(message: HonuaMapIframeCommand, targetOrigin: string) => void>(),
    };

    postHonuaMapIframeConfigure(target, {
      basemap: 'satellite',
      search: true,
    }, 'https://cdn.honua.dev');

    expect(target.postMessage).toHaveBeenCalledWith({
      source: 'honua-map-host',
      version: 1,
      type: 'honua-map-configure',
      options: {
        basemap: 'satellite',
        search: true,
      },
    }, 'https://cdn.honua.dev');
  });

  it('identifies iframe configure commands by source, version, type, and options', () => {
    expect(isHonuaMapIframeCommand({
      source: 'honua-map-host',
      version: 1,
      type: 'honua-map-configure',
      options: {
        basemap: 'satellite',
      },
    })).toBe(true);

    expect(isHonuaMapIframeCommand({
      source: 'honua-map-iframe',
      version: 1,
      type: 'honua-map-configure',
      options: {},
    })).toBe(false);

    expect(isHonuaMapIframeCommand({
      source: 'honua-map-host',
      version: 1,
      type: 'honua-map-configure',
      options: null,
    })).toBe(false);
  });

  it('applies configure commands from the declared parent origin and source', () => {
    const parent = {
      postMessage: vi.fn<(message: HonuaMapIframeMessage, targetOrigin: string) => void>(),
    };
    const parentSource = parent as unknown as MessageEventSource;

    const url = new URL('/embed/map.html', window.location.href);
    url.searchParams.set('service-url', 'https://services.example.test/FeatureServer');
    url.searchParams.set('layer-ids', 'assets');
    url.searchParams.set('search', 'false');
    url.searchParams.set('--honua-map-accent', '#0f766e');
    url.searchParams.set('parent-origin', 'https://portal.example.test');
    window.history.replaceState(null, '', url);

    const runtime = hydrateHonuaMapIframe({ parent });

    window.dispatchEvent(new MessageEvent('message', {
      data: {
        source: 'honua-map-host',
        version: 1,
        type: 'honua-map-configure',
        options: {
          basemap: 'satellite',
          search: true,
        },
      },
      origin: 'https://other.example.test',
      source: parentSource,
    }));
    window.dispatchEvent(new MessageEvent('message', {
      data: {
        source: 'honua-map-host',
        version: 1,
        type: 'honua-map-configure',
        options: {
          basemap: 'dark',
          identify: true,
        },
      },
      origin: 'https://portal.example.test',
      source: window,
    }));
    window.dispatchEvent(new MessageEvent('message', {
      data: {
        source: 'honua-map-host',
        version: 1,
        type: 'honua-map-configure',
        options: {
          basemap: 'streets',
          search: true,
          style: {
            fontFamily: 'Aptos, sans-serif',
          },
        },
      },
      origin: 'https://portal.example.test',
      source: parentSource,
    }));

    expect(runtime.element.getAttribute('basemap')).toBe('streets');
    expect(runtime.element.hasAttribute('search')).toBe(true);
    expect(runtime.element.hasAttribute('identify')).toBe(false);
    expect(runtime.element.style.getPropertyValue('--honua-map-font-family')).toBe('Aptos, sans-serif');
    expect(runtime.config.options).toMatchObject({
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets'],
      basemap: 'streets',
      search: true,
      style: {
        accent: '#0f766e',
        fontFamily: 'Aptos, sans-serif',
      },
    });

    window.dispatchEvent(new MessageEvent('message', {
      data: {
        source: 'honua-map-host',
        version: 1,
        type: 'honua-map-configure',
        options: {
          basemap: undefined,
          search: undefined,
          style: {
            fontFamily: undefined,
            foreground: '#111827',
          },
        },
      },
      origin: 'https://portal.example.test',
      source: parentSource,
    }));

    expect(runtime.element.getAttribute('basemap')).toBe('streets');
    expect(runtime.element.hasAttribute('search')).toBe(true);
    expect(runtime.element.style.getPropertyValue('--honua-map-font-family')).toBe('Aptos, sans-serif');
    expect(runtime.element.style.getPropertyValue('--honua-map-foreground')).toBe('#111827');
    expect(runtime.config.options).toMatchObject({
      basemap: 'streets',
      search: true,
      style: {
        accent: '#0f766e',
        fontFamily: 'Aptos, sans-serif',
        foreground: '#111827',
      },
    });

    runtime.disconnect();
    window.dispatchEvent(new MessageEvent('message', {
      data: {
        source: 'honua-map-host',
        version: 1,
        type: 'honua-map-configure',
        options: {
          basemap: 'topographic',
        },
      },
      origin: 'https://portal.example.test',
      source: parentSource,
    }));

    expect(runtime.element.getAttribute('basemap')).toBe('streets');
  });

  it('does not listen for configure commands when parent-origin is invalid', () => {
    const parent = {
      postMessage: vi.fn<(message: HonuaMapIframeMessage, targetOrigin: string) => void>(),
    };

    const url = new URL('/embed/map.html', window.location.href);
    url.searchParams.set('parent-origin', 'portal.example.test');
    window.history.replaceState(null, '', url);

    const runtime = hydrateHonuaMapIframe({ parent });
    window.dispatchEvent(new MessageEvent('message', {
      data: {
        source: 'honua-map-host',
        version: 1,
        type: 'honua-map-configure',
        options: {
          basemap: 'satellite',
        },
      },
      origin: 'https://portal.example.test',
      source: parent as unknown as MessageEventSource,
    }));

    expect(runtime.config.parentOrigin).toBeNull();
    expect(runtime.element.hasAttribute('basemap')).toBe(false);
  });

  it('subscribes to iframe fallback messages with origin and source filtering', () => {
    const sourceFrame = document.createElement('iframe');
    document.body.append(sourceFrame);
    const source = sourceFrame.contentWindow;
    const listener = vi.fn<(message: HonuaMapIframeMessage, event: MessageEvent) => void>();
    const message: HonuaMapIframeMessage = {
      source: 'honua-map-iframe',
      version: 1,
      type: 'honua-map-identify',
      detail: { x: 12, y: 34 },
    };
    const disconnect = addHonuaMapIframeMessageListener(listener, {
      origin: 'https://portal.example.test/admin/embed',
      source,
    });

    window.dispatchEvent(new MessageEvent('message', {
      data: message,
      origin: 'https://other.example.test',
      source,
    }));
    window.dispatchEvent(new MessageEvent('message', {
      data: message,
      origin: 'https://portal.example.test',
      source: window,
    }));
    window.dispatchEvent(new MessageEvent('message', {
      data: message,
      origin: 'https://portal.example.test',
      source,
    }));

    expect(listener).toHaveBeenCalledTimes(1);
    expect(listener.mock.calls[0]?.[0]).toBe(message);
    expect(listener.mock.calls[0]?.[1].origin).toBe('https://portal.example.test');

    disconnect();
    window.dispatchEvent(new MessageEvent('message', {
      data: message,
      origin: 'https://portal.example.test',
      source,
    }));

    expect(listener).toHaveBeenCalledTimes(1);
  });
});
