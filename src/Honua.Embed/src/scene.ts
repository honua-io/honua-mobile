import type {
  Cesium3DTileset,
  CesiumWidget,
  Event as CesiumEvent,
  ScreenSpaceEventHandler,
} from 'cesium';
import {
  createHonuaEmbedExtensionHost,
  type HonuaEmbedExtensionHost,
} from './extensions';
import {
  HonuaScenePackageCacheError,
  type HonuaScenePackageAssetResolverInput,
  type HonuaScenePackageAssetKind,
  type HonuaScenePackageCacheErrorCode,
  resolveScenePackageAsset,
} from './scene-package-cache';
import {
  HonuaSceneMetadataError,
  parseHonuaSceneMetadata,
  type HonuaSceneLayerMetadata,
  type HonuaSceneMetadata,
  type HonuaSceneViewSpec,
} from './scene-metadata';

export interface HonuaSceneCoordinate {
  latitude: number;
  longitude: number;
}

export interface HonuaSceneOrientation {
  heading: number;
  pitch: number;
  roll: number;
}

export interface HonuaSceneConfig {
  tilesetUrl: string | null;
  terrainUrl: string | null;
  packageId: string | null;
  tilesetAssetPath: string | null;
  terrainAssetPath: string | null;
  packageExpiresAtUtc: string | null;
  ionToken: string | null;
  cesiumBaseUrl: string | null;
  center: HonuaSceneCoordinate | null;
  height: number;
  orientation: HonuaSceneOrientation;
  theme: 'light' | 'dark';
  autoload: boolean;
}

export interface HonuaSceneReadyDetail {
  config: HonuaSceneConfig;
  widget: CesiumWidget | null;
  tileset: Cesium3DTileset | null;
}

export interface HonuaSceneLoadErrorDetail {
  config: HonuaSceneConfig;
  source: 'webgl' | 'cesium' | 'terrain' | 'tileset' | 'package-cache';
  code?: HonuaScenePackageCacheErrorCode;
  message: string;
  error?: unknown;
}

export interface HonuaSceneCameraChangeDetail {
  config: HonuaSceneConfig;
  center: HonuaSceneCoordinate | null;
  height: number | null;
  orientation: HonuaSceneOrientation;
}

export interface HonuaSceneSampledPoint {
  latitude: number;
  longitude: number;
  height: number;
}

export interface HonuaSceneIdentifyDetail {
  config: HonuaSceneConfig;
  x: number;
  y: number;
  picked: unknown;
  position: HonuaSceneSampledPoint | null;
}

export interface HonuaSceneMetadataChangeDetail {
  metadata: HonuaSceneMetadata | null;
  source: 'attribute' | 'property' | 'fetch' | 'cleared';
}

export interface HonuaSceneMetadataErrorDetail {
  url: string | null;
  code: 'metadata-invalid' | 'metadata-fetch-failed';
  message: string;
  path?: string;
  error?: unknown;
}

export type HonuaSceneLayerChangeReason =
  | 'added'
  | 'removed'
  | 'visibility'
  | 'opacity'
  | 'metadata';

export interface HonuaSceneLayerChangeDetail {
  layerId: string;
  reason: HonuaSceneLayerChangeReason;
  layer: HonuaSceneLayerMetadata | null;
  visible: boolean;
  opacity: number;
}

export interface HonuaSceneLayerHandle {
  metadata: HonuaSceneLayerMetadata;
  visible: boolean;
  opacity: number;
  tileset: Cesium3DTileset | null;
}

type CesiumModule = typeof import('cesium');
type BuildModuleUrl = ((relativeUrl: string) => string) & {
  setBaseUrl?: (baseUrl: string) => void;
};
type LayerTilesetLoadResult = 'loaded' | 'failed' | 'stale' | 'skipped';

const DEFAULT_HEIGHT = 1200;
const DEFAULT_PITCH = -45;
const ELEMENT_NAME = 'honua-scene';

