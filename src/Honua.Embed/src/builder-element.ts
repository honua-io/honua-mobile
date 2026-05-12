import {
  applyHonuaMapBuilderState,
  createHonuaMapBuilderState,
  type HonuaMapBuilderInput,
  type HonuaMapBuilderLayerOption,
  type HonuaMapBuilderState,
} from './builder';
import {
  createHonuaTemplate,
  defineHonuaCustomElement,
  HonuaHTMLElementBase,
} from './dom';
import { defineHonuaMapElement } from './map';
import type {
  HonuaMapEmbedBuilderSnippetOptions,
  HonuaMapEmbedBuilderTarget,
} from './snippets';

export interface HonuaMapBuilderChangeDetail {
  input: HonuaMapBuilderInput;
  state: HonuaMapBuilderState;
  target: HonuaMapEmbedBuilderTarget;
}

const ELEMENT_NAME = 'honua-map-builder';

const TARGETS: Array<{ value: HonuaMapEmbedBuilderTarget; label: string }> = [
  { value: 'web-component', label: 'Web component' },
  { value: 'cdn', label: 'CDN script' },
  { value: 'iframe', label: 'Iframe' },
  { value: 'react', label: 'React component' },
  { value: 'react-iframe', label: 'React iframe' },
  { value: 'vue', label: 'Vue component' },
  { value: 'vue-iframe', label: 'Vue iframe' },
  { value: 'angular', label: 'Angular component' },
  { value: 'angular-iframe', label: 'Angular iframe' },
];

