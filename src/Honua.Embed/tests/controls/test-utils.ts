import { vi } from 'vitest';
import type { HonuaSceneElement } from '../../src/scene';
import type { HonuaSceneMetadata } from '../../src/scene-metadata';

vi.mock('cesium', () => {
  class MockCesiumWidget {
    readonly canvas = document.createElement('canvas');
    readonly camera = {
      changed: { addEventListener: vi.fn(() => vi.fn()) },
      heading: 0,
      pitch: 0,
      roll: 0,
      positionCartographic: { latitude: 0, longitude: 0, height: 0 },
      setView: vi.fn(),
    };
    readonly scene = {
      primitives: { add: vi.fn(), remove: vi.fn() },
      pick: vi.fn(),
      pickPosition: vi.fn(),
      requestRender: vi.fn(),
    };
    readonly destroy = vi.fn();

    constructor(container: HTMLElement) {
      const widget = document.createElement('div');
      widget.className = 'cesium-widget';
      container.append(widget);
    }

    isDestroyed(): boolean {
      return false;
    }

    async zoomTo(): Promise<void> {
      return Promise.resolve();
    }
  }

  return {
    buildModuleUrl: Object.assign(vi.fn(), { setBaseUrl: vi.fn() }),
    Cartesian3: { fromDegrees: vi.fn() },
    Cartographic: {
      fromCartesian: vi.fn(() => ({ latitude: 0, longitude: 0, height: 0 })),
    },
    Cesium3DTileset: {
      fromUrl: vi.fn(async (url: string) => ({ url, show: true, style: undefined })),
    },
    Cesium3DTileStyle: vi.fn(function MockTileStyle(this: { color?: string }, opts: { color?: string }) {
      this.color = opts?.color;
    }),
    CesiumTerrainProvider: { fromUrl: vi.fn(async (url: string) => ({ url })) },
    CesiumWidget: MockCesiumWidget,
    Ion: { defaultAccessToken: '' },
    Math: {
      toDegrees: vi.fn((value: number) => value * (180 / globalThis.Math.PI)),
      toRadians: vi.fn((value: number) => value * (globalThis.Math.PI / 180)),
    },
    ScreenSpaceEventHandler: class {
      readonly setInputAction = vi.fn();
      destroy(): void {}
      isDestroyed(): boolean {
        return false;
      }
    },
    ScreenSpaceEventType: { LEFT_CLICK: 0 },
  };
});

export const SAMPLE_METADATA: HonuaSceneMetadata = {
  schema: 'honua-scene-metadata/v1',
  id: 'sample',
  name: 'Sample Scene',
  description: 'Fixture metadata',
  layers: [
    {
      id: 'as-built',
      title: 'As-built',
      kind: '3d-tiles',
      url: 'https://example.test/as-built/tileset.json',
      visible: true,
      opacity: 1,
    },
    {
      id: 'design-overlay',
      title: 'Design overlay',
      kind: '3d-tiles',
      url: 'https://example.test/design/tileset.json',
      visible: false,
      opacity: 0.85,
    },
  ],
  bookmarks: [
    {
      id: 'site-overview',
      title: 'Site overview',
      view: {
        center: { latitude: 39.95, longitude: -75.16 },
        height: 2400,
        orientation: { heading: 0, pitch: -45, roll: 0 },
      },
    },
    {
      id: 'entrance',
      title: 'Entrance',
      view: {
        center: { latitude: 39.95, longitude: -75.16 },
        height: 80,
        orientation: { heading: 90, pitch: -10, roll: 0 },
      },
    },
  ],
  timeline: {
    phases: [
      {
        id: 'phase-1',
        title: 'Phase 1',
        startUtc: '2026-01-01T00:00:00Z',
        endUtc: '2026-03-31T00:00:00Z',
        visibleLayerIds: ['as-built'],
      },
      {
        id: 'phase-2',
        title: 'Phase 2',
        startUtc: '2026-04-01T00:00:00Z',
        endUtc: '2026-06-30T00:00:00Z',
        visibleLayerIds: ['as-built', 'design-overlay'],
      },
    ],
  },
  compare: {
    modes: [
      {
        id: 'design-vs-asbuilt',
        title: 'Design vs As-built',
        leftLayerIds: ['design-overlay'],
        rightLayerIds: ['as-built'],
      },
    ],
  },
  inspector: {
    fields: [
      { key: 'id', title: 'Asset ID' },
      { key: 'phase', title: 'Phase' },
      { key: 'elevation', title: 'Elevation', format: 'number', unit: 'm' },
    ],
  },
};

export interface SceneSetup {
  scene: HonuaSceneElement;
  cleanup(): void;
}

export async function setupScene(metadata: HonuaSceneMetadata = SAMPLE_METADATA): Promise<SceneSetup> {
  const { defineHonuaSceneElement } = await import('../../src/scene');
  const { defineHonuaSceneControls } = await import('../../src/controls');
  defineHonuaSceneElement();
  defineHonuaSceneControls();

  const scene = document.createElement('honua-scene');
  scene.setAttribute('autoload', 'false');
  document.body.append(scene);
  scene.metadata = metadata;

  return {
    scene,
    cleanup() {
      scene.remove();
    },
  };
}
