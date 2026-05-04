import type { HonuaSceneElement } from '../scene';
import { HonuaSceneLink } from './scene-link';
import {
  CONTROL_BASE_STYLES,
  controlTemplate,
  upgradeProperty,
} from './shared';

export type HonuaSceneMeasurementKind = 'point' | 'line' | 'polygon';

export interface HonuaSceneMeasurementPoint {
  latitude: number;
  longitude: number;
  height: number;
}

export interface HonuaSceneMeasurementAddDetail {
  measurementId: string;
  kind: HonuaSceneMeasurementKind;
  points: HonuaSceneMeasurementPoint[];
  distance?: number;
  area?: number;
  controlId: string;
}

export interface HonuaSceneMeasurementClearDetail {
  measurementId: string | 'all';
  controlId: string;
}

const ELEMENT_NAME = 'honua-scene-measure';
const CONTROL_ID = 'measure';

const template = controlTemplate(`
  ${CONTROL_BASE_STYLES}
  <style>
    .modes {
      display: flex;
      gap: 6px;
    }

    .modes button {
      flex: 1;
    }

    .summary {
      display: flex;
      flex-direction: column;
      gap: 4px;
      color: var(--honua-control-foreground);
      font-size: 13px;
    }

    .summary span {
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
      <h2 class="title" part="title">Measure</h2>
      <button type="button" class="clear" part="clear">Clear</button>
    </header>
    <div class="body" part="body">
      <div class="modes" part="modes">
        <button type="button" data-kind="point" aria-pressed="false">Point</button>
        <button type="button" data-kind="line" aria-pressed="false">Distance</button>
        <button type="button" data-kind="polygon" aria-pressed="false">Area</button>
      </div>
      <div class="summary" part="summary">
        <p class="empty" data-empty>Choose a tool, then click in the scene to measure.</p>
      </div>
    </div>
  </section>
`);

export class HonuaSceneMeasureElement extends HTMLElement {
  static get observedAttributes(): string[] {
    return ['for', 'heading'];
  }

  readonly #root: ShadowRoot;
  readonly #link: HonuaSceneLink;
  readonly #handleIdentify = (event: Event) => this.#onIdentify(event as CustomEvent);
  #activeKind: HonuaSceneMeasurementKind | null = null;
  #points: HonuaSceneMeasurementPoint[] = [];
  #counter = 0;