const template = createHonuaTemplate(`
  <style>
    :host {
      --honua-builder-background: #f7f9fb;
      --honua-builder-foreground: #17212b;
      --honua-builder-muted: #5c6874;
      --honua-builder-accent: #1f7a8c;
      --honua-builder-surface: #ffffff;
      --honua-builder-border: rgba(23, 33, 43, 0.16);
      --honua-builder-danger: #b42318;
      --honua-builder-warning: #9a5b00;
      --honua-builder-font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif;
      display: block;
      color: var(--honua-builder-foreground);
      font-family: var(--honua-builder-font-family);
    }

    :host([theme="dark"]) {
      --honua-builder-background: #101820;
      --honua-builder-foreground: #eef5f7;
      --honua-builder-muted: #a9b8bf;
      --honua-builder-accent: #4fb4c8;
      --honua-builder-surface: #15232d;
      --honua-builder-border: rgba(238, 245, 247, 0.2);
      --honua-builder-danger: #ffb4a9;
      --honua-builder-warning: #ffd08a;
    }

    .builder {
      display: grid;
      grid-template-columns: minmax(280px, 360px) minmax(320px, 1fr);
      gap: 16px;
      padding: 16px;
      background: var(--honua-builder-background);
      border: 1px solid var(--honua-builder-border);
      border-radius: 8px;
      box-sizing: border-box;
    }

    form,
    .output {
      min-width: 0;
    }

    fieldset {
      margin: 0 0 14px;
      padding: 0;
      border: 0;
    }

    legend,
    .section-title {
      margin: 0 0 8px;
      color: var(--honua-builder-muted);
      font-size: 12px;
      font-weight: 700;
      letter-spacing: 0;
      text-transform: uppercase;
    }

    label {
      display: grid;
      gap: 5px;
      margin-block: 8px;
      color: var(--honua-builder-muted);
      font-size: 12px;
      font-weight: 600;
    }

    input,
    select,
    textarea {
      width: 100%;
      min-width: 0;
      box-sizing: border-box;
      color: var(--honua-builder-foreground);
      background: var(--honua-builder-surface);
      border: 1px solid var(--honua-builder-border);
      border-radius: 6px;
      font: inherit;
      font-size: 13px;
    }

    input,
    select {
      height: 34px;
      padding: 0 10px;
    }

    textarea {
      min-height: 190px;
      padding: 10px;
      resize: vertical;
      font-family: ui-monospace, SFMono-Regular, Consolas, "Liberation Mono", monospace;
      line-height: 1.45;
      white-space: pre;
    }

    input:focus-visible,
    select:focus-visible,
    textarea:focus-visible {
      outline: 3px solid color-mix(in srgb, var(--honua-builder-accent) 45%, transparent);
      outline-offset: 1px;
      border-color: var(--honua-builder-accent);
    }

    .grid {
      display: grid;
      grid-template-columns: repeat(2, minmax(0, 1fr));
      gap: 8px;
    }

    .toggles {
      display: grid;
      grid-template-columns: repeat(3, minmax(0, 1fr));
      gap: 8px;
    }

    .toggle,
    .layer-option {
      display: flex;
      min-width: 0;
      align-items: center;
      gap: 8px;
      margin: 0;
      padding: 8px;
      color: var(--honua-builder-foreground);
      background: color-mix(in srgb, var(--honua-builder-surface) 86%, transparent);
      border: 1px solid var(--honua-builder-border);
      border-radius: 6px;
      font-size: 13px;
      font-weight: 600;
    }

    .toggle input,
    .layer-option input {
      width: 16px;
      height: 16px;
      flex: 0 0 auto;
      margin: 0;
    }

    .layer-options {
      display: grid;
      gap: 6px;
    }

    .layer-option span {
      overflow: hidden;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .layer-option small {
      display: block;
      overflow: hidden;
      color: var(--honua-builder-muted);
      font-size: 11px;
      font-weight: 500;
      text-overflow: ellipsis;
      white-space: nowrap;
    }

    .preview {
      min-height: 280px;
      margin: 0 0 12px;
    }

    honua-map {
      height: 280px;
      min-height: 280px;
    }

    .status {
      min-height: 24px;
      margin: 0 0 8px;
      padding: 0;
      list-style: none;
    }

    .status li {
      margin: 0 0 6px;
      color: var(--honua-builder-muted);
      font-size: 12px;
    }

    .status li[data-severity="error"] {
      color: var(--honua-builder-danger);
    }

    .status li[data-severity="warning"] {
      color: var(--honua-builder-warning);
    }

    [hidden] {
      display: none !important;
    }

    @media (max-width: 820px) {
      .builder {
        grid-template-columns: 1fr;
      }

      .toggles {
        grid-template-columns: 1fr;
      }
    }
  </style>

  <div class="builder">
    <form part="form" autocomplete="off">
      <fieldset>
        <legend>Source</legend>
        <label>
          Service URL
          <input name="serviceUrl" type="url" placeholder="https://services.example.test/FeatureServer">
        </label>
        <label data-layer-text>
          Layer IDs
          <input name="layerIds" type="text" placeholder="assets,work-orders">
        </label>
        <div class="layer-options" data-layer-options hidden></div>
        <label>
          API key
          <input name="apiKey" type="password" autocomplete="off">
        </label>
      </fieldset>

      <fieldset>
        <legend>View</legend>
        <div class="grid">
          <label>
            Latitude
            <input name="latitude" type="number" step="any" inputmode="decimal">
          </label>
          <label>
            Longitude
            <input name="longitude" type="number" step="any" inputmode="decimal">
          </label>
        </div>
        <div class="grid">
          <label>
            Zoom
            <input name="zoom" type="number" min="0" max="24" step="any">
          </label>
          <label>
            Basemap
            <select name="basemap">
              <option value="">Default</option>
              <option value="streets">Streets</option>
              <option value="satellite">Satellite</option>
              <option value="dark">Dark</option>
            </select>
          </label>
        </div>
        <label>
          Bounds
          <input name="bounds" type="text" placeholder="-158.3,21.2,-157.6,21.6">
        </label>
      </fieldset>

      <fieldset>
        <legend>Behavior</legend>
        <div class="toggles">
          <label class="toggle"><input name="interactive" type="checkbox">Interactive</label>
          <label class="toggle"><input name="search" type="checkbox">Search</label>
          <label class="toggle"><input name="identify" type="checkbox">Identify</label>
        </div>
      </fieldset>

      <fieldset>
        <legend>Branding</legend>
        <label>
          Label
          <input name="label" type="text">
        </label>
        <label>
          Attribution
          <input name="attribution" type="text">
        </label>
        <div class="grid">
          <label>
            Theme
            <select name="theme">
              <option value="">Default</option>
              <option value="light">Light</option>
              <option value="dark">Dark</option>
            </select>
          </label>
          <label>
            Accent
            <input name="accent" type="text" placeholder="#1f7a8c">
          </label>
        </div>
      </fieldset>

      <fieldset>
        <legend>Output</legend>
        <label>
          Target
          <select name="target"></select>
        </label>
        <div class="grid">
          <label>
            Element name
            <input name="elementName" type="text" placeholder="honua-map">
          </label>
          <label class="toggle"><input name="includeCredentials" type="checkbox">Include key</label>
        </div>
        <label>
          CDN script URL
          <input name="scriptUrl" type="url" placeholder="https://cdn.honua.dev/embed.js">
        </label>
        <label>
          Iframe URL
          <input name="iframeUrl" type="url" placeholder="https://cdn.honua.dev/embed/map.html">
        </label>
        <label>
          Parent origin
          <input name="parentOrigin" type="url" placeholder="https://portal.example.com">
        </label>
      </fieldset>
    </form>

    <section class="output" part="output">
      <p class="section-title">Preview</p>
      <div class="preview" part="preview">
      </div>
      <p class="section-title">Status</p>
      <ul class="status" part="status"></ul>
      <p class="section-title">Snippet</p>
      <textarea name="snippet" part="snippet" readonly spellcheck="false"></textarea>
    </section>
  </div>
`);

