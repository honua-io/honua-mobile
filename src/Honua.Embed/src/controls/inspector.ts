import type {
  HonuaSceneElement,
  HonuaSceneIdentifyDetail,
} from '../scene';
import type { HonuaSceneInspectorField } from '../scene-metadata';
import { HonuaSceneLink } from './scene-link';
import {
  CONTROL_BASE_STYLES,
  controlTemplate,
  upgradeProperty,
} from './shared';

export interface HonuaSceneFeatureSelectDetail {
  featureId: string | null;
  attributes: Record<string, unknown>;
  controlId: string;
}

const ELEMENT_NAME = 'honua-scene-inspector';
const CONTROL_ID = 'inspector';

const template = controlTemplate(`
  ${CONTROL_BASE_STYLES}
  <style>
    dl {
      margin: 0;
      display: grid;
      grid-template-columns: max-content 1fr;
      gap: 4px 12px;
    }

    dt {
      color: var(--honua-control-muted);
      font-size: 12px;
      font-weight: 500;
    }

    dd {
      margin: 0;
      color: var(--honua-control-foreground);
      font-size: 13px;
      word-break: break-word;
    }

    .empty {
      color: var(--honua-control-muted);
      font-style: italic;
    }
  </style>
  <section class="control" part="control">
    <header part="header">
      <h2 class="title" part="title">Inspector</h2>
    </header>
    <div class="body" part="body">
      <p class="empty" data-empty>Click a feature to inspect attributes.</p>
      <dl class="fields" part="fields" hidden></dl>
    </div>
  </section>
`);

export class HonuaSceneInspectorElement extends HTMLElement {
  static get observedAttributes(): string[] {
    return ['for', 'heading'];
  }

  readonly #root: ShadowRoot;
  readonly #link: HonuaSceneLink;
  readonly #handleIdentify = (event: Event) => this.#onIdentify(event as CustomEvent<HonuaSceneIdentifyDetail>);
  #attributes: Record<string, unknown> | null = null;
  #featureId: string | null = null;

  constructor() {
    super();
    this.#root = this.attachShadow({ mode: 'open' });
    this.#root.append(template.content.cloneNode(true));
    this.#link = new HonuaSceneLink({
      host: this,
      controlId: CONTROL_ID,
      onSceneChange: () => this.#render(),
      forwardEvents: ['honua-scene-identify'],
      onForwardedEvent: this.#handleIdentify,
    });
  }

  connectedCallback(): void {
    upgradeProperty(this, 'sceneSelector');
    this.#applyHeading();
    this.#link.connect();
    this.#render();
  }

  disconnectedCallback(): void {
    this.#link.disconnect();
  }

  attributeChangedCallback(name: string): void {
    if (name === 'for') {
      this.#link.refresh('attribute');
      return;
    }

    if (name === 'heading') {
      this.#applyHeading();
    }
  }

  get scene(): HonuaSceneElement | null {
    return this.#link.scene;
  }

  get featureId(): string | null {
    return this.#featureId;
  }

  get featureAttributes(): Record<string, unknown> | null {
    return this.#attributes ? { ...this.#attributes } : null;
  }

  setFeature(featureId: string | null, attributes: Record<string, unknown>): void {
    this.#featureId = featureId;
    this.#attributes = { ...attributes };
    this.#render();
    this.dispatchEvent(new CustomEvent<HonuaSceneFeatureSelectDetail>('honua-scene-feature-select', {
      bubbles: true,
      composed: true,
      detail: { featureId, attributes: { ...attributes }, controlId: CONTROL_ID },
    }));
  }

  clearFeature(): void {
    this.#featureId = null;
    this.#attributes = null;
    this.#render();
  }

  refresh(): void {
    this.#render();
  }

  #applyHeading(): void {
    const heading = this.getAttribute('heading');
    const node = this.#root.querySelector<HTMLElement>('.title');
    if (node) {
      node.textContent = heading ?? 'Inspector';
    }
  }

  #onIdentify(event: CustomEvent<HonuaSceneIdentifyDetail>): void {
    const fields = this.#link.scene?.metadata?.inspector?.fields ?? [];
    const picked = event.detail.picked;
    if (!picked) {
      this.clearFeature();
      return;
    }

    const attributes = readPickedAttributes(picked, fields);
    const id = readPickedId(picked) ?? null;
    this.setFeature(id, attributes);
  }

  #render(): void {
    const fieldsEl = this.#root.querySelector<HTMLDListElement>('.fields')!;
    const empty = this.#root.querySelector<HTMLElement>('.empty')!;
    fieldsEl.replaceChildren();

    if (!this.#attributes) {
      fieldsEl.hidden = true;
      empty.hidden = false;
      return;
    }

    const fields = this.#link.scene?.metadata?.inspector?.fields ?? defaultFields(this.#attributes);
    fieldsEl.hidden = false;
    empty.hidden = true;

    for (const field of fields) {
      const value = this.#attributes[field.key];
      if (value === undefined || value === null) {
        continue;
      }
      const term = document.createElement('dt');
      term.textContent = field.title;
      const def = document.createElement('dd');
      def.textContent = formatValue(value, field);
      fieldsEl.append(term, def);
    }
  }
}

