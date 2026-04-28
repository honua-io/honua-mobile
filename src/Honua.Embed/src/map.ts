export interface HonuaMapCoordinate {
  latitude: number;
  longitude: number;
}

export interface HonuaMapBounds {
  minLongitude: number;
  minLatitude: number;
  maxLongitude: number;
  maxLatitude: number;
}

export interface HonuaMapConfig {
  serviceUrl: string | null;
  layerIds: string[];
  apiKey: string | null;
  center: HonuaMapCoordinate | null;
  zoom: number;
  bounds: HonuaMapBounds | null;
  basemap: string;
  interactive: boolean;
  search: boolean;
  identify: boolean;
  attribution: string | null;
  theme: 'light' | 'dark';
}

export interface HonuaMapIdentifyDetail {
  x: number;
  y: number;
  config: HonuaMapConfig;
}

export interface HonuaMapSearchDetail {
  query: string;
  config: HonuaMapConfig;
}

const DEFAULT_ZOOM = 10;
const ELEMENT_NAME = 'honua-map';

const template = document.createElement('template');
template.innerHTML = `
  <style>
    :host {
      --honua-map-background: #f4f7f9;
      --honua-map-foreground: #13212c;
      --honua-map-muted: #566774;
      --honua-map-accent: #1f7a8c;
      --honua-map-surface: #ffffff;
      --honua-map-border: rgba(19, 33, 44, 0.16);
      --honua-map-control-size: 36px;
      --honua-map-font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      display: block;
      min-height: 280px;
      color: var(--honua-map-foreground);
      font-family: var(--honua-map-font-family);
    }

    :host([theme="dark"]) {
      --honua-map-background: #101820;
      --honua-map-foreground: #eef5f7;
      --honua-map-muted: #a9b8bf;
      --honua-map-accent: #4fb4c8;
      --honua-map-surface: #15232d;
      --honua-map-border: rgba(238, 245, 247, 0.2);
    }

    .map {
      position: relative;
      min-height: inherit;
      height: 100%;
      overflow: hidden;
      background: var(--honua-map-background);
      border: 1px solid var(--honua-map-border);
      border-radius: 8px;
      box-sizing: border-box;
    }

    .map:focus-visible {
      outline: 3px solid color-mix(in srgb, var(--honua-map-accent) 60%, transparent);
      outline-offset: 2px;
    }

    .surface {
      position: absolute;
      inset: 0;
      background-color: #dfe9ea;
      background-image:
        linear-gradient(rgba(31, 122, 140, 0.16) 1px, transparent 1px),
        linear-gradient(90deg, rgba(31, 122, 140, 0.16) 1px, transparent 1px),
        radial-gradient(circle at 42% 48%, rgba(82, 145, 108, 0.24), transparent 28%),
        radial-gradient(circle at 62% 36%, rgba(64, 129, 161, 0.18), transparent 24%);
      background-size: 48px 48px, 48px 48px, 100% 100%, 100% 100%;
    }

    .surface[data-basemap="dark"] {
      background-color: #14212b;
      background-image:
        linear-gradient(rgba(79, 180, 200, 0.2) 1px, transparent 1px),
        linear-gradient(90deg, rgba(79, 180, 200, 0.2) 1px, transparent 1px),
        radial-gradient(circle at 40% 44%, rgba(76, 143, 113, 0.24), transparent 30%),
        radial-gradient(circle at 66% 38%, rgba(81, 121, 176, 0.18), transparent 22%);
    }

    .surface[data-basemap="satellite"] {
      background-color: #48644d;
      background-image:
        radial-gradient(circle at 24% 26%, rgba(96, 133, 75, 0.82), transparent 20%),
        radial-gradient(circle at 72% 62%, rgba(52, 90, 105, 0.66), transparent 24%),
        linear-gradient(145deg, rgba(25, 39, 28, 0.45), rgba(88, 116, 72, 0.3));
    }

    .surface[data-basemap="streets"] {
      background-image:
        linear-gradient(35deg, transparent 47%, rgba(255, 255, 255, 0.78) 48%, rgba(255, 255, 255, 0.78) 52%, transparent 53%),
        linear-gradient(110deg, transparent 47%, rgba(255, 255, 255, 0.62) 48%, rgba(255, 255, 255, 0.62) 52%, transparent 53%),
        linear-gradient(rgba(31, 122, 140, 0.12) 1px, transparent 1px),
        linear-gradient(90deg, rgba(31, 122, 140, 0.12) 1px, transparent 1px);
      background-size: 100% 100%, 100% 100%, 48px 48px, 48px 48px;
    }

    .toolbar,
    .layers,
    .meta,
    .controls,
    .popup {
      position: absolute;
      z-index: 1;
    }

    .toolbar {
      top: 12px;
      left: 12px;
      right: 12px;
      display: none;
    }

    :host([search]:not([search="false"]):not([search="0"]):not([search="no"])) .toolbar {
      display: block;
    }

    .search {
      display: flex;
      max-width: 440px;
      background: var(--honua-map-surface);
      border: 1px solid var(--honua-map-border);
      border-radius: 8px;
      box-shadow: 0 10px 28px rgba(19, 33, 44, 0.14);
    }

    input {
      min-width: 0;
      flex: 1;
      height: 38px;
      padding: 0 12px;
      color: var(--honua-map-foreground);
      background: transparent;
      border: 0;
      font: inherit;
      outline: 0;
    }

    button {
      width: var(--honua-map-control-size);
      height: var(--honua-map-control-size);
      color: var(--honua-map-foreground);
      background: var(--honua-map-surface);
      border: 1px solid var(--honua-map-border);
      border-radius: 6px;
      font: inherit;
      font-size: 18px;
      line-height: 1;
      cursor: pointer;
    }

    button:hover {
      border-color: var(--honua-map-accent);
    }

    svg {
      width: 18px;
      height: 18px;
      stroke: currentColor;
      stroke-width: 2;
      stroke-linecap: round;
      stroke-linejoin: round;
      fill: none;
      pointer-events: none;
    }

    .search button {
      height: 38px;
      border-width: 0 0 0 1px;
      border-radius: 0 8px 8px 0;
    }

    .controls {
      right: 12px;
      top: 12px;
      display: none;
      gap: 6px;
      flex-direction: column;
    }

    :host([interactive]:not([interactive="false"]):not([interactive="0"]):not([interactive="no"])) .controls {
      display: flex;
    }

    :host([search]:not([search="false"]):not([search="0"]):not([search="no"])) .controls {
      top: 62px;
    }

    .layers {
      left: 12px;
      bottom: 12px;
      display: flex;
      max-width: calc(100% - 24px);
      flex-wrap: wrap;
      gap: 6px;
    }

    .layer {
      max-width: 180px;
      overflow: hidden;
      padding: 4px 8px;
      color: var(--honua-map-foreground);
      text-overflow: ellipsis;
      white-space: nowrap;
      background: color-mix(in srgb, var(--honua-map-surface) 88%, transparent);
      border: 1px solid var(--honua-map-border);
      border-radius: 999px;
      font-size: 12px;
    }

    .meta {
      right: 12px;
      bottom: 12px;
      max-width: min(420px, calc(100% - 24px));
      color: var(--honua-map-muted);
      font-size: 12px;
      text-align: right;
    }

    .marker {
      position: absolute;
      left: 50%;
      top: 50%;
      width: 18px;
      height: 18px;
      transform: translate(-50%, -50%) rotate(45deg);
      background: var(--honua-map-accent);
      border: 2px solid var(--honua-map-surface);
      border-radius: 50% 50% 50% 0;
      box-shadow: 0 3px 10px rgba(19, 33, 44, 0.3);
    }

    .popup {
      display: none;
      min-width: 160px;
      padding: 8px 10px;
      color: var(--honua-map-foreground);
      background: var(--honua-map-surface);
      border: 1px solid var(--honua-map-border);
      border-radius: 8px;
      box-shadow: 0 14px 30px rgba(19, 33, 44, 0.16);
      font-size: 13px;
      pointer-events: none;
    }

    .popup[data-open="true"] {
      display: block;
    }

    .sr-only {
      position: absolute;
      width: 1px;
      height: 1px;
      overflow: hidden;
      clip: rect(0, 0, 0, 0);
      white-space: nowrap;
    }
  </style>
  <section class="map" role="application" aria-label="Embedded map">
    <div class="surface"></div>
    <div class="marker" aria-hidden="true"></div>
    <form class="toolbar" part="toolbar">
      <div class="search">
        <label class="sr-only" for="honua-map-search">Search</label>
        <input id="honua-map-search" name="search" type="search" autocomplete="off" placeholder="Search">
        <button type="submit" aria-label="Search">
          <svg aria-hidden="true" focusable="false" viewBox="0 0 24 24">
            <circle cx="11" cy="11" r="7"></circle>
            <path d="m20 20-3.5-3.5"></path>
          </svg>
        </button>
      </div>
    </form>
    <div class="controls" part="controls">
      <button type="button" data-action="zoom-in" aria-label="Zoom in">+</button>
      <button type="button" data-action="zoom-out" aria-label="Zoom out">&minus;</button>
    </div>
    <div class="layers" part="layers"></div>
    <output class="popup" part="popup"></output>
    <div class="meta" part="attribution"></div>
  </section>
`;