export class HonuaMapBuilderElement extends HonuaHTMLElementBase {
  static get observedAttributes(): string[] {
    return [
      'api-key',
      'attribution',
      'available-layers',
      'basemap',
      'bbox',
      'center',
      'element-name',
      'identify',
      'iframe-url',
      'include-credentials',
      'interactive',
      'label',
      'layer-ids',
      'parent-origin',
      'script-url',
      'search',
      'service-url',
      'target',
      'theme',
      'zoom',
    ];
  }

  #availableLayers: HonuaMapBuilderLayerOption[] = [];
  #hasExplicitLayerSelection = false;
  #hasProgrammaticValue = false;
  #state: HonuaMapBuilderState | null = null;
  #updating = false;

  constructor() {
    super();
    defineHonuaMapElement();
    const shadow = this.attachShadow({ mode: 'open' });
    shadow.append(template.content.cloneNode(true));
    shadow.querySelector('[part="preview"]')!.append(document.createElement('honua-map'));
    this.#populateTargets();
    this.#wireForm();
  }

  get availableLayers(): HonuaMapBuilderLayerOption[] {
    return this.#availableLayers.map((layer) => ({ ...layer }));
  }

  set availableLayers(value: readonly HonuaMapBuilderLayerOption[] | null | undefined) {
    this.#availableLayers = normalizeLayerOptions(value);
    this.#syncFormFromAttributes();
    this.#renderState();
  }

