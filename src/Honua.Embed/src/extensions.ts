import type { HonuaMapConfig } from './map';
import type { HonuaSceneConfig } from './scene';

export type HonuaEmbedTarget = 'map' | 'scene';

export interface HonuaEmbedConfigByTarget {
  map: HonuaMapConfig;
  scene: HonuaSceneConfig;
}

export type HonuaEmbedExtensionCleanup = () => void;

export interface HonuaEmbedExtensionContext<TTarget extends HonuaEmbedTarget = HonuaEmbedTarget> {
  readonly target: TTarget;
  readonly element: HTMLElement;
  readonly shadowRoot: ShadowRoot;
  readonly config: HonuaEmbedConfigByTarget[TTarget];
  addControl(options: HonuaEmbedControlOptions<TTarget>): HonuaEmbedContribution;
  setCssVariable(name: string, value: string | null): void;
  dispatch(type: string, detail?: unknown, init?: Omit<CustomEventInit, 'detail'>): boolean;
}

export interface HonuaEmbedControlOptions<TTarget extends HonuaEmbedTarget = HonuaEmbedTarget> {
  id?: string;
  label: string;
  title?: string;
  text?: string;
  disabled?: boolean;
  part?: string;
  onClick?: (event: MouseEvent, context: HonuaEmbedExtensionContext<TTarget>) => void;
}

export interface HonuaEmbedContribution {
  readonly element: HTMLElement;
  remove(): void;
}

export interface HonuaEmbedExtension<TTarget extends HonuaEmbedTarget = HonuaEmbedTarget> {
  id: string;
  target?: TTarget | readonly TTarget[];
  priority?: number;
  activate(context: HonuaEmbedExtensionContext<TTarget>): HonuaEmbedExtensionCleanup | void;
  configChanged?(context: HonuaEmbedExtensionContext<TTarget>): void;
}

export interface HonuaEmbedExtensionRegistration {
  readonly id: string;
  unregister(): void;
}

export interface HonuaEmbedExtensionDescriptor {
  readonly id: string;
  readonly target: readonly HonuaEmbedTarget[];
  readonly priority: number;
}

export interface HonuaEmbedExtensionErrorDetail {
  extensionId: string;
  target: HonuaEmbedTarget;
  lifecycle: 'activate' | 'configChanged' | 'deactivate';
  error: unknown;
}

interface HonuaEmbedExtensionHostOptions<TTarget extends HonuaEmbedTarget> {
  target: TTarget;
  element: HTMLElement;
  getConfig: () => HonuaEmbedConfigByTarget[TTarget];
  controlsSelector?: string;
}

interface ActiveExtension {
  extension: HonuaEmbedExtension;
  cleanup?: HonuaEmbedExtensionCleanup;
  contributions: HonuaEmbedContribution[];
}

const DEFAULT_CONTROLS_SELECTOR = '[data-honua-extension-controls]';
const extensions = new Map<string, HonuaEmbedExtension>();
const hosts = new Set<HonuaEmbedExtensionHost<HonuaEmbedTarget>>();

export function registerHonuaEmbedExtension<TTarget extends HonuaEmbedTarget>(
  extension: HonuaEmbedExtension<TTarget>,
): HonuaEmbedExtensionRegistration {
  const id = extension.id.trim();
  if (!id) {
    throw new Error('Honua embed extensions require a non-empty id.');
  }

  if (extensions.has(id)) {
    throw new Error(`A Honua embed extension is already registered with id "${id}".`);
  }

  const normalized = { ...extension, id } as HonuaEmbedExtension;
  extensions.set(id, normalized);
  for (const host of hosts) {
    host.activate(normalized);
  }

  let registered = true;
  return {
    id,
    unregister() {
      if (!registered) {
        return;
      }

      registered = false;
      extensions.delete(id);
      for (const host of hosts) {
        host.deactivate(id);
      }
    },
  };
}

export function listHonuaEmbedExtensions(target?: HonuaEmbedTarget): HonuaEmbedExtensionDescriptor[] {
  return sortedExtensions()
    .filter((extension) => !target || extensionTargets(extension).includes(target))
    .map((extension) => ({
      id: extension.id,
      target: extensionTargets(extension),
      priority: extension.priority ?? 0,
    }));
}

export class HonuaEmbedExtensionHost<TTarget extends HonuaEmbedTarget> {
  readonly #target: TTarget;
  readonly #element: HTMLElement;
  readonly #getConfig: () => HonuaEmbedConfigByTarget[TTarget];
  readonly #controlsSelector: string;
  readonly #active = new Map<string, ActiveExtension>();
  #connected = false;

  constructor(options: HonuaEmbedExtensionHostOptions<TTarget>) {
    this.#target = options.target;
    this.#element = options.element;
    this.#getConfig = options.getConfig;
    this.#controlsSelector = options.controlsSelector ?? DEFAULT_CONTROLS_SELECTOR;
  }

  connect(): void {
    if (this.#connected) {
      return;
    }

    this.#connected = true;
    hosts.add(this as HonuaEmbedExtensionHost<HonuaEmbedTarget>);
    for (const extension of sortedExtensions()) {
      this.activate(extension);
    }
  }

