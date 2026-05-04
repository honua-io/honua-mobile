import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  defineHonuaSceneElement,
  HonuaSceneElement,
  HonuaScenePackageCacheError,
} from '../src/index';

interface MockCesiumWidgetSnapshot {
  destroy: ReturnType<typeof vi.fn>;
  scene: {
    primitives: {
      add: ReturnType<typeof vi.fn>;
      remove: ReturnType<typeof vi.fn>;
    };
    pickPosition: ReturnType<typeof vi.fn>;
    requestRender: ReturnType<typeof vi.fn>;
  };
}

interface MockCesiumModule {
  Cesium3DTileset: {
    fromUrl: ReturnType<typeof vi.fn>;
  };
  CesiumTerrainProvider: {
    fromUrl: ReturnType<typeof vi.fn>;
  };
  Ion: {
    defaultAccessToken: string;
  };
  __mock: {
    widgets: MockCesiumWidgetSnapshot[];
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
        remove: vi.fn(),
      },
      pick: vi.fn(),
      pickPosition: vi.fn(),
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
    Cartographic: {
      fromCartesian: vi.fn((cartesian: { x?: number; y?: number; z?: number }) => ({
        latitude: (cartesian?.y ?? 0) / 100,
        longitude: (cartesian?.x ?? 0) / 100,
        height: cartesian?.z ?? 0,
      })),
    },
    Cesium3DTileset: {
      fromUrl: vi.fn(async (url: string) => ({ url, show: true, style: undefined })),
    },
    Cesium3DTileStyle: vi.fn(function MockTileStyle(this: { color?: string }, opts: { color?: string }) {
      this.color = opts?.color;
    }),
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