const sceneTemplate = document.createElement('template');
sceneTemplate.innerHTML = `
  <style>
    :host {
      --honua-scene-background: #101820;
      --honua-scene-foreground: #eef5f7;
      --honua-scene-muted: #a9b8bf;
      --honua-scene-accent: #4fb4c8;
      --honua-scene-border: rgba(238, 245, 247, 0.18);
      --honua-scene-font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      display: block;
      min-height: 360px;
      color: var(--honua-scene-foreground);
      font-family: var(--honua-scene-font-family);
    }

    :host([theme="light"]) {
      --honua-scene-background: #f4f7f9;
      --honua-scene-foreground: #13212c;
      --honua-scene-muted: #566774;
      --honua-scene-accent: #1f7a8c;
      --honua-scene-border: rgba(19, 33, 44, 0.16);
    }

    .scene {
      position: relative;
      min-height: inherit;
      height: 100%;
      overflow: hidden;
      background:
        linear-gradient(rgba(79, 180, 200, 0.15) 1px, transparent 1px),
        linear-gradient(90deg, rgba(79, 180, 200, 0.15) 1px, transparent 1px),
        var(--honua-scene-background);
      background-size: 56px 56px;
      border: 1px solid var(--honua-scene-border);
      border-radius: 8px;
      box-sizing: border-box;
    }

    .viewport {
      position: absolute;
      inset: 0;
    }

    .viewport :where(.cesium-widget, canvas) {
      width: 100%;
      height: 100%;
      display: block;
    }

    .viewport :where(.cesium-widget-credits) {
      font: 11px/1.4 var(--honua-scene-font-family);
    }

    .status {
      position: absolute;
      left: 12px;
      bottom: 12px;
      z-index: 1;
      max-width: min(420px, calc(100% - 24px));
      padding: 6px 8px;
      color: var(--honua-scene-muted);
      background: color-mix(in srgb, var(--honua-scene-background) 78%, transparent);
      border: 1px solid var(--honua-scene-border);
      border-radius: 6px;
      font-size: 12px;
      pointer-events: none;
    }

    .status[data-hidden="true"] {
      display: none;
    }

    .extension-controls {
      position: absolute;
      right: 12px;
      top: 12px;
      z-index: 1;
      display: none;
      gap: 6px;
      flex-direction: column;
    }

    .extension-controls[data-honua-extension-active="true"] {
      display: flex;
    }

    .extension-controls > button {
      width: 36px;
      height: 36px;
      color: var(--honua-scene-foreground);
      background: color-mix(in srgb, var(--honua-scene-background) 78%, transparent);
      border: 1px solid var(--honua-scene-border);
      border-radius: 6px;
      font: inherit;
      cursor: pointer;
    }

    .extension-controls > button:hover {
      border-color: var(--honua-scene-accent);
    }
  </style>
  <section class="scene" role="application" aria-label="Embedded 3D scene">
    <div class="viewport" part="viewport"></div>
    <div class="extension-controls" part="extension-controls" data-honua-extension-controls></div>
    <output class="status" part="status"></output>
  </section>
`;

export class HonuaSceneElement extends HTMLElement {
  static get observedAttributes(): string[] {
    return [
      'tileset-url',
      'terrain-url',
      'package-id',
      'tileset-asset',
      'terrain-asset',
      'package-expires-at',
      'ion-token',
      'cesium-base-url',
      'center',
      'height',
      'heading',
      'pitch',
      'roll',
      'theme',
      'autoload',
      'metadata-url',
    ];
  }

  readonly #root: ShadowRoot;
  #cesium: CesiumModule | null = null;
  #widget: CesiumWidget | null = null;
  #tileset: Cesium3DTileset | null = null;
  #handler: ScreenSpaceEventHandler | null = null;
  #removeCameraListener: CesiumEvent.RemoveCallback | null = null;
  #assetResolver: HonuaScenePackageAssetResolverInput | null = null;
  readonly #extensionHost: HonuaEmbedExtensionHost<'scene'>;
  #loadVersion = 0;
  #metadata: HonuaSceneMetadata | null = null;
  #metadataUrl: string | null = null;
  #metadataFetchVersion = 0;
  #pendingMetadataFetch: Promise<void> | null = null;
  #primaryOwner: 'implicit' | 'metadata' | null = null;
  readonly #layers = new Map<string, HonuaSceneLayerHandle>();
  readonly #layerLoadVersions = new Map<string, number>();