export class HonuaMapElement extends HTMLElement {
  static get observedAttributes(): string[] {
    return [
      'service-url',
      'layer-ids',
      'api-key',
      'center',
      'zoom',
      'bbox',
      'basemap',
      'interactive',
      'search',
      'identify',
      'attribution',
      'theme',
    ];
  }

  readonly #root: ShadowRoot;
  #readyDispatched = false;

  constructor() {
    super();
    this.#root = this.attachShadow({ mode: 'open' });
    this.#root.append(template.content.cloneNode(true));
  }

  get config(): HonuaMapConfig {
    return readConfig(this);
  }

  connectedCallback(): void {
    this.#upgradeProperty('center');
    this.#upgradeProperty('zoom');
    this.#render();
    this.#bindEvents();

    if (!this.#readyDispatched) {
      this.#readyDispatched = true;
      this.dispatchEvent(new CustomEvent('honua-map-ready', {
        bubbles: true,
        composed: true,
        detail: this.config,
      }));
    }
  }

  attributeChangedCallback(): void {
    this.#render();
    this.dispatchEvent(new CustomEvent('honua-map-config-change', {
      bubbles: true,
      composed: true,
      detail: this.config,
    }));
  }

  setView(center: HonuaMapCoordinate, zoom = this.config.zoom): void {
    this.setAttribute('center', `${center.latitude},${center.longitude}`);
    this.setAttribute('zoom', String(clampZoom(zoom)));
  }

