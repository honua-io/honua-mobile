import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  defineHonuaSceneElement,
  HonuaSceneElement,
  HonuaScenePackageCacheError,
} from '../src/index';

interface MockCesiumModule {
  CesiumTerrainProvider: {
    fromUrl: ReturnType<typeof vi.fn>;
  };
  Ion: {
    defaultAccessToken: string;
  };
  __mock: {
    widgets: Array<{
      destroy: ReturnType<typeof vi.fn>;
    }>;
  };
}

vi.mock('cesium', () => {
  const widgets: MockCesiumModule['__mock']['widgets'] = [];

  class MockCesiumWidget {
    readonly canvas = document.createElement('canvas');
    readonly camera = {
      changed: {
        addEventListener: vi.fn(() => vi.fn()),
      },
      heading: 0,
      pitch: 0,
      roll: 0,
      positionCartographic: {
        latitude: 0,
        longitude: 0,
        height: 0,
      },
      setView: vi.fn(),
    };
    readonly scene = {
      primitives: {
        add: vi.fn(),
      },
      pick: vi.fn(),
      requestRender: vi.fn(),
    };
    readonly destroy = vi.fn(() => {
      this.#destroyed = true;
      this.#container.replaceChildren();
    });
    readonly #container: HTMLElement;
    #destroyed = false;

    constructor(container: HTMLElement) {
      this.#container = container;
      const widget = document.createElement('div');
      widget.className = 'cesium-widget';
      this.#container.append(widget);
      widgets.push(this);
    }

    isDestroyed(): boolean {
      return this.#destroyed;
    }

    async zoomTo(): Promise<void> {
      return Promise.resolve();
    }
  }

  return {
    buildModuleUrl: Object.assign(vi.fn(), { setBaseUrl: vi.fn() }),
    Cartesian3: {
      fromDegrees: vi.fn((longitude: number, latitude: number, height: number) => ({
        longitude,
        latitude,
        height,
      })),
    },
    Cesium3DTileset: {
      fromUrl: vi.fn(async (url: string) => ({ url })),
    },
    CesiumTerrainProvider: {
      fromUrl: vi.fn(async (url: string) => ({ url })),
    },
    CesiumWidget: MockCesiumWidget,
    Ion: {
      defaultAccessToken: '',
    },
    Math: {
      toDegrees: vi.fn((value: number) => value * (180 / globalThis.Math.PI)),
      toRadians: vi.fn((value: number) => value * (globalThis.Math.PI / 180)),
    },
    ScreenSpaceEventHandler: class {
      readonly setInputAction = vi.fn();
      #destroyed = false;

      destroy(): void {
        this.#destroyed = true;
      }

      isDestroyed(): boolean {
        return this.#destroyed;
      }
    },
    ScreenSpaceEventType: {
      LEFT_CLICK: 0,
    },
    __mock: {
      widgets,
    },
  };
});