  constructor() {
    super();
    this.#root = this.attachShadow({ mode: 'open' });
    this.#root.append(sceneTemplate.content.cloneNode(true));
    this.#extensionHost = createHonuaEmbedExtensionHost({
      target: 'scene',
      element: this,
      getConfig: () => this.config,
    });
  }

  get config(): HonuaSceneConfig {
    return readSceneConfig(this);
  }

  get cesiumWidget(): CesiumWidget | null {
    return this.#widget;
  }

  get tileset(): Cesium3DTileset | null {
    return this.#tileset;
  }

  get packageAssetResolver(): HonuaScenePackageAssetResolverInput | null {
    return this.#assetResolver;
  }

  set packageAssetResolver(resolver: HonuaScenePackageAssetResolverInput | null) {
    this.setPackageAssetResolver(resolver);
  }

  get metadata(): HonuaSceneMetadata | null {
    return this.#metadata;
  }

  set metadata(value: HonuaSceneMetadata | null) {
    this.#metadataUrl = null;
    this.#metadataFetchVersion += 1;
    this.#pendingMetadataFetch = null;
    this.#applyMetadata(value, 'property');
  }

  get layers(): readonly HonuaSceneLayerHandle[] {
    return [...this.#layers.values()];
  }

  connectedCallback(): void {
    this.#upgradeProperty('center');
    this.#upgradeProperty('packageAssetResolver');
    this.#upgradeProperty('metadata');
    this.#render();
    this.#extensionHost.connect();

    const metadataUrl = this.getAttribute('metadata-url');
    if (metadataUrl && metadataUrl !== this.#metadataUrl) {
      this.#metadataUrl = metadataUrl;
      this.#pendingMetadataFetch = this.#fetchMetadata(metadataUrl);
    }

    const config = this.config;
    if (config.autoload && hasSceneData(config)) {
      void this.load();
    }
  }

  disconnectedCallback(): void {
    this.#loadVersion += 1;
    this.#extensionHost.disconnect();
    this.#destroyCesium();
  }

  attributeChangedCallback(name: string, oldValue: string | null, newValue: string | null): void {
    if (oldValue === newValue) {
      return;
    }

    this.#render();
    this.dispatchEvent(new CustomEvent('honua-scene-config-change', {
      bubbles: true,
      composed: true,
      detail: this.config,
    }));
    this.#extensionHost.configChanged();

    if (!this.isConnected) {
      return;
    }

    if (['center', 'height', 'heading', 'pitch', 'roll'].includes(name)) {
      this.#applyCamera();
      return;
    }

    if (name === 'metadata-url') {
      const next = emptyToNull(newValue);
      this.#metadataUrl = next;
      this.#metadataFetchVersion += 1;
      if (next) {
        this.#pendingMetadataFetch = this.#fetchMetadata(next);
      } else {
        this.#pendingMetadataFetch = null;
        this.#applyMetadata(null, 'cleared');
      }
      return;
    }

    if (
      this.config.autoload &&
      [
        'tileset-url',
        'terrain-url',
        'package-id',
        'tileset-asset',
        'terrain-asset',
        'package-expires-at',
        'ion-token',
        'cesium-base-url',
        'autoload',
      ].includes(name)
    ) {
      void this.load();
    }
  }

  setView(
    center: HonuaSceneCoordinate,
    height = this.config.height,
    orientation: Partial<HonuaSceneOrientation> = {},
  ): void {
    this.setAttribute('center', `${center.latitude},${center.longitude}`);
    this.setAttribute('height', String(height));

    if (orientation.heading !== undefined) {
      this.setAttribute('heading', String(orientation.heading));
    }

    if (orientation.pitch !== undefined) {
      this.setAttribute('pitch', String(orientation.pitch));
    }

    if (orientation.roll !== undefined) {
      this.setAttribute('roll', String(orientation.roll));
    }
  }

  applyView(view: HonuaSceneViewSpec): void {
    this.setView(view.center, view.height, view.orientation);
  }

  getLayer(layerId: string): HonuaSceneLayerHandle | null {
    return this.#layers.get(layerId) ?? null;
  }

  async addLayer(metadata: HonuaSceneLayerMetadata): Promise<HonuaSceneLayerHandle> {
    const existing = this.#layers.get(metadata.id);
    if (existing) {
      const sourceChanged =
        existing.metadata.kind !== metadata.kind ||
        existing.metadata.url !== metadata.url;

      existing.metadata = metadata;

      if (sourceChanged) {
        this.#detachLayerTileset(existing);
        this.#layerLoadVersions.set(
          metadata.id,
          (this.#layerLoadVersions.get(metadata.id) ?? 0) + 1,
        );
      }

      this.#applyLayerVisibility(existing, metadata.visible ?? existing.visible);
      this.#applyLayerOpacity(existing, metadata.opacity ?? existing.opacity);

      if (metadata.id === 'primary') {
        this.#primaryOwner = 'implicit';
      }

      if (sourceChanged && this.#canLoadLayerTileset(existing)) {
        await this.#loadLayerTileset(existing);
      }

      this.#emitLayerChange(existing, 'metadata');
      return existing;
    }

    const handle: HonuaSceneLayerHandle = {
      metadata,
      visible: metadata.visible ?? true,
      opacity: metadata.opacity ?? 1,
      tileset: null,
    };
    this.#layers.set(metadata.id, handle);
    this.#layerLoadVersions.set(metadata.id, (this.#layerLoadVersions.get(metadata.id) ?? 0) + 1);

    if (metadata.id === 'primary') {
      this.#primaryOwner = 'implicit';
    }

    if (this.#canLoadLayerTileset(handle)) {
      await this.#loadLayerTileset(handle);
    }

    this.#emitLayerChange(handle, 'added');
    return handle;
  }

  removeLayer(layerId: string): boolean {
    const handle = this.#layers.get(layerId);
    if (!handle) {
      return false;
    }

    this.#layers.delete(layerId);
    this.#layerLoadVersions.delete(layerId);
    if (layerId === 'primary') {
      this.#primaryOwner = null;
    }
    this.#detachLayerTileset(handle);

    this.dispatchEvent(new CustomEvent<HonuaSceneLayerChangeDetail>('honua-scene-layer-change', {
      bubbles: true,
      composed: true,
      detail: {
        layerId,
        reason: 'removed',
        layer: null,
        visible: false,
        opacity: handle.opacity,
      },
    }));
    return true;
  }

  setLayerVisibility(layerId: string, visible: boolean): boolean {
    const handle = this.#layers.get(layerId);
    if (!handle) {
      return false;
    }

    if (handle.visible === visible) {
      return true;
    }

    this.#applyLayerVisibility(handle, visible);
    this.#emitLayerChange(handle, 'visibility');
    return true;
  }

  setLayerOpacity(layerId: string, opacity: number): boolean {
    const handle = this.#layers.get(layerId);
    if (!handle) {
      return false;
    }

    const clamped = clampOpacity(opacity);
    if (handle.opacity === clamped) {
      return true;
    }

    this.#applyLayerOpacity(handle, clamped);
    this.#emitLayerChange(handle, 'opacity');
    return true;
  }

  async refresh(): Promise<void> {
    await this.load();
  }

  setPackageAssetResolver(resolver: HonuaScenePackageAssetResolverInput | null): void {
    this.#assetResolver = resolver;

    if (this.isConnected && this.config.autoload && hasSceneData(this.config)) {
      void this.load();
    }
  }

  async load(): Promise<void> {
    const version = ++this.#loadVersion;
    const config = this.config;
    const dataUrls = await this.#resolveSceneDataUrls(config);

    if (version !== this.#loadVersion) {
      return;
    }

    this.#dropStaleImplicitPrimary(dataUrls?.tilesetUrl ?? null);

    if (dataUrls === null) {
      this.#destroyCesium();
      return;
    }

    if (!dataUrls.tilesetUrl && !dataUrls.terrainUrl) {
      this.#destroyCesium();
      this.#setStatus('Set a 3D Tiles URL or package asset to load a scene.');
      return;
    }

    if (!canUseWebGl()) {
      this.#emitLoadError('webgl', '3D scenes require WebGL support in the host browser.');
      return;
    }

    this.#setStatus('Loading 3D scene...');

    let cesium: CesiumModule;
    try {
      cesium = await import('cesium');
      this.#cesium = cesium;
    } catch (error) {
      this.#emitLoadError('cesium', 'Unable to load CesiumJS.', error);
      return;
    }

    if (version !== this.#loadVersion) {
      return;
    }

    const pendingMetadataFetch = this.#pendingMetadataFetch;
    if (pendingMetadataFetch) {
      try {
        await pendingMetadataFetch;
      } catch {
        /* #fetchMetadata surfaces errors via honua-scene-metadata-error */
      }
      if (version !== this.#loadVersion) {
        return;
      }
    }

    this.#configureCesiumAssets(cesium, config);
    this.#destroyCesium();

    const viewport = this.#query<HTMLElement>('.viewport');
    viewport.replaceChildren();
    this.#appendCesiumStyles(config);

    try {
      const terrainProvider = dataUrls.terrainUrl
        ? await cesium.CesiumTerrainProvider.fromUrl(dataUrls.terrainUrl!)
        : undefined;

      if (version !== this.#loadVersion) {
        return;
      }

      this.#widget = new cesium.CesiumWidget(viewport, {
        baseLayer: false,
        terrainProvider,
        scene3DOnly: true,
        skyBox: false,
        skyAtmosphere: false,
        requestRenderMode: true,
        showRenderLoopErrors: false,
      });
    } catch (error) {
      if (version === this.#loadVersion) {
        this.#emitLoadError('terrain', 'Unable to initialize the terrain provider or scene widget.', error);
      }
      return;
    }

    let metadataDeclaresPrimary =
      this.#metadata?.layers?.some((layer) => layer.id === 'primary') ?? false;

    try {
      if (dataUrls.tilesetUrl && !metadataDeclaresPrimary) {
        const loaded = await cesium.Cesium3DTileset.fromUrl(dataUrls.tilesetUrl);

        if (version !== this.#loadVersion || !this.#widget) {
          destroyTilesetSafely(loaded);
          return;
        }

        metadataDeclaresPrimary =
          this.#metadata?.layers?.some((layer) => layer.id === 'primary') ?? false;

        if (metadataDeclaresPrimary) {
          destroyTilesetSafely(loaded);
        } else {
          this.#tileset = loaded;
          this.#widget.scene.primitives.add(loaded);
          this.#registerPrimaryLayer(dataUrls.tilesetUrl, loaded);
          if (!config.center) {
            await this.#widget.zoomTo(loaded);
          }

          if (version !== this.#loadVersion) {
            return;
          }
        }
      }

      this.#bindCesiumEvents(cesium);
      await this.#hydrateLayersFromMetadata();

      if (version !== this.#loadVersion || !this.#widget) {
        return;
      }

      metadataDeclaresPrimary =
        this.#metadata?.layers?.some((layer) => layer.id === 'primary') ?? false;

      if (metadataDeclaresPrimary) {
        const primaryHandle = this.#layers.get('primary');
        if (!primaryHandle?.tileset) {
          return;
        }

        this.#tileset = primaryHandle.tileset;
        if (!config.center) {
          await this.#widget.zoomTo(primaryHandle.tileset);
          if (version !== this.#loadVersion || !this.#widget) {
            return;
          }
        }
      }

      this.#applyCamera();
      this.#widget.scene.requestRender();
      this.#setStatus('', true);
      this.dispatchEvent(new CustomEvent<HonuaSceneReadyDetail>('honua-scene-ready', {
        bubbles: true,
        composed: true,
        detail: {
          config: this.config,
          widget: this.#widget,
          tileset: this.#tileset,
        },
      }));
    } catch (error) {
      if (version === this.#loadVersion) {
        this.#emitLoadError('tileset', 'Unable to load the 3D Tiles dataset.', error);
      }
    }
  }

  #registerPrimaryLayer(url: string, tileset: Cesium3DTileset): void {
    const handle: HonuaSceneLayerHandle = {
      metadata: {
        id: 'primary',
        title: 'Primary tileset',
        kind: '3d-tiles',
        url,
        visible: true,
        opacity: 1,
      },
      visible: true,
      opacity: 1,
      tileset,
    };
    this.#layers.set('primary', handle);
    this.#primaryOwner = 'implicit';
  }

  #dropStaleImplicitPrimary(targetTilesetUrl: string | null): void {
    const primary = this.#layers.get('primary');
    if (!primary) {
      return;
    }

    if (this.#metadata?.layers?.some((layer) => layer.id === 'primary')) {
      return;
    }

    if (primary.metadata.url === targetTilesetUrl) {
      return;
    }

    this.#detachLayerTileset(primary);
    this.#layers.delete('primary');
    this.#layerLoadVersions.delete('primary');
    this.#primaryOwner = null;
  }

  async #hydrateLayersFromMetadata(): Promise<void> {
    if (!this.#widget || !this.#cesium) {
      return;
    }

    for (const handle of [...this.#layers.values()]) {
      if (handle.tileset) {
        continue;
      }
      await this.#loadLayerTileset(handle);
    }
  }

  #canLoadLayerTileset(handle: HonuaSceneLayerHandle): boolean {
    return (
      handle.metadata.kind === '3d-tiles' &&
      this.#widget !== null &&
      this.#cesium !== null
    );
  }

  async #loadLayerTileset(handle: HonuaSceneLayerHandle): Promise<LayerTilesetLoadResult> {
    const cesium = this.#cesium;
    if (!this.#canLoadLayerTileset(handle) || !cesium) {
      return 'skipped';
    }

    const layerId = handle.metadata.id;
    const targetUrl = handle.metadata.url;
    const targetKind = handle.metadata.kind;
    const captured = this.#layerLoadVersions.get(layerId) ?? 0;
    const capturedSceneVersion = this.#loadVersion;
    const capturedWidget = this.#widget;

    let tileset: Cesium3DTileset;
    try {
      tileset = await cesium.Cesium3DTileset.fromUrl(targetUrl);
    } catch (error) {
      if (
        this.#isCurrentLayerTilesetLoad(
          layerId,
          handle,
          captured,
          capturedSceneVersion,
          capturedWidget,
        )
      ) {
        this.#emitLoadError('tileset', `Unable to load layer "${layerId}".`, error);
        return 'failed';
      }
      return 'stale';
    }

    if (
      !this.#isCurrentLayerTilesetLoad(
        layerId,
        handle,
        captured,
        capturedSceneVersion,
        capturedWidget,
      ) ||
      handle.metadata.kind !== targetKind ||
      handle.metadata.url !== targetUrl
    ) {
      destroyTilesetSafely(tileset);
      return 'stale';
    }

    const widget = this.#widget;
    if (!widget) {
      destroyTilesetSafely(tileset);
      return 'stale';
    }

    this.#detachLayerTileset(handle);
    handle.tileset = tileset;
    widget.scene.primitives.add(tileset);
    this.#applyLayerVisibility(handle, handle.visible);
    this.#applyLayerOpacity(handle, handle.opacity);
    if (handle.metadata.id === 'primary') {
      this.#tileset = tileset;
    }
    widget.scene.requestRender();
    return 'loaded';
  }

  #isCurrentLayerTilesetLoad(
    layerId: string,
    handle: HonuaSceneLayerHandle,
    captured: number,
    capturedSceneVersion: number,
    capturedWidget: CesiumWidget | null,
  ): boolean {
    return (
      capturedSceneVersion === this.#loadVersion &&
      this.#layers.get(layerId) === handle &&
      (this.#layerLoadVersions.get(layerId) ?? 0) === captured &&
      capturedWidget !== null &&
      this.#widget === capturedWidget &&
      !capturedWidget.isDestroyed()
    );
  }

  #detachLayerTileset(handle: HonuaSceneLayerHandle): void {
    if (!handle.tileset) {
      return;
    }
    if (this.#tileset === handle.tileset) {
      this.#tileset = null;
    }
    if (this.#widget) {
      try {
        this.#widget.scene.primitives.remove(handle.tileset);
      } catch {
        /* primitives may already be torn down */
      }
      this.#widget.scene.requestRender();
    }
    handle.tileset = null;
  }

  async #resolveSceneDataUrls(config: HonuaSceneConfig): Promise<{
    tilesetUrl: string | null;
    terrainUrl: string | null;
  } | null> {
    if (!config.packageId) {
      return {
        tilesetUrl: config.tilesetUrl,
        terrainUrl: config.terrainUrl,
      };
    }

    if (isExpired(config.packageExpiresAtUtc)) {
      this.#emitLoadError(
        'package-cache',
        'The offline scene package has expired and must be refreshed before rendering.',
        undefined,
        'expired-package',
      );
      return null;
    }

    try {
      return {
        tilesetUrl: config.tilesetAssetPath
          ? await this.#resolvePackageAssetUrl(config, config.tilesetAssetPath, 'tileset')
          : config.tilesetUrl,
        terrainUrl: config.terrainAssetPath
          ? await this.#resolvePackageAssetUrl(config, config.terrainAssetPath, 'terrain')
          : config.terrainUrl,
      };
    } catch (error) {
      this.#emitPackageCacheError(error);
      return null;
    }
  }

  async #resolvePackageAssetUrl(
    config: HonuaSceneConfig,
    path: string,
    kind: HonuaScenePackageAssetKind,
  ): Promise<string> {
    if (!this.#assetResolver) {
      throw new HonuaScenePackageCacheError(
        'unsupported-browser-storage',
        'No scene package asset resolver is configured for this browser or WebView host.',
      );
    }

    return await resolveScenePackageAsset(this.#assetResolver, {
      packageId: config.packageId!,
      path,
      kind,
      config,
    });
  }

  #bindCesiumEvents(cesium: CesiumModule): void {
    if (!this.#widget) {
      return;
    }

    this.#removeCameraListener = this.#widget.camera.changed.addEventListener(() => {
      this.dispatchEvent(new CustomEvent<HonuaSceneCameraChangeDetail>('honua-scene-camera-change', {
        bubbles: true,
        composed: true,
        detail: this.#cameraDetail(),
      }));
    });

    this.#handler = new cesium.ScreenSpaceEventHandler(this.#widget.canvas);
    this.#handler.setInputAction((event: ScreenSpaceEventHandler.PositionedEvent) => {
      if (!this.#widget) {
        return;
      }

      const picked = this.#widget.scene.pick(event.position);
      const position = this.samplePoint(event.position.x, event.position.y);
      this.dispatchEvent(new CustomEvent<HonuaSceneIdentifyDetail>('honua-scene-identify', {
        bubbles: true,
        composed: true,
        detail: {
          config: this.config,
          x: event.position.x,
          y: event.position.y,
          picked,
          position,
        },
      }));
    }, cesium.ScreenSpaceEventType.LEFT_CLICK);
  }

  samplePoint(x: number, y: number): HonuaSceneSampledPoint | null {
    if (!this.#widget || !this.#cesium) {
      return null;
    }

    const scene = this.#widget.scene as unknown as {
      pickPosition?: (position: { x: number; y: number }) => unknown;
    };
    if (typeof scene.pickPosition !== 'function') {
      return null;
    }

    let cartesian: unknown;
    try {
      cartesian = scene.pickPosition({ x, y });
    } catch {
      return null;
    }

    if (!cartesian) {
      return null;
    }

    const { Cartographic, Math: CesiumMath } = this.#cesium;
    let cartographic: { latitude: number; longitude: number; height: number } | null;
    try {
      cartographic = Cartographic.fromCartesian(
        cartesian as Parameters<typeof Cartographic.fromCartesian>[0],
      );
    } catch {
      return null;
    }

    if (!cartographic) {
      return null;
    }

    return {
      latitude: CesiumMath.toDegrees(cartographic.latitude),
      longitude: CesiumMath.toDegrees(cartographic.longitude),
      height: cartographic.height,
    };
  }

  #applyCamera(): void {
    if (!this.#widget || !this.#cesium) {
      return;
    }

    const config = this.config;
    if (!config.center) {
      return;
    }

    const { Cartesian3, Math: CesiumMath } = this.#cesium;
    this.#widget.camera.setView({
      destination: Cartesian3.fromDegrees(
        config.center.longitude,
        config.center.latitude,
        config.height,
      ),
      orientation: {
        heading: CesiumMath.toRadians(config.orientation.heading),
        pitch: CesiumMath.toRadians(config.orientation.pitch),
        roll: CesiumMath.toRadians(config.orientation.roll),
      },
    });
    this.#widget.scene.requestRender();
  }

  #cameraDetail(): HonuaSceneCameraChangeDetail {
    if (!this.#widget || !this.#cesium) {
      return {
        config: this.config,
        center: null,
        height: null,
        orientation: this.config.orientation,
      };
    }

    const { Math: CesiumMath } = this.#cesium;
    const position = this.#widget.camera.positionCartographic;
    return {
      config: this.config,
      center: {
        latitude: CesiumMath.toDegrees(position.latitude),
        longitude: CesiumMath.toDegrees(position.longitude),
      },
      height: position.height,
      orientation: {
        heading: CesiumMath.toDegrees(this.#widget.camera.heading),
        pitch: CesiumMath.toDegrees(this.#widget.camera.pitch),
        roll: CesiumMath.toDegrees(this.#widget.camera.roll),
      },
    };
  }

  #configureCesiumAssets(cesium: CesiumModule, config: HonuaSceneConfig): void {
    const baseUrl = config.cesiumBaseUrl ?? defaultCesiumBaseUrl();
    (cesium.buildModuleUrl as BuildModuleUrl).setBaseUrl?.(baseUrl);

    if (typeof window !== 'undefined') {
      (window as Window & { CESIUM_BASE_URL?: string }).CESIUM_BASE_URL = baseUrl;
    }

    cesium.Ion.defaultAccessToken = config.ionToken ?? '';
  }

  #appendCesiumStyles(config: HonuaSceneConfig): void {
    const href = `${config.cesiumBaseUrl ?? defaultCesiumBaseUrl()}Widgets/widgets.css`;
    const existing = this.#root.querySelector<HTMLLinkElement>('link[data-cesium-widgets]');
    if (existing) {
      existing.href = href;
      return;
    }

    const link = document.createElement('link');
    link.rel = 'stylesheet';
    link.href = href;
    link.dataset.cesiumWidgets = 'true';
    this.#root.append(link);
  }

  #render(): void {
    if (this.#widget) {
      this.#setStatus('', true);
      return;
    }

    this.#setStatus(hasSceneData(this.config)
      ? '3D scene ready to load.'
      : 'Set a 3D Tiles URL or package asset to load a scene.');
  }

  #destroyCesium(): void {
    this.#removeCameraListener?.();
    this.#removeCameraListener = null;

    if (this.#handler && !this.#handler.isDestroyed()) {
      this.#handler.destroy();
    }

    this.#handler = null;
    this.#tileset = null;

    for (const handle of this.#layers.values()) {
      handle.tileset = null;
    }

    if (this.#widget && !this.#widget.isDestroyed()) {
      this.#widget.destroy();
    }

    this.#widget = null;
  }

  async #fetchMetadata(url: string): Promise<void> {
    const version = ++this.#metadataFetchVersion;
    let response: Response;
    try {
      response = await fetch(url, { credentials: 'same-origin' });
    } catch (error) {
      if (version === this.#metadataFetchVersion) {
        this.#emitMetadataError({
          url,
          code: 'metadata-fetch-failed',
          message: 'Unable to fetch scene metadata.',
          error,
        });
      }
      return;
    }

    if (version !== this.#metadataFetchVersion) {
      return;
    }

    if (!response.ok) {
      this.#emitMetadataError({
        url,
        code: 'metadata-fetch-failed',
        message: `Scene metadata request failed (${response.status}).`,
      });
      return;
    }

    let payload: unknown;
    try {
      payload = await response.json();
    } catch (error) {
      if (version === this.#metadataFetchVersion) {
        this.#emitMetadataError({
          url,
          code: 'metadata-fetch-failed',
          message: 'Scene metadata response was not valid JSON.',
          error,
        });
      }
      return;
    }

    if (version !== this.#metadataFetchVersion) {
      return;
    }

    try {
      const metadata = parseHonuaSceneMetadata(payload);
      if (version !== this.#metadataFetchVersion) {
        return;
      }
      this.#applyMetadata(metadata, 'fetch');
    } catch (error) {
      if (version !== this.#metadataFetchVersion) {
        return;
      }
      if (error instanceof HonuaSceneMetadataError) {
        this.#emitMetadataError({
          url,
          code: error.code,
          message: error.message,
          path: error.path,
          error,
        });
        return;
      }
      this.#emitMetadataError({
        url,
        code: 'metadata-invalid',
        message: 'Scene metadata could not be parsed.',
        error,
      });
    }
  }

  #applyMetadata(
    metadata: HonuaSceneMetadata | null,
    source: HonuaSceneMetadataChangeDetail['source'],
  ): void {
    this.#metadata = metadata;
    this.#syncLayersFromMetadata(metadata);

    this.dispatchEvent(new CustomEvent<HonuaSceneMetadataChangeDetail>('honua-scene-metadata-change', {
      bubbles: true,
      composed: true,
      detail: {
        metadata,
        source,
      },
    }));
  }

  #syncLayersFromMetadata(metadata: HonuaSceneMetadata | null): void {
    const declaredIds = new Set<string>();
    if (metadata?.layers) {
      for (const layer of metadata.layers) {
        declaredIds.add(layer.id);
        void this.addLayer(layer);
        if (layer.id === 'primary') {
          this.#primaryOwner = 'metadata';
        }
      }
    }

    let metadataPrimaryDropped = false;
    if (!declaredIds.has('primary') && this.#primaryOwner === 'metadata') {
      const primary = this.#layers.get('primary');
      if (primary) {
        this.#detachLayerTileset(primary);
        this.#layers.delete('primary');
        this.#layerLoadVersions.delete('primary');
      }
      this.#primaryOwner = null;
      metadataPrimaryDropped = true;
    }

    for (const id of [...this.#layers.keys()]) {
      if (declaredIds.has(id)) {
        continue;
      }
      if (id === 'primary') {
        continue;
      }
      this.removeLayer(id);
    }

    if (metadataPrimaryDropped) {
      const config = this.config;
      if (this.isConnected && config.autoload && hasSceneData(config)) {
        void this.load();
      }
    }
  }

  #emitMetadataError(detail: HonuaSceneMetadataErrorDetail): void {
    this.dispatchEvent(new CustomEvent<HonuaSceneMetadataErrorDetail>('honua-scene-metadata-error', {
      bubbles: true,
      composed: true,
      detail,
    }));
  }

  #applyLayerVisibility(handle: HonuaSceneLayerHandle, visible: boolean): void {
    handle.visible = visible;
    if (handle.tileset) {
      handle.tileset.show = visible;
    }
    this.#widget?.scene.requestRender();
  }

  #applyLayerOpacity(handle: HonuaSceneLayerHandle, opacity: number): void {
    handle.opacity = opacity;
    if (handle.tileset && this.#cesium) {
      try {
        handle.tileset.style = new this.#cesium.Cesium3DTileStyle({
          color: `color('white', ${opacity})`,
        });
      } catch {
        /* style construction is best-effort and may fail in mocks */
      }
    }
    this.#widget?.scene.requestRender();
  }

  #emitLayerChange(
    handle: HonuaSceneLayerHandle,
    reason: HonuaSceneLayerChangeReason,
  ): void {
    this.dispatchEvent(new CustomEvent<HonuaSceneLayerChangeDetail>('honua-scene-layer-change', {
      bubbles: true,
      composed: true,
      detail: {
        layerId: handle.metadata.id,
        reason,
        layer: handle.metadata,
        visible: handle.visible,
        opacity: handle.opacity,
      },
    }));
  }

  #emitPackageCacheError(error: unknown): void {
    if (error instanceof HonuaScenePackageCacheError) {
      this.#emitLoadError('package-cache', error.message, error, error.code);
      return;
    }

    this.#emitLoadError(
      'package-cache',
      'Unable to resolve the offline scene package asset.',
      error,
      'cache-miss',
    );
  }

  #emitLoadError(
    source: HonuaSceneLoadErrorDetail['source'],
    message: string,
    error?: unknown,
    code?: HonuaScenePackageCacheErrorCode,
  ): void {
    this.#setStatus(message);
    this.dispatchEvent(new CustomEvent<HonuaSceneLoadErrorDetail>('honua-scene-load-error', {
      bubbles: true,
      composed: true,
      detail: {
        config: this.config,
        source,
        code,
        message,
        error,
      },
    }));
  }

  #setStatus(message: string, hidden = false): void {
    const status = this.#query<HTMLOutputElement>('.status');
    status.value = message;
    status.textContent = message;
    status.dataset.hidden = hidden ? 'true' : 'false';
  }

  #query<T extends Element>(selector: string): T {
    const element = this.#root.querySelector<T>(selector);
    if (!element) {
      throw new Error(`Missing Honua scene element: ${selector}`);
    }

    return element;
  }

  #upgradeProperty(propertyName: string): void {
    if (!Object.prototype.hasOwnProperty.call(this, propertyName)) {
      return;
    }

    const value = (this as unknown as Record<string, unknown>)[propertyName];
    delete (this as unknown as Record<string, unknown>)[propertyName];
    (this as unknown as Record<string, unknown>)[propertyName] = value;
  }
}