  disconnect(): void {
    if (!this.#connected) {
      return;
    }

    for (const id of [...this.#active.keys()]) {
      this.deactivate(id);
    }

    hosts.delete(this as HonuaEmbedExtensionHost<HonuaEmbedTarget>);
    this.#connected = false;
  }

  configChanged(): void {
    if (!this.#connected) {
      return;
    }

    for (const active of this.#active.values()) {
      try {
        active.extension.configChanged?.(this.#context(active.extension.id) as HonuaEmbedExtensionContext);
      } catch (error) {
        this.#emitError(active.extension.id, 'configChanged', error);
      }
    }
  }

  activate(extension: HonuaEmbedExtension): void {
    if (!this.#connected || this.#active.has(extension.id) || !extensionTargets(extension).includes(this.#target)) {
      return;
    }

    const active: ActiveExtension = { extension, contributions: [] };
    this.#active.set(extension.id, active);

    try {
      const cleanup = extension.activate(this.#context(extension.id) as HonuaEmbedExtensionContext);
      if (cleanup) {
        active.cleanup = cleanup;
      }
    } catch (error) {
      this.deactivate(extension.id);
      this.#emitError(extension.id, 'activate', error);
    }
  }

  deactivate(id: string): void {
    const active = this.#active.get(id);
    if (!active) {
      return;
    }

    this.#active.delete(id);
    for (const contribution of active.contributions.splice(0)) {
      contribution.remove();
    }

    try {
      active.cleanup?.();
    } catch (error) {
      this.#emitError(id, 'deactivate', error);
    }
  }

  #context(extensionId: string): HonuaEmbedExtensionContext<TTarget> {
    const thisHost = this;

    return {
      get target() {
        return thisHost.#target;
      },
      get element() {
        return thisHost.#element;
      },
      get shadowRoot() {
        return thisHost.#root();
      },
      get config() {
        return thisHost.#getConfig();
      },
      addControl(options) {
        return thisHost.#addControl(extensionId, options);
      },
      setCssVariable(name, value) {
        thisHost.#setCssVariable(name, value);
      },
      dispatch(type, detail, init) {
        return thisHost.#element.dispatchEvent(new CustomEvent(type, {
          bubbles: true,
          composed: true,
          ...init,
          detail,
        }));
      },
    };
  }

  #addControl(
    extensionId: string,
    options: HonuaEmbedControlOptions<TTarget>,
  ): HonuaEmbedContribution {
    if (!this.#active.has(extensionId)) {
      throw new Error(`Honua embed extension "${extensionId}" is not active.`);
    }

    const label = options.label.trim();
    if (!label) {
      throw new Error('Honua embed extension controls require a non-empty label.');
    }

    const outlet = this.#root().querySelector<HTMLElement>(this.#controlsSelector);
    if (!outlet) {
      throw new Error(`Missing Honua embed extension outlet: ${this.#controlsSelector}`);
    }

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'extension-control';
    button.setAttribute('part', ['extension-control', options.part].filter(Boolean).join(' '));
    button.setAttribute('aria-label', label);
    button.title = options.title ?? label;
    button.disabled = options.disabled ?? false;
    button.textContent = options.text ?? label;

    if (options.id?.trim()) {
      button.dataset.honuaExtensionControl = options.id.trim();
    }

    button.addEventListener('click', (event) => {
      options.onClick?.(event, this.#context(extensionId));
    });

    outlet.append(button);
    setOutletActive(outlet);

    let removed = false;
    const contribution: HonuaEmbedContribution = {
      element: button,
      remove() {
        if (removed) {
          return;
        }

        removed = true;
        button.remove();
        setOutletActive(outlet);
      },
    };

    this.#active.get(extensionId)?.contributions.push(contribution);
    return contribution;
  }

  #setCssVariable(name: string, value: string | null): void {
    if (!name.startsWith('--')) {
      throw new Error(`Honua embed CSS variables must start with "--": ${name}`);
    }

    if (value === null) {
      this.#element.style.removeProperty(name);
      return;
    }

    this.#element.style.setProperty(name, value);
  }

  #root(): ShadowRoot {
    const root = this.#element.shadowRoot;
    if (!root) {
      throw new Error('Honua embed extensions require an open shadow root.');
    }

    return root;
  }

  #emitError(
    extensionId: string,
    lifecycle: HonuaEmbedExtensionErrorDetail['lifecycle'],
    error: unknown,
  ): void {
    this.#element.dispatchEvent(new CustomEvent<HonuaEmbedExtensionErrorDetail>('honua-embed-extension-error', {
      bubbles: true,
      composed: true,
      detail: {
        extensionId,
        target: this.#target,
        lifecycle,
        error,
      },
    }));
  }
}

export function createHonuaEmbedExtensionHost<TTarget extends HonuaEmbedTarget>(
  options: HonuaEmbedExtensionHostOptions<TTarget>,
): HonuaEmbedExtensionHost<TTarget> {
  return new HonuaEmbedExtensionHost(options);
}

function sortedExtensions(): HonuaEmbedExtension[] {
  return [...extensions.values()].sort((left, right) => {
    const priority = (left.priority ?? 0) - (right.priority ?? 0);
    return priority === 0 ? left.id.localeCompare(right.id) : priority;
  });
}

function extensionTargets(extension: HonuaEmbedExtension): readonly HonuaEmbedTarget[] {
  const target = extension.target;
  if (!target) {
    return ['map', 'scene'];
  }

  return typeof target === 'string' ? [target] : [...target];
}

function setOutletActive(outlet: HTMLElement): void {
  const active = outlet.childElementCount > 0 ? 'true' : 'false';
  outlet.dataset.honuaExtensionActive = active;

  const parent = outlet.parentElement;
  if (parent?.classList.contains('controls')) {
    parent.dataset.honuaExtensionActive = active;
  }
}
