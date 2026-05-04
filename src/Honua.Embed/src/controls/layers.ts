import type {
  HonuaSceneElement,
  HonuaSceneLayerChangeDetail,
} from '../scene';
import type { HonuaSceneLayerMetadata } from '../scene-metadata';
import { HonuaSceneLink, type HonuaSceneControlErrorDetail } from './scene-link';
import {
  CONTROL_BASE_STYLES,
  controlTemplate,
  upgradeProperty,
} from './shared';

export interface HonuaSceneLayerToggleDetail {
  layerId: string;
  visible: boolean;
  controlId: string;
}

export interface HonuaSceneLayerOpacityDetail {
  layerId: string;
  opacity: number;
  controlId: string;
}

const ELEMENT_NAME = 'honua-scene-layers';
const CONTROL_ID = 'layers';

const template = controlTemplate(`
  ${CONTROL_BASE_STYLES}
  <style>
    .layer {
      display: flex;
      flex-direction: column;
      gap: 6px;
      padding: 8px 10px;
      border: 1px solid var(--honua-control-border);
      border-radius: 6px;
      background: var(--honua-control-surface);
    }

    .layer-row {
      display: flex;
      align-items: center;
      gap: 8px;
    }

    .layer-title {
      flex: 1;
      font-weight: 500;
      color: var(--honua-control-foreground);
    }

    .layer-description {
      color: var(--honua-control-muted);
      font-size: 12px;
    }

    input[type="range"] {
      width: 100%;
    }

    .empty {
      color: var(--honua-control-muted);
      font-style: italic;
    }
  </style>
  <section class="control" part="control">
    <header part="header">
      <h2 class="title" part="title">Layers</h2>
    </header>
    <div class="body" part="body" data-empty="true">
      <p class="empty" data-empty>No layers in this scene yet.</p>
      <ul class="list" part="list" hidden></ul>
    </div>
  </section>
`);

export class HonuaSceneLayersElement extends HTMLElement {
  static get observedAttributes(): string[] {
    return ['for', 'heading'];
  }

  readonly #root: ShadowRoot;
  readonly #link: HonuaSceneLink;
  readonly #handleLayerChange = (event: Event) => {
    void event;
    this.#render();
  };

  constructor() {
    super();
    this.#root = this.attachShadow({ mode: 'open' });
    this.#root.append(template.content.cloneNode(true));
    this.#link = new HonuaSceneLink({
      host: this,
      controlId: CONTROL_ID,
      onSceneChange: () => this.#render(),
      forwardEvents: ['honua-scene-layer-change'],
      onForwardedEvent: this.#handleLayerChange,
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

  refresh(): void {
    this.#render();
  }

  #applyHeading(): void {
    const heading = this.getAttribute('heading');
    const node = this.#root.querySelector<HTMLElement>('.title');
    if (node) {
      node.textContent = heading ?? 'Layers';
    }
  }

  #render(): void {
    const scene = this.#link.scene;
    const list = this.#root.querySelector<HTMLUListElement>('.list')!;
    const empty = this.#root.querySelector<HTMLElement>('.empty')!;
    const body = this.#root.querySelector<HTMLElement>('.body')!;
    list.replaceChildren();

    const layers = listLayers(scene);
    if (!scene || layers.length === 0) {
      list.hidden = true;
      empty.hidden = false;
      body.dataset.empty = 'true';
      return;
    }

    list.hidden = false;
    empty.hidden = true;
    body.dataset.empty = 'false';

    for (const layer of layers) {
      list.append(this.#renderLayer(layer));
    }
  }

  #renderLayer(layer: HonuaSceneLayerMetadata): HTMLLIElement {
    const item = document.createElement('li');
    item.className = 'layer';
    item.dataset.layerId = layer.id;

    const row = document.createElement('div');
    row.className = 'layer-row';

    const visibleInput = document.createElement('input');
    visibleInput.type = 'checkbox';
    visibleInput.id = `honua-scene-layer-${layer.id}`;
    visibleInput.checked = this.scene?.getLayer(layer.id)?.visible ?? layer.visible ?? true;
    visibleInput.addEventListener('change', () => {
      this.#emitToggle(layer.id, visibleInput.checked);
    });

    const labelEl = document.createElement('label');
    labelEl.htmlFor = visibleInput.id;
    labelEl.className = 'layer-title';
    labelEl.textContent = layer.title;

    row.append(visibleInput, labelEl);
    item.append(row);

    if (layer.description) {
      const description = document.createElement('p');
      description.className = 'layer-description';
      description.textContent = layer.description;
      item.append(description);
    }

    const opacityInput = document.createElement('input');
    opacityInput.type = 'range';
    opacityInput.min = '0';
    opacityInput.max = '1';
    opacityInput.step = '0.05';
    opacityInput.value = String(this.scene?.getLayer(layer.id)?.opacity ?? layer.opacity ?? 1);
    opacityInput.setAttribute('aria-label', `Opacity for ${layer.title}`);
    opacityInput.addEventListener('input', () => {
      this.#emitOpacity(layer.id, Number(opacityInput.value));
    });
    item.append(opacityInput);

    return item;
  }

  #emitToggle(layerId: string, visible: boolean): void {
    const scene = this.#link.scene;
    if (!scene) {
      this.#link.emitError('no-scene', 'Layers control is not bound to a <honua-scene>.');
      return;
    }

    if (!scene.setLayerVisibility(layerId, visible)) {
      this.#link.emitError('layer-not-found', `Unknown layer "${layerId}".`);
      return;
    }

    this.dispatchEvent(new CustomEvent<HonuaSceneLayerToggleDetail>('honua-scene-layer-toggle', {
      bubbles: true,
      composed: true,
      detail: { layerId, visible, controlId: CONTROL_ID },
    }));
  }

  #emitOpacity(layerId: string, opacity: number): void {
    const scene = this.#link.scene;
    if (!scene) {
      this.#link.emitError('no-scene', 'Layers control is not bound to a <honua-scene>.');
      return;
    }

    if (!scene.setLayerOpacity(layerId, opacity)) {
      this.#link.emitError('layer-not-found', `Unknown layer "${layerId}".`);
      return;
    }

    this.dispatchEvent(new CustomEvent<HonuaSceneLayerOpacityDetail>('honua-scene-layer-opacity', {
      bubbles: true,
      composed: true,
      detail: { layerId, opacity, controlId: CONTROL_ID },
    }));
  }
}

function listLayers(scene: HonuaSceneElement | null): HonuaSceneLayerMetadata[] {
  if (!scene) {
    return [];
  }
  return scene.metadata?.layers ?? [];
}

export function defineHonuaSceneLayersElement(name = ELEMENT_NAME): CustomElementConstructor {
  const existing = customElements.get(name);
  if (existing) {
    return existing;
  }
  customElements.define(name, HonuaSceneLayersElement);
  return HonuaSceneLayersElement;
}

export type { HonuaSceneControlErrorDetail, HonuaSceneLayerChangeDetail };

declare global {
  interface HTMLElementTagNameMap {
    'honua-scene-layers': HonuaSceneLayersElement;
  }
}
