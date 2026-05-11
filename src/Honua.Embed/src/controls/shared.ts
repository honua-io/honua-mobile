import { createHonuaTemplate } from '../dom';

export {
  assertHonuaDomAvailable,
  defineHonuaCustomElement,
  HonuaHTMLElementBase,
} from '../dom';

export const CONTROL_BASE_STYLES = `
  <style>
    :host {
      --honua-control-background: var(--honua-scene-background, #101820);
      --honua-control-foreground: var(--honua-scene-foreground, #eef5f7);
      --honua-control-muted: var(--honua-scene-muted, #a9b8bf);
      --honua-control-accent: var(--honua-scene-accent, #4fb4c8);
      --honua-control-border: var(--honua-scene-border, rgba(238, 245, 247, 0.18));
      --honua-control-surface: color-mix(in srgb, var(--honua-control-background) 80%, transparent);
      --honua-control-font-family: var(--honua-scene-font-family, Inter, ui-sans-serif, system-ui, sans-serif);
      display: block;
      box-sizing: border-box;
      color: var(--honua-control-foreground);
      font-family: var(--honua-control-font-family);
      font-size: 13px;
    }

    .control {
      display: flex;
      flex-direction: column;
      gap: 8px;
      padding: 12px;
      border: 1px solid var(--honua-control-border);
      border-radius: 8px;
      background: var(--honua-control-background);
    }

    header {
      display: flex;
      align-items: center;
      justify-content: space-between;
      gap: 8px;
    }

    .title {
      margin: 0;
      font-size: 13px;
      font-weight: 600;
      letter-spacing: 0.04em;
      text-transform: uppercase;
      color: var(--honua-control-foreground);
    }

    .body {
      display: flex;
      flex-direction: column;
      gap: 6px;
    }

    button {
      padding: 6px 10px;
      color: var(--honua-control-foreground);
      background: var(--honua-control-surface);
      border: 1px solid var(--honua-control-border);
      border-radius: 6px;
      font: inherit;
      cursor: pointer;
    }

    button:hover,
    button:focus-visible {
      border-color: var(--honua-control-accent);
      outline: none;
    }

    button[aria-pressed="true"] {
      border-color: var(--honua-control-accent);
      color: var(--honua-control-accent);
    }

    ul {
      list-style: none;
      margin: 0;
      padding: 0;
      display: flex;
      flex-direction: column;
      gap: 6px;
    }
  </style>
`;

export function controlTemplate(html: string): HTMLTemplateElement {
  return createHonuaTemplate(html);
}

export function upgradeProperty(element: HTMLElement, propertyName: string): void {
  if (!Object.prototype.hasOwnProperty.call(element, propertyName)) {
    return;
  }

  const value = (element as unknown as Record<string, unknown>)[propertyName];
  delete (element as unknown as Record<string, unknown>)[propertyName];
  (element as unknown as Record<string, unknown>)[propertyName] = value;
}

interface SceneLayerSurface {
  metadata?: { layers?: { id: string }[] | null } | null;
  layers?: ReadonlyArray<{ metadata: { id: string } }>;
}

export function resolveControlLayerIds(scene: SceneLayerSurface): string[] {
  const ids = new Set<string>();
  for (const layer of scene.metadata?.layers ?? []) {
    ids.add(layer.id);
  }
  for (const handle of scene.layers ?? []) {
    ids.add(handle.metadata.id);
  }
  return [...ids];
}