  get state(): HonuaMapBuilderState {
    return this.#state ?? createHonuaMapBuilderState(
      this.#readInput(),
      this.#readSnippetOptions(),
    );
  }

  get value(): HonuaMapBuilderInput {
    return this.#readInput();
  }

  set value(value: HonuaMapBuilderInput | null | undefined) {
    const input = value ?? {};
    if (input.availableLayers !== undefined) {
      this.#availableLayers = normalizeLayerOptions(input.availableLayers);
    }

    this.#hasExplicitLayerSelection = input.selectedLayerIds !== undefined ||
      input.layerIds !== undefined;
    this.#hasProgrammaticValue = true;
    this.#writeInput(input);
    this.#renderState();
  }

  connectedCallback(): void {
    if (!this.#hasProgrammaticValue) {
      this.#syncFormFromAttributes();
    }

    this.#renderState();
  }

  attributeChangedCallback(name: string, oldValue: string | null, newValue: string | null): void {
    if (oldValue === newValue || this.#updating) {
      return;
    }

    if (name === 'available-layers') {
      this.#availableLayers = parseAvailableLayers(newValue);
    }

    if (this.isConnected) {
      this.#hasProgrammaticValue = false;
      this.#syncFormFromAttributes();
      this.#renderState();
    }
  }

  #populateTargets(): void {
    const select = this.#field<HTMLSelectElement>('target');
    select.replaceChildren(...TARGETS.map((target) => {
      const option = document.createElement('option');
      option.value = target.value;
      option.textContent = target.label;
      return option;
    }));
  }

  #wireForm(): void {
    this.#form.addEventListener('input', (event) => {
      this.#markLayerSelectionTouched(event);
      this.#renderState();
    });
    this.#form.addEventListener('change', (event) => {
      this.#markLayerSelectionTouched(event);
      this.#renderState();
    });
  }

  #syncFormFromAttributes(): void {
    this.#updating = true;
    try {
      const center = parseCoordinate(this.getAttribute('center'));
      const theme = this.getAttribute('theme') ?? '';
      this.#hasExplicitLayerSelection = this.hasAttribute('layer-ids');

      this.#field<HTMLInputElement>('serviceUrl').value = this.getAttribute('service-url') ?? '';
      this.#field<HTMLInputElement>('apiKey').value = this.getAttribute('api-key') ?? '';
      this.#field<HTMLInputElement>('latitude').value = center?.latitude === undefined ? '' : String(center.latitude);
      this.#field<HTMLInputElement>('longitude').value = center?.longitude === undefined ? '' : String(center.longitude);
      this.#field<HTMLInputElement>('zoom').value = this.getAttribute('zoom') ?? '';
      this.#field<HTMLInputElement>('bounds').value = this.getAttribute('bbox') ?? '';
      this.#field<HTMLSelectElement>('basemap').value = this.getAttribute('basemap') ?? '';
      this.#field<HTMLInputElement>('interactive').checked = parseBooleanAttribute(this, 'interactive') === true;
      this.#field<HTMLInputElement>('search').checked = parseBooleanAttribute(this, 'search') === true;
      this.#field<HTMLInputElement>('identify').checked = parseBooleanAttribute(this, 'identify') === true;
      this.#field<HTMLInputElement>('label').value = this.getAttribute('label') ?? '';
      this.#field<HTMLInputElement>('attribution').value = this.getAttribute('attribution') ?? '';
      this.#field<HTMLSelectElement>('theme').value = theme === 'dark' || theme === 'light' ? theme : '';
      this.#field<HTMLInputElement>('target').value = normalizeTarget(this.getAttribute('target'));
      this.#field<HTMLInputElement>('elementName').value = this.getAttribute('element-name') ?? '';
      this.#field<HTMLInputElement>('includeCredentials').checked = parseBooleanAttribute(this, 'include-credentials') === true;
      this.#field<HTMLInputElement>('scriptUrl').value = this.getAttribute('script-url') ?? '';
      this.#field<HTMLInputElement>('iframeUrl').value = this.getAttribute('iframe-url') ?? '';
      this.#field<HTMLInputElement>('parentOrigin').value = this.getAttribute('parent-origin') ?? '';

      this.#renderLayerInputs(parseStringList(this.getAttribute('layer-ids')));
    } finally {
      this.#updating = false;
    }
  }

  #writeInput(input: HonuaMapBuilderInput): void {
    this.#updating = true;
    try {
      this.#field<HTMLInputElement>('serviceUrl').value = input.serviceUrl ?? '';
      this.#field<HTMLInputElement>('apiKey').value = input.apiKey ?? '';
      this.#field<HTMLInputElement>('latitude').value = input.center?.latitude === undefined ? '' : String(input.center.latitude);
      this.#field<HTMLInputElement>('longitude').value = input.center?.longitude === undefined ? '' : String(input.center.longitude);
      this.#field<HTMLInputElement>('zoom').value = input.zoom === null || input.zoom === undefined ? '' : String(input.zoom);
      this.#field<HTMLInputElement>('bounds').value = input.bounds
        ? `${input.bounds.minLongitude},${input.bounds.minLatitude},${input.bounds.maxLongitude},${input.bounds.maxLatitude}`
        : '';
      this.#field<HTMLSelectElement>('basemap').value = input.basemap ?? '';
      this.#field<HTMLInputElement>('interactive').checked = input.interactive === true;
      this.#field<HTMLInputElement>('search').checked = input.search === true;
      this.#field<HTMLInputElement>('identify').checked = input.identify === true;
      this.#field<HTMLInputElement>('label').value = input.label ?? '';
      this.#field<HTMLInputElement>('attribution').value = input.attribution ?? '';
      this.#field<HTMLSelectElement>('theme').value = input.theme ?? '';
      this.#field<HTMLInputElement>('accent').value = input.style?.accent ?? '';
      this.#renderLayerInputs([...(input.selectedLayerIds ?? input.layerIds ?? [])]);
    } finally {
      this.#updating = false;
    }
  }

  #renderState(): void {
    if (this.#updating) {
      return;
    }

    const input = this.#readInput();
    const target = this.#target;
    const state = createHonuaMapBuilderState(input, this.#readSnippetOptions());
    this.#state = state;
    this.#renderLayerInputs(state.selectedLayerIds);
    this.#renderPreview(state);
    this.#renderIssues(state);
    this.#field<HTMLTextAreaElement>('snippet').value = state.snippet ?? '';
    this.dispatchEvent(new CustomEvent<HonuaMapBuilderChangeDetail>('honua-map-builder-change', {
      bubbles: true,
      composed: true,
      detail: {
        input,
        state,
        target,
      },
    }));
  }

  #markLayerSelectionTouched(event: Event): void {
    const target = event.target;
    if (!(target instanceof HTMLInputElement)) {
      return;
    }

    if (target.name === 'layerIds' || target.name === 'selectedLayer') {
      this.#hasExplicitLayerSelection = true;
    }
  }

  #renderLayerInputs(selectedLayerIds: readonly string[]): void {
    const selected = new Set(selectedLayerIds);
    const layerTextLabel = this.shadowRoot!.querySelector<HTMLElement>('[data-layer-text]')!;
    const layerOptions = this.shadowRoot!.querySelector<HTMLElement>('[data-layer-options]')!;

    if (this.#availableLayers.length === 0) {
      layerTextLabel.hidden = false;
      layerOptions.hidden = true;
      this.#field<HTMLInputElement>('layerIds').value = selectedLayerIds.join(',');
      return;
    }

    layerTextLabel.hidden = true;
    layerOptions.hidden = false;
    layerOptions.replaceChildren(...this.#availableLayers.map((layer) => {
      const label = document.createElement('label');
      label.className = 'layer-option';

      const checkbox = document.createElement('input');
      checkbox.type = 'checkbox';
      checkbox.name = 'selectedLayer';
      checkbox.value = layer.id;
      checkbox.checked = selected.has(layer.id) && layer.disabled !== true;
      checkbox.disabled = layer.disabled === true;

      const content = document.createElement('span');
      const name = document.createElement('span');
      name.textContent = layer.label ?? layer.id;
      content.append(name);

      if (layer.description) {
        const description = document.createElement('small');
        description.textContent = layer.description;
        content.append(description);
      }

      label.append(checkbox, content);
      return label;
    }));
  }

  #renderPreview(state: HonuaMapBuilderState): void {
    const preview = this.shadowRoot!.querySelector<HTMLElement>('honua-map')!;
    applyHonuaMapBuilderState(preview, state);
  }

  #renderIssues(state: HonuaMapBuilderState): void {
    const status = this.shadowRoot!.querySelector<HTMLUListElement>('.status')!;
    status.replaceChildren(...state.issues.map((issue) => {
      const item = document.createElement('li');
      item.dataset.severity = issue.severity;
      item.textContent = `${issue.severity}: ${issue.message}`;
      return item;
    }));
  }

  #readInput(): HonuaMapBuilderInput {
    const center = readCenter(
      this.#field<HTMLInputElement>('latitude').value,
      this.#field<HTMLInputElement>('longitude').value,
    );
    const bounds = readBounds(this.#field<HTMLInputElement>('bounds').value);
    const accent = normalizeString(this.#field<HTMLInputElement>('accent').value);
    const selectedLayerIds = this.#readSelectedLayerIds();
    const input: HonuaMapBuilderInput = {
      serviceUrl: normalizeString(this.#field<HTMLInputElement>('serviceUrl').value),
      availableLayers: this.#availableLayers,
      apiKey: normalizeString(this.#field<HTMLInputElement>('apiKey').value),
      center,
      zoom: readOptionalNumber(this.#field<HTMLInputElement>('zoom').value),
      bounds,
      basemap: normalizeString(this.#field<HTMLSelectElement>('basemap').value),
      interactive: this.#field<HTMLInputElement>('interactive').checked,
      search: this.#field<HTMLInputElement>('search').checked,
      identify: this.#field<HTMLInputElement>('identify').checked,
      attribution: normalizeString(this.#field<HTMLInputElement>('attribution').value),
      theme: readTheme(this.#field<HTMLSelectElement>('theme').value),
      label: normalizeString(this.#field<HTMLInputElement>('label').value),
      style: accent ? { accent } : null,
    };

    if (this.#hasExplicitLayerSelection) {
      input.selectedLayerIds = selectedLayerIds;
    }

    return input;
  }

  #readSelectedLayerIds(): string[] {
    if (this.#availableLayers.length === 0) {
      return parseStringList(this.#field<HTMLInputElement>('layerIds').value);
    }

    return [...this.shadowRoot!.querySelectorAll<HTMLInputElement>('input[name="selectedLayer"]:checked')]
      .map((input) => input.value);
  }

  #readSnippetOptions(): HonuaMapEmbedBuilderSnippetOptions {
    const elementName = normalizeString(this.#field<HTMLInputElement>('elementName').value) ?? undefined;
    const includeCredentials = this.#field<HTMLInputElement>('includeCredentials').checked;
    const scriptUrl = normalizeString(this.#field<HTMLInputElement>('scriptUrl').value) ?? undefined;
    const iframeUrl = normalizeString(this.#field<HTMLInputElement>('iframeUrl').value) ?? undefined;
    const parentOrigin = normalizeString(this.#field<HTMLInputElement>('parentOrigin').value);

    return {
      target: this.#target,
      webComponent: { elementName, includeCredentials },
      cdn: { elementName, includeCredentials, scriptUrl },
      iframe: { includeCredentials, iframeUrl, parentOrigin },
      react: { elementName, includeCredentials },
      reactIframe: { includeCredentials, iframeUrl, parentOrigin },
      vue: { elementName, includeCredentials },
      vueIframe: { includeCredentials, iframeUrl, parentOrigin },
      angular: { elementName, includeCredentials },
      angularIframe: { includeCredentials, iframeUrl, parentOrigin },
    };
  }

  get #target(): HonuaMapEmbedBuilderTarget {
    return normalizeTarget(this.#field<HTMLSelectElement>('target').value);
  }

  get #form(): HTMLFormElement {
    return this.shadowRoot!.querySelector('form')!;
  }

  #field<T extends HTMLInputElement | HTMLSelectElement | HTMLTextAreaElement>(name: string): T {
    return this.shadowRoot!.querySelector<T>(`[name="${name}"]`)!;
  }
}

export function defineHonuaMapBuilderElement(name = ELEMENT_NAME): typeof HonuaMapBuilderElement {
  defineHonuaMapElement();
  return defineHonuaCustomElement(name, HonuaMapBuilderElement) as typeof HonuaMapBuilderElement;
}

function parseAvailableLayers(value: string | null): HonuaMapBuilderLayerOption[] {
  if (!value) {
    return [];
  }

  try {
    const parsed = JSON.parse(value) as unknown;
    if (Array.isArray(parsed)) {
      return normalizeLayerOptions(parsed.map((entry) => {
        if (typeof entry === 'string') {
          return { id: entry };
        }

        if (typeof entry === 'object' && entry !== null) {
          const record = entry as Record<string, unknown>;
          return {
            id: String(record.id ?? ''),
            label: typeof record.label === 'string' ? record.label : null,
            description: typeof record.description === 'string' ? record.description : null,
            defaultSelected: record.defaultSelected === true,
            disabled: record.disabled === true,
          };
        }

        return { id: '' };
      }));
    }
  } catch {
    return normalizeLayerOptions(parseStringList(value).map((id) => ({ id })));
  }

  return [];
}

function normalizeLayerOptions(
  value: readonly HonuaMapBuilderLayerOption[] | null | undefined,
): HonuaMapBuilderLayerOption[] {
  return (value ?? [])
    .map((layer) => ({
      ...layer,
      id: layer.id.trim(),
    }))
    .filter((layer) => layer.id);
}

function parseBooleanAttribute(element: HTMLElement, name: string): boolean | null {
  const value = element.getAttribute(name);
  if (value === null) {
    return null;
  }

  return value !== 'false' && value !== '0' && value !== 'no';
}

function parseCoordinate(value: string | null): { latitude?: number; longitude?: number } | null {
  const parts = parseStringList(value);
  if (parts.length === 0) {
    return null;
  }

  return {
    latitude: Number(parts[0]),
    longitude: Number(parts[1]),
  };
}

function readCenter(latitude: string, longitude: string): HonuaMapBuilderInput['center'] {
  if (!latitude.trim() && !longitude.trim()) {
    return null;
  }

  return {
    latitude: Number(latitude),
    longitude: Number(longitude),
  };
}

function readBounds(value: string): HonuaMapBuilderInput['bounds'] {
  const parts = parseStringList(value);
  if (parts.length === 0) {
    return null;
  }

  return {
    minLongitude: Number(parts[0]),
    minLatitude: Number(parts[1]),
    maxLongitude: Number(parts[2]),
    maxLatitude: Number(parts[3]),
  };
}

function readOptionalNumber(value: string): number | null {
  const normalized = normalizeString(value);
  return normalized === null ? null : Number(normalized);
}

function readTheme(value: string): 'light' | 'dark' | null {
  return value === 'light' || value === 'dark' ? value : null;
}

function normalizeTarget(value: string | null | undefined): HonuaMapEmbedBuilderTarget {
  return TARGETS.some((target) => target.value === value)
    ? value as HonuaMapEmbedBuilderTarget
    : 'web-component';
}

function normalizeString(value: string | null | undefined): string | null {
  if (value === null || value === undefined) {
    return null;
  }

  const normalized = value.trim();
  return normalized || null;
}

function parseStringList(value: string | null | undefined): string[] {
  return (value ?? '')
    .split(',')
    .map((item) => item.trim())
    .filter(Boolean);
}