  constructor() {
    super();
    this.#root = this.attachShadow({ mode: 'open' });
    this.#root.append(template.content.cloneNode(true));
    this.#link = new HonuaSceneLink({
      host: this,
      controlId: CONTROL_ID,
      onSceneChange: () => this.#renderActiveState(),
      forwardEvents: ['honua-scene-identify'],
      onForwardedEvent: this.#handleIdentify,
    });
  }

  connectedCallback(): void {
    upgradeProperty(this, 'sceneSelector');
    this.#applyHeading();
    this.#wireControls();
    this.#link.connect();
    this.#renderActiveState();
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

  get activeKind(): HonuaSceneMeasurementKind | null {
    return this.#activeKind;
  }

  selectKind(kind: HonuaSceneMeasurementKind | null): void {
    this.#activeKind = kind;
    this.#points = [];
    this.#renderActiveState();
    this.#renderSummary();
  }

  finalize(): boolean {
    if (!this.#activeKind || this.#points.length === 0) {
      return false;
    }

    const detail: HonuaSceneMeasurementAddDetail = {
      measurementId: `m-${++this.#counter}`,
      kind: this.#activeKind,
      points: [...this.#points],
      controlId: CONTROL_ID,
    };

    if (this.#activeKind === 'line' && this.#points.length >= 2) {
      detail.distance = computeDistance(this.#points);
    }

    if (this.#activeKind === 'polygon' && this.#points.length >= 3) {
      detail.area = computeArea(this.#points);
    }

    this.dispatchEvent(new CustomEvent<HonuaSceneMeasurementAddDetail>('honua-scene-measurement-add', {
      bubbles: true,
      composed: true,
      detail,
    }));

    this.#points = [];
    this.#renderSummary();
    return true;
  }

  clear(): void {
    this.#points = [];
    this.#activeKind = null;
    this.#renderActiveState();
    this.#renderSummary();
    this.dispatchEvent(new CustomEvent<HonuaSceneMeasurementClearDetail>('honua-scene-measurement-clear', {
      bubbles: true,
      composed: true,
      detail: { measurementId: 'all', controlId: CONTROL_ID },
    }));
  }

  #onIdentify(event: CustomEvent): void {
    if (!this.#activeKind) {
      return;
    }

    const point = readMeasurementPoint(this.#link.scene, event);
    if (!point) {
      this.#link.emitError('pick-failed', 'Unable to sample a point at the click location.');
      return;
    }

    this.#points.push(point);
    this.#renderSummary();

    if (this.#activeKind === 'point') {
      this.finalize();
    }
  }

  #wireControls(): void {
    const buttons = this.#root.querySelectorAll<HTMLButtonElement>('.modes button');
    for (const button of buttons) {
      button.addEventListener('click', () => {
        const kind = button.dataset.kind as HonuaSceneMeasurementKind | undefined;
        if (!kind) {
          return;
        }
        if (this.#activeKind === kind && (kind === 'line' || kind === 'polygon')) {
          this.finalize();
          return;
        }
        this.selectKind(kind);
      });
    }

    const clearButton = this.#root.querySelector<HTMLButtonElement>('.clear')!;
    clearButton.addEventListener('click', () => this.clear());
  }

  #applyHeading(): void {
    const heading = this.getAttribute('heading');
    const node = this.#root.querySelector<HTMLElement>('.title');
    if (node) {
      node.textContent = heading ?? 'Measure';
    }
  }

  #renderActiveState(): void {
    const buttons = this.#root.querySelectorAll<HTMLButtonElement>('.modes button');
    for (const button of buttons) {
      const matches = button.dataset.kind === this.#activeKind;
      button.setAttribute('aria-pressed', String(matches));
    }
  }

  #renderSummary(): void {
    const summary = this.#root.querySelector<HTMLElement>('.summary')!;
    summary.replaceChildren();

    if (!this.#activeKind) {
      const empty = document.createElement('p');
      empty.className = 'empty';
      empty.textContent = 'Choose a tool, then click in the scene to measure.';
      summary.append(empty);
      return;
    }

    const heading = document.createElement('p');
    heading.textContent = `Measuring (${this.#activeKind}) – ${this.#points.length} point${this.#points.length === 1 ? '' : 's'}`;
    summary.append(heading);

    if (this.#activeKind === 'line' && this.#points.length >= 2) {
      const distance = computeDistance(this.#points);
      const span = document.createElement('span');
      span.textContent = `Running distance: ${distance.toFixed(2)} m`;
      summary.append(span);
    }

    if (this.#activeKind === 'polygon' && this.#points.length >= 3) {
      const area = computeArea(this.#points);
      const span = document.createElement('span');
      span.textContent = `Approximate area: ${area.toFixed(2)} m²`;
      summary.append(span);
    }
  }
}

function readMeasurementPoint(
  scene: HonuaSceneElement | null,
  event: CustomEvent,
): HonuaSceneMeasurementPoint | null {
  const detail = event.detail as
    | {
        picked?: unknown;
        x?: number;
        y?: number;
        position?: { latitude?: number; longitude?: number; height?: number } | null;
      }
    | undefined;
  if (!detail) {
    return null;
  }

  const fromDetail = readPointFromCandidate(detail.position);
  if (fromDetail) {
    return fromDetail;
  }

  const fromPicked =
    detail.picked && typeof detail.picked === 'object'
      ? readPointFromCandidate(
          (detail.picked as { position?: unknown }).position,
        )
      : null;
  if (fromPicked) {
    return fromPicked;
  }

  if (scene && typeof detail.x === 'number' && typeof detail.y === 'number') {
    const sampled = scene.samplePoint(detail.x, detail.y);
    if (sampled) {
      return sampled;
    }
  }

  return null;
}

function readPointFromCandidate(value: unknown): HonuaSceneMeasurementPoint | null {
  if (!value || typeof value !== 'object') {
    return null;
  }

  const candidate = value as { latitude?: unknown; longitude?: unknown; height?: unknown };
  if (typeof candidate.latitude !== 'number' || typeof candidate.longitude !== 'number') {
    return null;
  }

  return {
    latitude: candidate.latitude,
    longitude: candidate.longitude,
    height: typeof candidate.height === 'number' ? candidate.height : 0,
  };
}

function computeDistance(points: HonuaSceneMeasurementPoint[]): number {
  let total = 0;
  for (let i = 1; i < points.length; i += 1) {
    total += haversineMeters(points[i - 1], points[i]);
  }
  return total;
}

function computeArea(points: HonuaSceneMeasurementPoint[]): number {
  if (points.length < 3) {
    return 0;
  }

  const radius = 6371000;
  let area = 0;
  for (let i = 0; i < points.length; i += 1) {
    const a = points[i];
    const b = points[(i + 1) % points.length];
    area +=
      toRadians(b.longitude - a.longitude) *
      (2 + Math.sin(toRadians(a.latitude)) + Math.sin(toRadians(b.latitude)));
  }

  return Math.abs((area * radius * radius) / 2);
}

function haversineMeters(
  a: HonuaSceneMeasurementPoint,
  b: HonuaSceneMeasurementPoint,
): number {
  const radius = 6371000;
  const lat1 = toRadians(a.latitude);
  const lat2 = toRadians(b.latitude);
  const dLat = toRadians(b.latitude - a.latitude);
  const dLon = toRadians(b.longitude - a.longitude);

  const sinLat = Math.sin(dLat / 2);
  const sinLon = Math.sin(dLon / 2);
  const h = sinLat * sinLat + Math.cos(lat1) * Math.cos(lat2) * sinLon * sinLon;
  const surface = 2 * radius * Math.asin(Math.min(1, Math.sqrt(h)));
  const dHeight = b.height - a.height;
  return Math.sqrt(surface * surface + dHeight * dHeight);
}

function toRadians(value: number): number {
  return (value * Math.PI) / 180;
}

export function defineHonuaSceneMeasureElement(name = ELEMENT_NAME): CustomElementConstructor {
  const existing = customElements.get(name);
  if (existing) {
    return existing;
  }
  customElements.define(name, HonuaSceneMeasureElement);
  return HonuaSceneMeasureElement;
}

declare global {
  interface HTMLElementTagNameMap {
    'honua-scene-measure': HonuaSceneMeasureElement;
  }
}
