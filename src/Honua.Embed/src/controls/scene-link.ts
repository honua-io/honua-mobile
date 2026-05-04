import type { HonuaSceneElement } from '../scene';

export interface HonuaSceneControlErrorDetail {
  controlId: string;
  kind: string;
  message: string;
  error?: unknown;
}

export type HonuaSceneLinkChangeReason =
  | 'attribute'
  | 'connected'
  | 'disconnected'
  | 'metadata-change'
  | 'config-change'
  | 'ready';

export interface HonuaSceneLinkContext {
  readonly scene: HonuaSceneElement | null;
}

export interface HonuaSceneLinkOptions {
  host: HTMLElement;
  controlId: string;
  onSceneChange?(scene: HonuaSceneElement | null, reason: HonuaSceneLinkChangeReason): void;
  forwardEvents?: readonly string[];
  onForwardedEvent?(event: Event): void;
}

const SCENE_EVENT_TYPES_FOR_REWIRE = [
  'honua-scene-ready',
  'honua-scene-config-change',
  'honua-scene-metadata-change',
] as const;

export class HonuaSceneLink {
  readonly #host: HTMLElement;
  readonly #controlId: string;
  readonly #onSceneChange?: (
    scene: HonuaSceneElement | null,
    reason: HonuaSceneLinkChangeReason,
  ) => void;
  readonly #forwardEvents: readonly string[];
  readonly #onForwardedEvent?: (event: Event) => void;
  readonly #boundForwardListener: (event: Event) => void;
  readonly #boundRewireListener: (event: Event) => void;
  #scene: HonuaSceneElement | null = null;
  #connected = false;
  #observer: MutationObserver | null = null;

  constructor(options: HonuaSceneLinkOptions) {
    this.#host = options.host;
    this.#controlId = options.controlId;
    this.#onSceneChange = options.onSceneChange;
    this.#forwardEvents = options.forwardEvents ?? [];
    this.#onForwardedEvent = options.onForwardedEvent;
    this.#boundForwardListener = (event: Event) => this.#handleForward(event);
    this.#boundRewireListener = () => this.#handleSceneRewire();
  }

  get scene(): HonuaSceneElement | null {
    return this.#scene;
  }

  get controlId(): string {
    return this.#controlId;
  }

  connect(): void {
    if (this.#connected) {
      return;
    }
    this.#connected = true;
    this.#installObserver();
    this.#updateScene('connected');
  }

  disconnect(): void {
    if (!this.#connected) {
      return;
    }
    this.#connected = false;
    this.#observer?.disconnect();
    this.#observer = null;
    this.#detachScene();
    this.#scene = null;
    this.#onSceneChange?.(null, 'disconnected');
  }

  refresh(reason: HonuaSceneLinkChangeReason = 'attribute'): void {
    if (!this.#connected) {
      return;
    }
    this.#updateScene(reason);
  }

  emitError(kind: string, message: string, error?: unknown): void {
    this.#host.dispatchEvent(new CustomEvent<HonuaSceneControlErrorDetail>('honua-scene-control-error', {
      bubbles: true,
      composed: true,
      detail: {
        controlId: this.#controlId,
        kind,
        message,
        error,
      },
    }));
  }

  #installObserver(): void {
    if (typeof MutationObserver === 'undefined') {
      return;
    }

    this.#observer = new MutationObserver(() => this.#updateScene('attribute'));
    const root = this.#host.getRootNode();
    if (root instanceof Document || root instanceof ShadowRoot) {
      this.#observer.observe(root, {
        childList: true,
        subtree: true,
      });
    }
  }

  #updateScene(reason: HonuaSceneLinkChangeReason): void {
    const next = resolveSceneTarget(this.#host);
    if (next === this.#scene) {
      return;
    }

    this.#detachScene();
    this.#scene = next;
    this.#attachScene();
    this.#onSceneChange?.(next, reason);
  }

  #attachScene(): void {
    if (!this.#scene) {
      return;
    }

    for (const type of SCENE_EVENT_TYPES_FOR_REWIRE) {
      this.#scene.addEventListener(type, this.#boundRewireListener);
    }

    for (const type of this.#forwardEvents) {
      this.#scene.addEventListener(type, this.#boundForwardListener);
    }
  }

  #detachScene(): void {
    if (!this.#scene) {
      return;
    }

    for (const type of SCENE_EVENT_TYPES_FOR_REWIRE) {
      this.#scene.removeEventListener(type, this.#boundRewireListener);
    }

    for (const type of this.#forwardEvents) {
      this.#scene.removeEventListener(type, this.#boundForwardListener);
    }
  }

  #handleSceneRewire(): void {
    if (!this.#scene) {
      return;
    }

    this.#onSceneChange?.(this.#scene, 'metadata-change');
  }

  #handleForward(event: Event): void {
    this.#onForwardedEvent?.(event);
  }
}

function resolveSceneTarget(host: HTMLElement): HonuaSceneElement | null {
  const selector = host.getAttribute('for');
  const root = host.getRootNode();
  const search = (root instanceof Document || root instanceof ShadowRoot) ? root : document;

  if (selector) {
    const match = (search.querySelector?.(selector) ?? null) as HTMLElement | null;
    return isSceneElement(match) ? match : null;
  }

  let sibling = host.previousElementSibling;
  while (sibling) {
    if (isSceneElement(sibling)) {
      return sibling;
    }
    sibling = sibling.previousElementSibling;
  }

  const fallback = (search.querySelector?.('honua-scene') ?? null) as HTMLElement | null;
  return isSceneElement(fallback) ? fallback : null;
}

function isSceneElement(node: Element | null): node is HonuaSceneElement {
  if (!node) {
    return false;
  }
  return node.tagName.toLowerCase() === 'honua-scene';
}