describe('honua-scene', () => {
  let cesium: MockCesiumModule;

  beforeEach(async () => {
    cesium = await import('cesium') as unknown as MockCesiumModule;
    vi.clearAllMocks();
    defineHonuaSceneElement();
    document.body.replaceChildren();
    cesium.__mock.widgets.length = 0;
    cesium.Ion.defaultAccessToken = '';
  });

  it('defines the custom element idempotently', () => {
    const first = defineHonuaSceneElement();
    const second = defineHonuaSceneElement();

    expect(first).toBe(HonuaSceneElement);
    expect(second).toBe(HonuaSceneElement);
    expect(customElements.get('honua-scene')).toBe(HonuaSceneElement);
  });

  it('parses declarative attributes into scene config', () => {
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/tileset.json');
    element.setAttribute('terrain-url', 'https://data.example.test/terrain');
    element.setAttribute('package-id', 'pkg-downtown');
    element.setAttribute('tileset-asset', 'tilesets/buildings/tileset.json');
    element.setAttribute('terrain-asset', 'terrain/layer.json');
    element.setAttribute('package-expires-at', '2026-06-27T00:00:00Z');
    element.setAttribute('ion-token', 'secret-ion-token');
    element.setAttribute('cesium-base-url', '/assets/cesium');
    element.setAttribute('center', '21.3069,-157.8583');
    element.setAttribute('height', '1800');
    element.setAttribute('heading', '30');
    element.setAttribute('pitch', '-35');
    element.setAttribute('roll', '2');
    element.setAttribute('theme', 'light');
    element.setAttribute('autoload', 'false');

    document.body.append(element);

    expect(element.config).toMatchObject({
      tilesetUrl: 'https://data.example.test/tileset.json',
      terrainUrl: 'https://data.example.test/terrain',
      packageId: 'pkg-downtown',
      tilesetAssetPath: 'tilesets/buildings/tileset.json',
      terrainAssetPath: 'terrain/layer.json',
      packageExpiresAtUtc: '2026-06-27T00:00:00Z',
      ionToken: 'secret-ion-token',
      cesiumBaseUrl: '/assets/cesium/',
      center: { latitude: 21.3069, longitude: -157.8583 },
      height: 1800,
      orientation: {
        heading: 30,
        pitch: -35,
        roll: 2,
      },
      theme: 'light',
      autoload: false,
    });
  });

  it('updates camera attributes through the public API', () => {
    const element = document.createElement('honua-scene');
    document.body.append(element);

    element.setView(
      { latitude: 20.75, longitude: -156.45 },
      2500,
      { heading: 90, pitch: -50, roll: 0 },
    );

    expect(element.getAttribute('center')).toBe('20.75,-156.45');
    expect(element.getAttribute('height')).toBe('2500');
    expect(element.getAttribute('heading')).toBe('90');
    expect(element.getAttribute('pitch')).toBe('-50');
    expect(element.getAttribute('roll')).toBe('0');
  });

  it('emits an actionable load error when WebGL is unavailable', async () => {
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/tileset.json');
    element.setAttribute('autoload', 'false');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-scene-load-error', listener);

    await element.load();

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      source: 'webgl',
      message: '3D scenes require WebGL support in the host browser.',
    });
  });

  it('resolves package-local scene assets through a host-provided resolver', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('package-id', 'pkg-downtown');
    element.setAttribute('tileset-asset', 'tilesets/buildings/tileset.json');
    element.setAttribute('terrain-asset', 'terrain/layer.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    const resolver = vi.fn((request) => `https://cache.example.test/${request.packageId}/${request.path}`);
    element.packageAssetResolver = resolver;
    document.body.append(element);

    await element.load();

    expect(resolver).toHaveBeenCalledWith(expect.objectContaining({
      packageId: 'pkg-downtown',
      path: 'tilesets/buildings/tileset.json',
      kind: 'tileset',
    }));
    expect(resolver).toHaveBeenCalledWith(expect.objectContaining({
      packageId: 'pkg-downtown',
      path: 'terrain/layer.json',
      kind: 'terrain',
    }));
    expect(cesium.CesiumTerrainProvider.fromUrl).toHaveBeenCalledWith(
      'https://cache.example.test/pkg-downtown/terrain/layer.json',
    );
    webgl.mockRestore();
  });

  it('surfaces unsupported browser storage when package assets have no resolver', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('package-id', 'pkg-downtown');
    element.setAttribute('tileset-asset', 'tilesets/buildings/tileset.json');
    element.setAttribute('autoload', 'false');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-scene-load-error', listener);

    await element.load();

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      source: 'package-cache',
      code: 'unsupported-browser-storage',
    });
    webgl.mockRestore();
  });

  it('surfaces cache misses and expired package grants before Cesium loads', async () => {
    const webgl = mockWebGl();
    const missing = document.createElement('honua-scene');
    missing.setAttribute('package-id', 'pkg-downtown');
    missing.setAttribute('tileset-asset', 'tilesets/missing/tileset.json');
    missing.setAttribute('autoload', 'false');
    missing.packageAssetResolver = () => {
      throw new HonuaScenePackageCacheError('cache-miss', 'missing tileset');
    };
    document.body.append(missing);
    const missingListener = vi.fn();
    missing.addEventListener('honua-scene-load-error', missingListener);

    await missing.load();

    expect(missingListener.mock.calls[0][0].detail).toMatchObject({
      source: 'package-cache',
      code: 'cache-miss',
      message: 'missing tileset',
    });

    const expired = document.createElement('honua-scene');
    expired.setAttribute('package-id', 'pkg-expired');
    expired.setAttribute('tileset-asset', 'tilesets/buildings/tileset.json');
    expired.setAttribute('package-expires-at', '2000-01-01T00:00:00Z');
    expired.setAttribute('autoload', 'false');
    const expiredResolver = vi.fn(() => 'https://cache.example.test/tileset.json');
    expired.packageAssetResolver = expiredResolver;
    document.body.append(expired);
    const expiredListener = vi.fn();
    expired.addEventListener('honua-scene-load-error', expiredListener);

    await expired.load();

    expect(expiredListener.mock.calls[0][0].detail).toMatchObject({
      source: 'package-cache',
      code: 'expired-package',
    });
    expect(expiredResolver).not.toHaveBeenCalled();
    webgl.mockRestore();
  });

  it('does not render access tokens or Honua branding by default', () => {
    const element = document.createElement('honua-scene');
    element.setAttribute('ion-token', 'do-not-render');
    document.body.append(element);

    const visibleText = [
      ...element.shadowRoot!.querySelectorAll('.status'),
    ].map((node) => node.textContent).join(' ');

    expect(visibleText).not.toContain('do-not-render');
    expect(visibleText.toLowerCase()).not.toContain('honua');
  });

  it('tears down the existing scene when data URLs are cleared', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/tileset.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    await element.load();
    expect(cesium.__mock.widgets).toHaveLength(1);

    element.removeAttribute('tileset-url');
    await element.load();

    expect(cesium.__mock.widgets[0].destroy).toHaveBeenCalledOnce();
    expect(element.cesiumWidget).toBeNull();
    webgl.mockRestore();
  });

  it('tears down the existing scene when package resolution fails', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('package-id', 'pkg-downtown');
    element.setAttribute('tileset-asset', 'tilesets/buildings/tileset.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    element.packageAssetResolver = () => 'https://cache.example.test/tileset.json';
    document.body.append(element);

    await element.load();
    expect(cesium.__mock.widgets).toHaveLength(1);

    const listener = vi.fn();
    element.addEventListener('honua-scene-load-error', listener);
    element.setAttribute('package-expires-at', '2000-01-01T00:00:00Z');
    await element.load();

    expect(listener.mock.calls[0][0].detail).toMatchObject({
      source: 'package-cache',
      code: 'expired-package',
    });
    expect(cesium.__mock.widgets[0].destroy).toHaveBeenCalledOnce();
    expect(element.cesiumWidget).toBeNull();
    webgl.mockRestore();
  });

  it('cancels in-flight loads when disconnected', async () => {
    const webgl = mockWebGl();
    let resolveTerrain!: (value: unknown) => void;
    const terrainStarted = new Promise<void>((resolve) => {
      cesium.CesiumTerrainProvider.fromUrl.mockImplementationOnce(async () => {
        resolve();
        return await new Promise((terrainResolve) => {
          resolveTerrain = terrainResolve;
        });
      });
    });
    const element = document.createElement('honua-scene');
    element.setAttribute('terrain-url', 'https://data.example.test/terrain');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    const loading = element.load();
    await terrainStarted;
    element.remove();
    resolveTerrain({});
    await loading;

    expect(cesium.__mock.widgets).toHaveLength(0);
    webgl.mockRestore();
  });

  it('clears the Cesium Ion token when the attribute is removed', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/tileset.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('ion-token', 'first-token');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    await element.load();
    expect(cesium.Ion.defaultAccessToken).toBe('first-token');

    element.removeAttribute('ion-token');
    await element.load();

    expect(cesium.Ion.defaultAccessToken).toBe('');
    webgl.mockRestore();
  });
});

function mockWebGl() {
  return vi
    .spyOn(HTMLCanvasElement.prototype, 'getContext')
    .mockReturnValue({} as RenderingContext);
}