  it('does not reload the implicit primary layer after tileset-url is removed', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/primary.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    await element.load();
    expect(element.getLayer('primary')?.metadata.url).toBe(
      'https://data.example.test/primary.json',
    );

    element.removeAttribute('tileset-url');
    await element.load();
    expect(element.getLayer('primary')).toBeNull();

    cesium.Cesium3DTileset.fromUrl.mockClear();
    element.setAttribute('terrain-url', 'https://data.example.test/terrain');
    await element.load();

    const reloadedFromStale = cesium.Cesium3DTileset.fromUrl.mock.calls.some(
      (call) => call[0] === 'https://data.example.test/primary.json',
    );
    expect(reloadedFromStale).toBe(false);
    expect(element.getLayer('primary')).toBeNull();

    webgl.mockRestore();
  });

  it('replaces the implicit primary handle when tileset-url is repointed', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/v1.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    await element.load();
    expect(element.getLayer('primary')?.metadata.url).toBe(
      'https://data.example.test/v1.json',
    );

    element.setAttribute('tileset-url', 'https://data.example.test/v2.json');
    await element.load();

    expect(element.getLayer('primary')?.metadata.url).toBe(
      'https://data.example.test/v2.json',
    );

    webgl.mockRestore();
  });

  it('preserves a metadata-declared primary layer when tileset-url is removed', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/override.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    element.metadata = {
      schema: 'honua-scene-metadata/v1',
      id: 'demo',
      name: 'Demo',
      layers: [
        {
          id: 'primary',
          title: 'Primary from metadata',
          kind: '3d-tiles',
          url: 'https://data.example.test/metadata-primary.json',
        },
      ],
    };

    await element.load();

    element.removeAttribute('tileset-url');
    await element.load();

    expect(element.getLayer('primary')).not.toBeNull();
    webgl.mockRestore();
  });

  it('lets metadata-declared primary win over tileset-url for both URL and fields', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/attribute-primary.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    element.metadata = {
      schema: 'honua-scene-metadata/v1',
      id: 'demo',
      name: 'Demo',
      layers: [
        {
          id: 'primary',
          title: 'Site capture (metadata)',
          description: 'As-built primary tileset from the scene document.',
          kind: '3d-tiles',
          url: 'https://data.example.test/metadata-primary.json',
          opacity: 0.6,
          visible: false,
        },
      ],
    };

    await element.load();

    const fromUrlCalls = cesium.Cesium3DTileset.fromUrl.mock.calls.map((call) => call[0]);
    expect(fromUrlCalls).toContain('https://data.example.test/metadata-primary.json');
    expect(fromUrlCalls).not.toContain('https://data.example.test/attribute-primary.json');

    const handle = element.getLayer('primary');
    expect(handle?.metadata.url).toBe('https://data.example.test/metadata-primary.json');
    expect(handle?.metadata.title).toBe('Site capture (metadata)');
    expect(handle?.metadata.description).toBe('As-built primary tileset from the scene document.');
    expect(handle?.metadata.opacity).toBe(0.6);
    expect(handle?.metadata.visible).toBe(false);

    webgl.mockRestore();
  });

  it('uses the metadata-declared primary tileset for the public tileset getter and ready event', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    element.setAttribute('terrain-url', 'https://data.example.test/terrain');
    document.body.append(element);

    element.metadata = {
      schema: 'honua-scene-metadata/v1',
      id: 'demo',
      name: 'Demo',
      layers: [
        {
          id: 'primary',
          title: 'Primary from metadata',
          kind: '3d-tiles',
          url: 'https://data.example.test/metadata-primary.json',
        },
      ],
    };

    const ready = vi.fn();
    element.addEventListener('honua-scene-ready', ready);

    await element.load();

    const handle = element.getLayer('primary');
    expect(handle?.tileset).not.toBeNull();
    expect(element.tileset).toBe(handle?.tileset);
    expect(ready).toHaveBeenCalledOnce();
    expect(ready.mock.calls[0][0].detail.tileset).toBe(handle?.tileset);

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

  it('parses metadata-url and emits honua-scene-metadata-change', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(JSON.stringify({
      schema: 'honua-scene-metadata/v1',
      id: 'demo',
      name: 'Demo',
      layers: [{ id: 'as-built', title: 'As-built', kind: '3d-tiles', url: 'https://example.test/a.json' }],
    }), { status: 200, headers: { 'content-type': 'application/json' } }));

    const element = document.createElement('honua-scene');
    element.setAttribute('autoload', 'false');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-scene-metadata-change', listener);
    element.setAttribute('metadata-url', 'https://example.test/metadata.json');

    await new Promise((resolve) => setTimeout(resolve, 0));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(fetchSpy).toHaveBeenCalledWith('https://example.test/metadata.json', expect.objectContaining({ credentials: 'same-origin' }));
    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      source: 'fetch',
      metadata: expect.objectContaining({
        id: 'demo',
        layers: [expect.objectContaining({ id: 'as-built' })],
      }),
    });

    fetchSpy.mockRestore();
  });

  it('emits honua-scene-metadata-error when the document is invalid', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValueOnce(new Response(JSON.stringify({
      schema: 'unsupported',
      id: 'demo',
      name: 'Demo',
    }), { status: 200, headers: { 'content-type': 'application/json' } }));

    const element = document.createElement('honua-scene');
    element.setAttribute('autoload', 'false');
    element.setAttribute('metadata-url', 'https://example.test/bad-metadata.json');
    document.body.append(element);
    const errorListener = vi.fn();
    element.addEventListener('honua-scene-metadata-error', errorListener);

    await new Promise((resolve) => setTimeout(resolve, 0));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(errorListener).toHaveBeenCalledOnce();
    expect(errorListener.mock.calls[0][0].detail).toMatchObject({
      code: 'metadata-invalid',
      url: 'https://example.test/bad-metadata.json',
    });

    fetchSpy.mockRestore();
  });

  it('exposes metadata via a property setter without fetching', () => {
    const element = document.createElement('honua-scene');
    element.setAttribute('autoload', 'false');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-scene-metadata-change', listener);

    element.metadata = {
      schema: 'honua-scene-metadata/v1',
      id: 'demo',
      name: 'Demo',
      layers: [
        { id: 'a', title: 'A', kind: '3d-tiles', url: 'https://example.test/a.json' },
        { id: 'b', title: 'B', kind: '3d-tiles', url: 'https://example.test/b.json' },
      ],
    };

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail.source).toBe('property');
    expect(element.metadata?.layers).toHaveLength(2);
  });

  it('addLayer / setLayerVisibility / removeLayer emit honua-scene-layer-change', () => {
    const element = document.createElement('honua-scene');
    element.setAttribute('autoload', 'false');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-scene-layer-change', listener);

    void element.addLayer({
      id: 'drone-orthomosaic',
      title: 'Drone orthomosaic',
      kind: '3d-tiles',
      url: 'https://example.test/ortho.json',
    });
    element.setLayerVisibility('drone-orthomosaic', false);
    element.removeLayer('drone-orthomosaic');

    const reasons = listener.mock.calls.map((call) => call[0].detail.reason);
    expect(reasons).toEqual(['added', 'visibility', 'removed']);
  });

  it('applyView writes camera attributes from a typed view spec', () => {
    const element = document.createElement('honua-scene');
    document.body.append(element);
    element.applyView({
      center: { latitude: 21.31, longitude: -157.86 },
      height: 800,
      orientation: { heading: 45, pitch: -30, roll: 0 },
    });

    expect(element.getAttribute('center')).toBe('21.31,-157.86');
    expect(element.getAttribute('height')).toBe('800');
    expect(element.getAttribute('heading')).toBe('45');
  });

  it('detaches an existing layer tileset when re-added with a different URL', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/primary.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    await element.load();
    const widget = cesium.__mock.widgets[0];

    await element.addLayer({
      id: 'design-overlay',
      title: 'Design overlay',
      kind: '3d-tiles',
      url: 'https://data.example.test/first.json',
    });
    const original = element.getLayer('design-overlay')?.tileset;
    expect(original).toBeDefined();

    await element.addLayer({
      id: 'design-overlay',
      title: 'Design overlay',
      kind: '3d-tiles',
      url: 'https://data.example.test/second.json',
    });

    expect(widget.scene.primitives.remove).toHaveBeenCalledWith(original);
    expect((element.getLayer('design-overlay')?.tileset as { url?: string } | null)?.url).toBe(
      'https://data.example.test/second.json',
    );

    webgl.mockRestore();
  });

  it('drops stale layer-tileset loads after a URL change', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/primary.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    await element.load();
    const widget = cesium.__mock.widgets[0];

    type StaleTileset = { url: string; show: boolean; style: undefined };
    let resolveStale: (value: StaleTileset) => void = () => {};
    cesium.Cesium3DTileset.fromUrl.mockImplementationOnce(
      () =>
        new Promise<StaleTileset>((resolve) => {
          resolveStale = resolve;
        }),
    );

    const stalePromise = element.addLayer({
      id: 'design-overlay',
      title: 'Design overlay',
      kind: '3d-tiles',
      url: 'https://data.example.test/v1.json',
    });

    await element.addLayer({
      id: 'design-overlay',
      title: 'Design overlay',
      kind: '3d-tiles',
      url: 'https://data.example.test/v2.json',
    });

    resolveStale({ url: 'https://data.example.test/v1.json', show: true, style: undefined });
    await stalePromise;

    const tileset = element.getLayer('design-overlay')?.tileset as { url?: string } | null;
    expect(tileset?.url).toBe('https://data.example.test/v2.json');
    expect(
      widget.scene.primitives.add.mock.calls.some(
        (call) => (call[0] as { url?: string } | undefined)?.url === 'https://data.example.test/v1.json',
      ),
    ).toBe(false);

    webgl.mockRestore();
  });

  it('destroys a primary tileset that resolves after the element disconnects', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/v1.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    type StaleTileset = {
      url: string;
      show: boolean;
      style: undefined;
      destroy: ReturnType<typeof vi.fn>;
      isDestroyed: () => boolean;
    };
    let staleDestroyed = false;
    let resolveStale: (value: StaleTileset) => void = () => {};
    cesium.Cesium3DTileset.fromUrl.mockImplementationOnce(
      () =>
        new Promise<StaleTileset>((resolve) => {
          resolveStale = resolve;
        }),
    );

    const errorListener = vi.fn();
    element.addEventListener('honua-scene-load-error', errorListener);

    const stalePromise = element.load();
    await new Promise((resolve) => setTimeout(resolve, 0));
    const widget = cesium.__mock.widgets[cesium.__mock.widgets.length - 1];

    element.remove();

    resolveStale({
      url: 'https://data.example.test/v1.json',
      show: true,
      style: undefined,
      destroy: vi.fn(() => {
        staleDestroyed = true;
      }),
      isDestroyed: () => staleDestroyed,
    });
    await stalePromise;

    expect(staleDestroyed).toBe(true);
    expect(element.tileset).toBeNull();
    expect(
      widget.scene.primitives.add.mock.calls.some(
        (call) => (call[0] as { url?: string } | undefined)?.url === 'https://data.example.test/v1.json',
      ),
    ).toBe(false);
    expect(errorListener).not.toHaveBeenCalled();

    webgl.mockRestore();
  });

  it('discards a metadata-layer tileset that resolves after the scene reconnects', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/primary.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    await element.load();

    type StaleTileset = {
      url: string;
      show: boolean;
      style: undefined;
      destroy: ReturnType<typeof vi.fn>;
      isDestroyed: () => boolean;
    };
    let staleDestroyed = false;
    let resolveStale: (value: StaleTileset) => void = () => {};
    cesium.Cesium3DTileset.fromUrl.mockImplementationOnce(
      () =>
        new Promise<StaleTileset>((resolve) => {
          resolveStale = resolve;
        }),
    );

    const errorListener = vi.fn();
    element.addEventListener('honua-scene-load-error', errorListener);

    const stalePromise = element.addLayer({
      id: 'overlay',
      title: 'Overlay',
      kind: '3d-tiles',
      url: 'https://data.example.test/overlay.json',
    });
    await new Promise((resolve) => setTimeout(resolve, 0));

    element.remove();
    document.body.append(element);
    await element.load();
    const widgetV2 = cesium.__mock.widgets[cesium.__mock.widgets.length - 1];

    resolveStale({
      url: 'https://data.example.test/overlay.json',
      show: true,
      style: undefined,
      destroy: vi.fn(() => {
        staleDestroyed = true;
      }),
      isDestroyed: () => staleDestroyed,
    });
    await stalePromise;

    expect(staleDestroyed).toBe(true);
    expect(
      widgetV2.scene.primitives.add.mock.calls.some(
        (call) => typeof (call[0] as { destroy?: unknown } | undefined)?.destroy === 'function',
      ),
    ).toBe(false);
    expect(element.getLayer('overlay')?.tileset).not.toBeNull();
    expect(errorListener).not.toHaveBeenCalled();

    webgl.mockRestore();
  });

  it('does not emit a load-error when a primary tileset load fails after disconnect', async () => {
    const webgl = mockWebGl();
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/v1.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    element.setAttribute('autoload', 'false');
    document.body.append(element);

    let rejectStale: (reason: unknown) => void = () => {};
    cesium.Cesium3DTileset.fromUrl.mockImplementationOnce(
      () =>
        new Promise((_, reject) => {
          rejectStale = reject;
        }),
    );

    const errorListener = vi.fn();
    element.addEventListener('honua-scene-load-error', errorListener);

    const stalePromise = element.load();
    await new Promise((resolve) => setTimeout(resolve, 0));

    element.remove();

    rejectStale(new Error('stale tileset failed'));
    await stalePromise;

    expect(errorListener).not.toHaveBeenCalled();

    webgl.mockRestore();
  });

  it('does not emit metadata-fetch-failed when a stale JSON parse rejects after a newer source wins', async () => {
    const fetchSpy = vi.spyOn(globalThis, 'fetch');
    let rejectStaleJson: (reason: unknown) => void = () => {};
    const stalePending = new Promise((_, reject) => {
      rejectStaleJson = reject;
    });
    fetchSpy.mockResolvedValueOnce({
      ok: true,
      status: 200,
      json: () => stalePending,
    } as unknown as Response);

    const element = document.createElement('honua-scene');
    element.setAttribute('autoload', 'false');
    document.body.append(element);
    const errorListener = vi.fn();
    const changeListener = vi.fn();
    element.addEventListener('honua-scene-metadata-error', errorListener);
    element.addEventListener('honua-scene-metadata-change', changeListener);

    element.setAttribute('metadata-url', 'https://example.test/stale.json');
    await new Promise((resolve) => setTimeout(resolve, 0));

    element.metadata = {
      schema: 'honua-scene-metadata/v1',
      id: 'fresh',
      name: 'Fresh',
    };

    rejectStaleJson(new SyntaxError('Unexpected token'));
    await new Promise((resolve) => setTimeout(resolve, 0));
    await new Promise((resolve) => setTimeout(resolve, 0));

    expect(errorListener).not.toHaveBeenCalled();
    expect(changeListener).toHaveBeenCalled();
    expect(element.metadata?.id).toBe('fresh');

    fetchSpy.mockRestore();
  });

  it('samplePoint returns null without a widget and a cartographic point when the widget is loaded', async () => {
    const element = document.createElement('honua-scene');
    element.setAttribute('autoload', 'false');
    document.body.append(element);
    expect(element.samplePoint(10, 20)).toBeNull();

    const webgl = mockWebGl();
    element.setAttribute('tileset-url', 'https://data.example.test/primary.json');
    element.setAttribute('cesium-base-url', 'data:text/css,');
    await element.load();
    const widget = cesium.__mock.widgets[0];
    widget.scene.pickPosition.mockReturnValueOnce({ x: 100, y: 200, z: 300 });

    const sampled = element.samplePoint(50, 60);
    expect(widget.scene.pickPosition).toHaveBeenCalledWith({ x: 50, y: 60 });
    expect(sampled).not.toBeNull();
    expect(sampled?.height).toBe(300);
    expect(typeof sampled?.latitude).toBe('number');
    expect(typeof sampled?.longitude).toBe('number');
    expect(Number.isFinite(sampled?.latitude)).toBe(true);
    expect(Number.isFinite(sampled?.longitude)).toBe(true);

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