export function defineHonuaSceneElement(name = ELEMENT_NAME): CustomElementConstructor {
  const existing = customElements.get(name);
  if (existing) {
    return existing;
  }

  customElements.define(name, HonuaSceneElement);
  return HonuaSceneElement;
}

function readSceneConfig(element: HTMLElement): HonuaSceneConfig {
  return {
    tilesetUrl: emptyToNull(element.getAttribute('tileset-url')),
    terrainUrl: emptyToNull(element.getAttribute('terrain-url')),
    packageId: emptyToNull(element.getAttribute('package-id')),
    tilesetAssetPath: emptyToNull(element.getAttribute('tileset-asset')),
    terrainAssetPath: emptyToNull(element.getAttribute('terrain-asset')),
    packageExpiresAtUtc: emptyToNull(element.getAttribute('package-expires-at')),
    ionToken: emptyToNull(element.getAttribute('ion-token')),
    cesiumBaseUrl: normalizeBaseUrl(element.getAttribute('cesium-base-url')),
    center: parseCoordinate(element.getAttribute('center')),
    height: parseNumber(element.getAttribute('height')) ?? DEFAULT_HEIGHT,
    orientation: {
      heading: parseNumber(element.getAttribute('heading')) ?? 0,
      pitch: parseNumber(element.getAttribute('pitch')) ?? DEFAULT_PITCH,
      roll: parseNumber(element.getAttribute('roll')) ?? 0,
    },
    theme: element.getAttribute('theme') === 'light' ? 'light' : 'dark',
    autoload: parseBooleanAttribute(element, 'autoload', true),
  };
}

