import type { HonuaMapConfig } from './map';
import type { HonuaSceneConfig } from './scene';

export type HonuaEmbedTarget = 'map' | 'scene';

export interface HonuaEmbedConfigByTarget {
  map: HonuaMapConfig;
  scene: HonuaSceneConfig;
}

export type HonuaEmbedExtensionCleanup = () => void;
export type HonuaEmbedExtensionTrustState = 'approved' | 'untrusted' | 'revoked';

export interface HonuaEmbedExtensionContext<TTarget extends HonuaEmbedTarget = HonuaEmbedTarget> {
  readonly target: TTarget;
  readonly element: HTMLElement;
  readonly shadowRoot: ShadowRoot;
  readonly config: HonuaEmbedConfigByTarget[TTarget];
  hasPermission(permission: string): boolean;
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

export interface HonuaEmbedExtensionPermission {
  permission: string;
  required?: boolean;
  reason?: string;
}

export interface HonuaEmbedExtensionRegistrationOptions {
  trustState?: HonuaEmbedExtensionTrustState;
  permissions?: readonly HonuaEmbedExtensionPermission[];
  grantedPermissions?: readonly string[];
}

export interface HonuaEmbedExtensionDescriptor {
  readonly id: string;
  readonly target: readonly HonuaEmbedTarget[];
  readonly priority: number;
  readonly trustState: HonuaEmbedExtensionTrustState;
  readonly permissions: readonly string[];
}

export interface HonuaEmbedExtensionErrorDetail {
  extensionId: string;
  target: HonuaEmbedTarget;
  lifecycle: 'activate' | 'configChanged' | 'command' | 'deactivate';
  message: string;
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
  policy: HonuaEmbedExtensionRegistrationPolicy;
  cleanup?: HonuaEmbedExtensionCleanup;
  contributions: HonuaEmbedContribution[];
}

interface HonuaEmbedExtensionRegistrationPolicy {
  trustState: HonuaEmbedExtensionTrustState;
  permissions: readonly HonuaEmbedExtensionPermission[];
  grantedPermissions: ReadonlySet<string>;
}

interface RegisteredExtension {
  extension: HonuaEmbedExtension;
  policy: HonuaEmbedExtensionRegistrationPolicy;
}

const DEFAULT_CONTROLS_SELECTOR = '[data-honua-extension-controls]';
const extensions = new Map<string, RegisteredExtension>();
const hosts = new Set<HonuaEmbedExtensionHost<HonuaEmbedTarget>>();

export function registerHonuaEmbedExtension<TTarget extends HonuaEmbedTarget>(
  extension: HonuaEmbedExtension<TTarget>,
  options?: HonuaEmbedExtensionRegistrationOptions,
): HonuaEmbedExtensionRegistration {
  const id = extension.id.trim();
  if (!id) {
    throw new Error('Honua embed extensions require a non-empty id.');
  }

  if (extensions.has(id)) {
    throw new Error(`A Honua embed extension is already registered with id "${id}".`);
  }

  const policy = normalizeRegistrationPolicy(id, options);
  const normalized = { ...extension, id } as HonuaEmbedExtension;
  extensions.set(id, { extension: normalized, policy });
  for (const host of hosts) {
    host.activate(normalized, policy);
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
  return sortedExtensionEntries()
    .filter((entry) => !target || extensionTargets(entry.extension).includes(target))
    .map((entry) => ({
      id: entry.extension.id,
      target: extensionTargets(entry.extension),
      priority: entry.extension.priority ?? 0,
      trustState: entry.policy.trustState,
      permissions: entry.policy.permissions.map((permission) => permission.permission),
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
    for (const entry of sortedExtensionEntries()) {
      this.activate(entry.extension, entry.policy);
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

  activate(
    extension: HonuaEmbedExtension,
    policy: HonuaEmbedExtensionRegistrationPolicy = createDefaultRegistrationPolicy(),
  ): void {
    if (!this.#connected || this.#active.has(extension.id) || !extensionTargets(extension).includes(this.#target)) {
      return;
    }

    const active: ActiveExtension = { extension, policy, contributions: [] };
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
      hasPermission(permission) {
        const active = thisHost.#active.get(extensionId);
        return active?.policy.grantedPermissions.has(permission.trim()) ?? false;
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
      try {
        options.onClick?.(event, this.#context(extensionId));
      } catch (error) {
        this.#emitError(extensionId, 'command', error);
      }
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
        message: redactExtensionError(error),
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

function sortedExtensionEntries(): RegisteredExtension[] {
  return [...extensions.values()].sort((left, right) => {
    const priority = (left.extension.priority ?? 0) - (right.extension.priority ?? 0);
    return priority === 0 ? left.extension.id.localeCompare(right.extension.id) : priority;
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

function createDefaultRegistrationPolicy(): HonuaEmbedExtensionRegistrationPolicy {
  return {
    trustState: 'approved',
    permissions: [],
    grantedPermissions: new Set(),
  };
}

function normalizeRegistrationPolicy(
  extensionId: string,
  options?: HonuaEmbedExtensionRegistrationOptions,
): HonuaEmbedExtensionRegistrationPolicy {
  const trustState = options?.trustState ?? 'approved';
  if (trustState !== 'approved') {
    throw new Error(`Honua embed extension "${extensionId}" is not approved to load: ${trustState}.`);
  }

  const permissions = options?.permissions ?? [];
  const grantedPermissions = new Set((options?.grantedPermissions ?? [])
    .map((permission) => permission.trim())
    .filter(Boolean));
  const missingRequired = permissions.find((permission) =>
    permission.required !== false && !grantedPermissions.has(permission.permission.trim()));
  if (missingRequired) {
    throw new Error(
      `Honua embed extension "${extensionId}" requires permission "${missingRequired.permission}" before registration.`,
    );
  }

  return {
    trustState,
    permissions,
    grantedPermissions,
  };
}

function redactExtensionError(error: unknown): string {
  const message = error instanceof Error ? error.message : String(error);
  return message
    .replace(/\b(authorization)\s*[:=]\s*(?:bearer|basic)?\s*[A-Za-z0-9._~+/=-]+/gi, '$1=[redacted]')
    .replace(/\b(access[_-]?token|refresh[_-]?token|token|x[_-]?api[_-]?key|api[_-]?key|apikey|access[_-]?key|accesskey|password|passwd|secret|client[_-]?secret|credential)\b\s*[:=]\s*["']?[^"'&\s,;]+["']?/gi, '$1=[redacted]')
    .slice(0, 1000);
}
