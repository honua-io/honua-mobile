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

export interface HonuaSceneIdentifyDetail {
  config: HonuaSceneConfig;
  x: number;
  y: number;
  picked: unknown;
}

type CesiumModule = typeof import('cesium');
type BuildModuleUrl = ((relativeUrl: string) => string) & {
  setBaseUrl?: (baseUrl: string) => void;
};

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

  connectedCallback(): void {
    this.#upgradeProperty('center');
    this.#upgradeProperty('packageAssetResolver');
    this.#render();
    this.#extensionHost.connect();

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
      this.#emitLoadError('terrain', 'Unable to initialize the terrain provider or scene widget.', error);
      return;
    }

    try {
      if (dataUrls.tilesetUrl) {
        this.#tileset = await cesium.Cesium3DTileset.fromUrl(dataUrls.tilesetUrl);

        if (version !== this.#loadVersion || !this.#widget) {
          return;
        }

        this.#widget.scene.primitives.add(this.#tileset);
        if (!config.center) {
          await this.#widget.zoomTo(this.#tileset);
        }
      }

      this.#bindCesiumEvents(cesium);
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
      this.#emitLoadError('tileset', 'Unable to load the 3D Tiles dataset.', error);
    }
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
      this.dispatchEvent(new CustomEvent<HonuaSceneIdentifyDetail>('honua-scene-identify', {
        bubbles: true,
        composed: true,
        detail: {
          config: this.config,
          x: event.position.x,
          y: event.position.y,
          picked,
        },
      }));
    }, cesium.ScreenSpaceEventType.LEFT_CLICK);
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

    if (this.#widget && !this.#widget.isDestroyed()) {
      this.#widget.destroy();
    }

    this.#widget = null;
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