function hasSceneData(config: HonuaSceneConfig): boolean {
  return Boolean(
    config.tilesetUrl ||
    config.terrainUrl ||
    (config.packageId && (config.tilesetAssetPath || config.terrainAssetPath)),
  );
}

function isExpired(expiresAtUtc: string | null): boolean {
  if (!expiresAtUtc) {
    return false;
  }

  const expiresAt = Date.parse(expiresAtUtc);
  return Number.isFinite(expiresAt) && expiresAt <= Date.now();
}

function emptyToNull(value: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

function normalizeBaseUrl(value: string | null): string | null {
  const trimmed = emptyToNull(value);
  if (!trimmed) {
    return null;
  }

  return trimmed.endsWith('/') ? trimmed : `${trimmed}/`;
}

function splitList(value: string | null): string[] {
  return value
    ?.split(',')
    .map((item) => item.trim())
    .filter((item) => item.length > 0) ?? [];
}

function parseCoordinate(value: string | null): HonuaSceneCoordinate | null {
  const [latitude, longitude] = splitList(value).map(Number);
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) {
    return null;
  }

  return { latitude, longitude };
}

function parseNumber(value: string | null): number | null {
  if (value === null) {
    return null;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function parseBooleanAttribute(element: HTMLElement, name: string, defaultValue = false): boolean {
  if (!element.hasAttribute(name)) {
    return defaultValue;
  }

  const value = element.getAttribute(name);
  return value === '' || value === null || !['false', '0', 'no'].includes(value.toLowerCase());
}

function clampOpacity(value: number): number {
  if (!Number.isFinite(value)) {
    return 1;
  }

  return Math.max(0, Math.min(1, value));
}

function destroyTilesetSafely(tileset: Cesium3DTileset | null): void {
  if (!tileset) {
    return;
  }
  const candidate = tileset as { isDestroyed?: () => boolean; destroy?: () => void };
  try {
    if (candidate.isDestroyed?.() === false) {
      candidate.destroy?.();
    }
  } catch {
    /* tilesets may already be torn down by Cesium */
  }
}

function canUseWebGl(): boolean {
  try {
    const canvas = document.createElement('canvas');
    return Boolean(canvas.getContext('webgl2') ?? canvas.getContext('webgl'));
  } catch {
    return false;
  }
}

function defaultCesiumBaseUrl(): string {
  const cesiumAssetsPath = './cesium/';
  return new URL(cesiumAssetsPath, import.meta.url).toString();
}

declare global {
  interface HTMLElementTagNameMap {
    'honua-scene': HonuaSceneElement;
  }
}