function readPickedAttributes(
  picked: unknown,
  fields: HonuaSceneInspectorField[],
): Record<string, unknown> {
  const result: Record<string, unknown> = {};

  if (typeof picked !== 'object' || picked === null) {
    return result;
  }

  const accessor = (picked as { getProperty?: (key: string) => unknown }).getProperty;
  const properties = (picked as { properties?: Record<string, unknown> }).properties;
  const direct = picked as Record<string, unknown>;

  if (fields.length === 0) {
    if (properties && typeof properties === 'object') {
      for (const [key, value] of Object.entries(properties)) {
        result[key] = value;
      }
    }
    return result;
  }

  for (const field of fields) {
    let value: unknown;
    if (typeof accessor === 'function') {
      try {
        value = accessor.call(picked, field.key);
      } catch {
        value = undefined;
      }
    } else if (properties && Object.prototype.hasOwnProperty.call(properties, field.key)) {
      value = properties[field.key];
    } else if (Object.prototype.hasOwnProperty.call(direct, field.key)) {
      value = direct[field.key];
    }

    if (value !== undefined) {
      result[field.key] = value;
    }
  }

  return result;
}

function readPickedId(picked: unknown): string | undefined {
  if (typeof picked !== 'object' || picked === null) {
    return undefined;
  }

  const candidate =
    (picked as { id?: unknown }).id ??
    (picked as { featureId?: unknown }).featureId;

  if (typeof candidate === 'string') {
    return candidate;
  }
  if (typeof candidate === 'number') {
    return String(candidate);
  }
  return undefined;
}

function formatValue(value: unknown, field: HonuaSceneInspectorField): string {
  if (field.format === 'date' && typeof value === 'string') {
    const parsed = Date.parse(value);
    if (Number.isFinite(parsed)) {
      return new Date(parsed).toISOString();
    }
  }

  if (field.format === 'number' && typeof value === 'number') {
    return field.unit ? `${value} ${field.unit}` : String(value);
  }

  if (typeof value === 'object') {
    return JSON.stringify(value);
  }

  return field.unit && typeof value === 'number'
    ? `${value} ${field.unit}`
    : String(value);
}

function defaultFields(attributes: Record<string, unknown>): HonuaSceneInspectorField[] {
  return Object.keys(attributes).map((key) => ({ key, title: key }));
}

export function defineHonuaSceneInspectorElement(name = ELEMENT_NAME): CustomElementConstructor {
  const existing = customElements.get(name);
  if (existing) {
    return existing;
  }
  customElements.define(name, HonuaSceneInspectorElement);
  return HonuaSceneInspectorElement;
}

declare global {
  interface HTMLElementTagNameMap {
    'honua-scene-inspector': HonuaSceneInspectorElement;
  }
}
