import type { HonuaSceneElement } from '../scene';
import type { HonuaSceneTimelinePhase } from '../scene-metadata';
import { HonuaSceneLink } from './scene-link';
import {
  assertHonuaDomAvailable,
  CONTROL_BASE_STYLES,
  controlTemplate,
  defineHonuaCustomElement,
  HonuaHTMLElementBase,
  resolveControlLayerIds,
  upgradeProperty,
} from './shared';

export interface HonuaSceneTimelineChangeDetail {
  phaseId: string;
  startUtc: string;
  endUtc: string;
  visibleLayerIds: string[];
  controlId: string;
}

const ELEMENT_NAME = 'honua-scene-timeline';
const CONTROL_ID = 'timeline';

const template = controlTemplate(`
  ${CONTROL_BASE_STYLES}
  <style>
    .phases {
      display: flex;
      flex-wrap: wrap;
      gap: 6px;
    }

    .phase {
      flex: 1 1 auto;
      min-width: 120px;
      text-align: left;
    }

    .phase-title {
      font-weight: 500;
    }

    .phase-range {
      color: var(--honua-control-muted);
      font-size: 12px;
    }

    .empty {
      color: var(--honua-control-muted);
      font-style: italic;
    }
  </style>
  <section class="control" part="control">
    <header part="header">
      <h2 class="title" part="title">Timeline</h2>
    </header>
    <div class="body" part="body">
      <p class="empty" data-empty>No timeline phases for this scene.</p>
      <div class="phases" part="phases" role="tablist" hidden></div>
    </div>
  </section>
`);

export class HonuaSceneTimelineElement extends HonuaHTMLElementBase {
  static get observedAttributes(): string[] {
    return ['for', 'heading', 'phase'];
  }

  readonly #root: ShadowRoot;
  readonly #link: HonuaSceneLink;
  #activePhase: string | null = null;

  constructor() {
    super();
    assertHonuaDomAvailable(ELEMENT_NAME);
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

    if (name === 'phase' && newValue && newValue !== this.#activePhase) {
      this.activate(newValue);
    }
  }

  get scene(): HonuaSceneElement | null {
    return this.#link.scene;
  }

  get activePhaseId(): string | null {
    return this.#activePhase;
  }

  activate(phaseId: string): boolean {
    const scene = this.#link.scene;
    const phases = scene?.metadata?.timeline?.phases ?? [];
    const phase = phases.find((entry) => entry.id === phaseId);
    if (!scene || !phase) {
      this.#link.emitError('phase-not-found', `Unknown timeline phase "${phaseId}".`);
      return false;
    }

    this.#activePhase = phaseId;
    this.#syncLayerVisibility(scene, phase);
    this.#renderActiveState();

    this.dispatchEvent(new CustomEvent<HonuaSceneTimelineChangeDetail>('honua-scene-timeline-change', {
      bubbles: true,
      composed: true,
      detail: {
        phaseId: phase.id,
        startUtc: phase.startUtc,
        endUtc: phase.endUtc,
        visibleLayerIds: [...phase.visibleLayerIds],
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
      node.textContent = heading ?? 'Timeline';
    }
  }

  #render(): void {
    const phasesEl = this.#root.querySelector<HTMLElement>('.phases')!;
    const empty = this.#root.querySelector<HTMLElement>('.empty')!;
    phasesEl.replaceChildren();

    const phases = this.#link.scene?.metadata?.timeline?.phases ?? [];
    if (phases.length === 0) {
      phasesEl.hidden = true;
      empty.hidden = false;
      return;
    }

    phasesEl.hidden = false;
    empty.hidden = true;

    for (const phase of phases) {
      phasesEl.append(this.#renderPhase(phase));
    }

    this.#renderActiveState();
  }

  #renderPhase(phase: HonuaSceneTimelinePhase): HTMLButtonElement {
    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'phase';
    button.dataset.phaseId = phase.id;
    button.role = 'tab';

    const title = document.createElement('div');
    title.className = 'phase-title';
    title.textContent = phase.title;
    button.append(title);

    const range = document.createElement('div');
    range.className = 'phase-range';
    range.textContent = `${formatRange(phase.startUtc)} – ${formatRange(phase.endUtc)}`;
    button.append(range);

    button.addEventListener('click', () => this.activate(phase.id));
    return button;
  }

  #renderActiveState(): void {
    const buttons = this.#root.querySelectorAll<HTMLButtonElement>('.phase');
    for (const button of buttons) {
      const isActive = button.dataset.phaseId === this.#activePhase;
      button.setAttribute('aria-pressed', String(isActive));
      button.setAttribute('aria-selected', String(isActive));
    }
  }

  #syncLayerVisibility(scene: HonuaSceneElement, phase: HonuaSceneTimelinePhase): void {
    const visible = new Set(phase.visibleLayerIds);
    for (const id of resolveControlLayerIds(scene)) {
      scene.setLayerVisibility(id, visible.has(id));
    }
  }
}

function formatRange(value: string): string {
  const time = Date.parse(value);
  if (!Number.isFinite(time)) {
    return value;
  }
  const date = new Date(time);
  return date.toISOString().slice(0, 10);
}

export function defineHonuaSceneTimelineElement(name = ELEMENT_NAME): CustomElementConstructor {
  return defineHonuaCustomElement(name, HonuaSceneTimelineElement);
}

declare global {
  interface HTMLElementTagNameMap {
    'honua-scene-timeline': HonuaSceneTimelineElement;
  }
}