  refresh(): void {
    this.#render();
  }

  identifyAt(x: number, y: number): void {
    if (!this.config.identify) {
      return;
    }

    const popup = this.#query<HTMLOutputElement>('.popup');
    popup.style.left = `${Math.max(8, x)}px`;
    popup.style.top = `${Math.max(8, y)}px`;
    popup.dataset.open = 'true';
    popup.value = `x ${Math.round(x)}, y ${Math.round(y)}`;

    this.dispatchEvent(new CustomEvent<HonuaMapIdentifyDetail>('honua-map-identify', {
      bubbles: true,
      composed: true,
      detail: { x, y, config: this.config },
    }));
  }

  #bindEvents(): void {
    this.#query<HTMLFormElement>('.toolbar').onsubmit = (event) => {
      event.preventDefault();
      if (!this.config.search) {
        return;
      }

      const input = this.#query<HTMLInputElement>('input[type="search"]');
      const query = input.value.trim();
      if (query.length === 0) {
        return;
      }

      this.dispatchEvent(new CustomEvent<HonuaMapSearchDetail>('honua-map-search', {
        bubbles: true,
        composed: true,
        detail: { query, config: this.config },
      }));
    };

    this.#query<HTMLElement>('.surface').onclick = (event) => {
      const rect = this.#query<HTMLElement>('.map').getBoundingClientRect();
      this.identifyAt(event.clientX - rect.left, event.clientY - rect.top);
    };

    this.#query<HTMLElement>('.controls').onclick = (event) => {
      if (!this.config.interactive) {
        return;
      }

      const target = event.target;
      if (!(target instanceof HTMLElement)) {
        return;
      }

      if (target.dataset.action === 'zoom-in') {
        this.setAttribute('zoom', String(clampZoom(this.config.zoom + 1)));
      }

      if (target.dataset.action === 'zoom-out') {
        this.setAttribute('zoom', String(clampZoom(this.config.zoom - 1)));
      }
    };

    this.#query<HTMLElement>('.map').onkeydown = (event) => {
      if (!this.config.interactive) {
        return;
      }

      if (event.key === '+' || event.key === '=') {
        event.preventDefault();
        this.setAttribute('zoom', String(clampZoom(this.config.zoom + 1)));
      }

      if (event.key === '-' || event.key === '_') {
        event.preventDefault();
        this.setAttribute('zoom', String(clampZoom(this.config.zoom - 1)));
      }
    };
  }

  #render(): void {
    const config = this.config;
    const map = this.#query<HTMLElement>('.map');
    const surface = this.#query<HTMLElement>('.surface');
    const layers = this.#query<HTMLElement>('.layers');
    const meta = this.#query<HTMLElement>('.meta');

    map.tabIndex = config.interactive ? 0 : -1;
    surface.dataset.basemap = config.basemap;
    layers.replaceChildren(...config.layerIds.map((layerId) => {
      const chip = document.createElement('span');
      chip.className = 'layer';
      chip.setAttribute('part', 'layer');
      chip.textContent = layerId;
      return chip;
    }));
    meta.textContent = config.attribution ?? '';
  }

  #query<T extends Element>(selector: string): T {
    const element = this.#root.querySelector<T>(selector);
    if (!element) {
      throw new Error(`Missing Honua embed element: ${selector}`);
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

export function defineHonuaMapElement(name = ELEMENT_NAME): CustomElementConstructor {
  const existing = customElements.get(name);
  if (existing) {
    return existing;
  }

  customElements.define(name, HonuaMapElement);
  return HonuaMapElement;
}

function readConfig(element: HTMLElement): HonuaMapConfig {
  return {
    serviceUrl: emptyToNull(element.getAttribute('service-url')),
    layerIds: splitList(element.getAttribute('layer-ids')),
    apiKey: emptyToNull(element.getAttribute('api-key')),
    center: parseCoordinate(element.getAttribute('center')),
    zoom: clampZoom(parseNumber(element.getAttribute('zoom')) ?? DEFAULT_ZOOM),
    bounds: parseBounds(element.getAttribute('bbox')),
    basemap: element.getAttribute('basemap')?.trim() || 'streets',
    interactive: parseBooleanAttribute(element, 'interactive'),
    search: parseBooleanAttribute(element, 'search'),
    identify: parseBooleanAttribute(element, 'identify'),
    attribution: emptyToNull(element.getAttribute('attribution')),
    theme: element.getAttribute('theme') === 'dark' ? 'dark' : 'light',
  };
}

function emptyToNull(value: string | null): string | null {
  const trimmed = value?.trim();
  return trimmed ? trimmed : null;
}

function splitList(value: string | null): string[] {
  return value
    ?.split(',')
    .map((item) => item.trim())
    .filter((item) => item.length > 0) ?? [];
}

function parseCoordinate(value: string | null): HonuaMapCoordinate | null {
  const [latitude, longitude] = splitList(value).map(Number);
  if (!Number.isFinite(latitude) || !Number.isFinite(longitude)) {
    return null;
  }

  return { latitude, longitude };
}

function parseBounds(value: string | null): HonuaMapBounds | null {
  const [minLongitude, minLatitude, maxLongitude, maxLatitude] = splitList(value).map(Number);
  if (![minLongitude, minLatitude, maxLongitude, maxLatitude].every(Number.isFinite)) {
    return null;
  }

  return { minLongitude, minLatitude, maxLongitude, maxLatitude };
}

function parseNumber(value: string | null): number | null {
  if (value === null) {
    return null;
  }

  const parsed = Number(value);
  return Number.isFinite(parsed) ? parsed : null;
}

function parseBooleanAttribute(element: HTMLElement, name: string): boolean {
  if (!element.hasAttribute(name)) {
    return false;
  }

  const value = element.getAttribute(name);
  return value === '' || value === null || !['false', '0', 'no'].includes(value.toLowerCase());
}

function clampZoom(value: number): number {
  return Math.max(0, Math.min(24, Math.round(value)));
}

declare global {
  interface HTMLElementTagNameMap {
    'honua-map': HonuaMapElement;
  }
}
