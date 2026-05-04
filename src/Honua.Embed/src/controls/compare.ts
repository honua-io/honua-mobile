import type { HonuaSceneElement } from '../scene';
import type { HonuaSceneCompareMode } from '../scene-metadata';
import { HonuaSceneLink } from './scene-link';
import {
  CONTROL_BASE_STYLES,
  controlTemplate,
  upgradeProperty,
} from './shared';

export interface HonuaSceneCompareSetDetail {
  modeId: string;
  side: 'left' | 'right' | 'both';
  leftLayerIds: string[];
  rightLayerIds: string[];
  controlId: string;
}

const ELEMENT_NAME = 'honua-scene-compare';
const CONTROL_ID = 'compare';

const template = controlTemplate(`
  ${CONTROL_BASE_STYLES}
  <style>
    .modes {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    .mode {
      display: flex;
      flex-direction: column;
      gap: 6px;
      padding: 8px 10px;
      border: 1px solid var(--honua-control-border);
      border-radius: 6px;
      background: var(--honua-control-surface);
    }

    .mode-title {
      font-weight: 500;
    }

    .mode-buttons {
      display: flex;
      gap: 6px;
    }

    .mode-buttons button {
      flex: 1;
    }

    .empty {
      color: var(--honua-control-muted);
      font-style: italic;
    }
  </style>
  <section class="control" part="control">
    <header part="header">
      <h2 class="title" part="title">Compare</h2>
    </header>
    <div class="body" part="body">
      <p class="empty" data-empty>No compare modes for this scene.</p>
      <div class="modes" part="modes" hidden></div>
    </div>
  </section>
`);

export class HonuaSceneCompareElement extends HTMLElement {
  static get observedAttributes(): string[] {
    return ['for', 'heading', 'mode', 'side'];
  }

  readonly #root: ShadowRoot;
  readonly #link: HonuaSceneLink;
  #activeMode: string | null = null;
  #activeSide: 'left' | 'right' | 'both' = 'both';

  constructor() {
    super();
    this.#root = this.attachShadow({ mode: 'open' });
    this.#root.append(template.content.cloneNode(true));
    this.#link = new HonuaSceneLink({
      host: this,
      controlId: CONTROL_ID,
      onSceneChange: () => this.#render(),
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

  attributeChangedCallback(name: string, _oldValue: string | null, newValue: string | null): void {
    if (name === 'for') {
      this.#link.refresh('attribute');
      return;
    }

    if (name === 'heading') {
      this.#applyHeading();
      return;
    }

    if (name === 'mode' && newValue && newValue !== this.#activeMode) {
      this.activate(newValue, this.#activeSide);
      return;
    }

    if (name === 'side' && newValue) {
      const side = (['left', 'right', 'both'] as const).includes(newValue as 'left' | 'right' | 'both')
        ? (newValue as 'left' | 'right' | 'both')
        : 'both';
      if (side !== this.#activeSide && this.#activeMode) {
        this.activate(this.#activeMode, side);
      }
    }
  }

  get scene(): HonuaSceneElement | null {
    return this.#link.scene;
  }

  get activeModeId(): string | null {
    return this.#activeMode;
  }

  get activeSide(): 'left' | 'right' | 'both' {
    return this.#activeSide;
  }

  activate(modeId: string, side: 'left' | 'right' | 'both' = 'both'): boolean {
    const scene = this.#link.scene;
    const modes = scene?.metadata?.compare?.modes ?? [];
    const mode = modes.find((entry) => entry.id === modeId);
    if (!scene || !mode) {
      this.#link.emitError('mode-not-found', `Unknown compare mode "${modeId}".`);
      return false;
    }

    this.#activeMode = modeId;
    this.#activeSide = side;
    this.#applyVisibility(scene, mode, side);
    this.#renderActiveState();

    this.dispatchEvent(new CustomEvent<HonuaSceneCompareSetDetail>('honua-scene-compare-set', {
      bubbles: true,
      composed: true,
      detail: {
        modeId,
        side,
        leftLayerIds: [...mode.leftLayerIds],
        rightLayerIds: [...mode.rightLayerIds],
        controlId: CONTROL_ID,
      },
    }));
    return true;
  }

  refresh(): void {
    this.#render();
  }

  #applyHeading(): void {
    const heading = this.getAttribute('heading');
    const node = this.#root.querySelector<HTMLElement>('.title');
    if (node) {
      node.textContent = heading ?? 'Compare';
    }
  }

  #render(): void {
    const modesEl = this.#root.querySelector<HTMLElement>('.modes')!;
    const empty = this.#root.querySelector<HTMLElement>('.empty')!;
    modesEl.replaceChildren();

    const modes = this.#link.scene?.metadata?.compare?.modes ?? [];
    if (modes.length === 0) {
      modesEl.hidden = true;
      empty.hidden = false;
      return;
    }

    modesEl.hidden = false;
    empty.hidden = true;

    for (const mode of modes) {
      modesEl.append(this.#renderMode(mode));
    }
    this.#renderActiveState();
  }

  #renderMode(mode: HonuaSceneCompareMode): HTMLDivElement {
    const wrapper = document.createElement('div');
    wrapper.className = 'mode';
    wrapper.dataset.modeId = mode.id;

    const title = document.createElement('div');
    title.className = 'mode-title';
    title.textContent = mode.title;
    wrapper.append(title);

    if (mode.description) {
      const description = document.createElement('div');
      description.className = 'mode-description';
      description.textContent = mode.description;
      wrapper.append(description);
    }

    const buttons = document.createElement('div');
    buttons.className = 'mode-buttons';

    for (const side of ['left', 'right', 'both'] as const) {
      const button = document.createElement('button');
      button.type = 'button';
      button.dataset.modeId = mode.id;
      button.dataset.side = side;
      button.textContent = side === 'both' ? 'Both' : side === 'left' ? 'Design' : 'As-built';
      button.addEventListener('click', () => this.activate(mode.id, side));
      buttons.append(button);
    }

    wrapper.append(buttons);
    return wrapper;
  }

  #renderActiveState(): void {
    const buttons = this.#root.querySelectorAll<HTMLButtonElement>('.mode-buttons button');
    for (const button of buttons) {
      const matches =
        button.dataset.modeId === this.#activeMode && button.dataset.side === this.#activeSide;
      button.setAttribute('aria-pressed', String(matches));
    }
  }

  #applyVisibility(
    scene: HonuaSceneElement,
    mode: HonuaSceneCompareMode,
    side: 'left' | 'right' | 'both',
  ): void {
    const visible = new Set<string>();
    if (side === 'left' || side === 'both') {
      mode.leftLayerIds.forEach((id) => visible.add(id));
    }
    if (side === 'right' || side === 'both') {
      mode.rightLayerIds.forEach((id) => visible.add(id));
    }

    const layers = scene.metadata?.layers ?? [];
    for (const layer of layers) {
      scene.setLayerVisibility(layer.id, visible.has(layer.id));
    }
  }
}

export function defineHonuaSceneCompareElement(name = ELEMENT_NAME): CustomElementConstructor {
  const existing = customElements.get(name);
  if (existing) {
    return existing;
  }
  customElements.define(name, HonuaSceneCompareElement);
  return HonuaSceneCompareElement;
}

declare global {
  interface HTMLElementTagNameMap {
    'honua-scene-compare': HonuaSceneCompareElement;
  }
}
