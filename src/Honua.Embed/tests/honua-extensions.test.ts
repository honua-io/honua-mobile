import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import {
  defineHonuaMapElement,
  defineHonuaSceneElement,
  listHonuaEmbedExtensions,
  registerHonuaEmbedExtension,
  type HonuaEmbedExtensionRegistration,
} from '../src/index';

const registrations: HonuaEmbedExtensionRegistration[] = [];

describe('honua embed extensions', () => {
  beforeEach(() => {
    defineHonuaMapElement();
    defineHonuaSceneElement();
    document.body.replaceChildren();
  });

  afterEach(() => {
    for (const registration of registrations.splice(0)) {
      registration.unregister();
    }
    document.body.replaceChildren();
  });

  it('mounts registered map controls and removes them on unregister', () => {
    const registration = registerHonuaEmbedExtension({
      id: 'isv-locate',
      target: 'map',
      activate(context) {
        context.setCssVariable('--honua-map-accent', '#0f766e');
        context.addControl({
          id: 'locate',
          label: 'Locate asset',
          text: 'L',
          onClick: (_event, clickContext) => {
            clickContext.dispatch('isv-locate', { zoom: clickContext.config.zoom });
          },
        });
      },
    });
    registrations.push(registration);

    const element = document.createElement('honua-map');
    element.setAttribute('zoom', '8');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('isv-locate', listener);

    const button = element.shadowRoot!.querySelector<HTMLButtonElement>('[data-honua-extension-control="locate"]')!;
    button.click();

    expect(button.textContent).toBe('L');
    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toEqual({ zoom: 8 });
    expect(element.style.getPropertyValue('--honua-map-accent')).toBe('#0f766e');
    expect(element.shadowRoot!.querySelector<HTMLElement>('.controls')!.dataset.honuaExtensionActive).toBe('true');

    registration.unregister();

    expect(button.isConnected).toBe(false);
    expect(element.shadowRoot!.querySelector<HTMLElement>('.controls')!.dataset.honuaExtensionActive).toBe('false');
  });

  it('notifies active extensions when element config changes', () => {
    const configChanges: string[] = [];
    const registration = registerHonuaEmbedExtension({
      id: 'isv-config-watch',
      target: 'map',
      activate: vi.fn(),
      configChanged(context) {
        configChanges.push(context.config.basemap);
      },
    });
    registrations.push(registration);

    const element = document.createElement('honua-map');
    document.body.append(element);

    element.setAttribute('basemap', 'satellite');
    element.setAttribute('basemap', 'dark');

    expect(configChanges).toEqual(['satellite', 'dark']);
  });

  it('keeps target-specific extensions scoped to their embed type', () => {
    const registration = registerHonuaEmbedExtension({
      id: 'scene-reset',
      target: 'scene',
      activate(context) {
        context.addControl({
          id: 'reset',
          label: 'Reset view',
          text: 'R',
        });
      },
    });
    registrations.push(registration);

    const map = document.createElement('honua-map');
    const scene = document.createElement('honua-scene');
    document.body.append(map, scene);

    expect(map.shadowRoot!.querySelector('[data-honua-extension-control="reset"]')).toBeNull();
    expect(scene.shadowRoot!.querySelector('[data-honua-extension-control="reset"]')).not.toBeNull();
    expect(listHonuaEmbedExtensions('scene')).toMatchObject([
      { id: 'scene-reset', target: ['scene'], priority: 0 },
    ]);
  });

  it('rejects duplicate extension ids', () => {
    const registration = registerHonuaEmbedExtension({
      id: 'duplicate-extension',
      activate: vi.fn(),
    });
    registrations.push(registration);

    expect(() => registerHonuaEmbedExtension({
      id: 'duplicate-extension',
      activate: vi.fn(),
    })).toThrow(/already registered/);
  });

  it('emits extension lifecycle errors on the host element', () => {
    const error = new Error('failed to activate');
    const registration = registerHonuaEmbedExtension({
      id: 'broken-extension',
      target: 'map',
      activate() {
        throw error;
      },
    });
    registrations.push(registration);

    const element = document.createElement('honua-map');
    const listener = vi.fn();
    element.addEventListener('honua-embed-extension-error', listener);
    document.body.append(element);

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      extensionId: 'broken-extension',
      target: 'map',
      lifecycle: 'activate',
      error,
    });
  });
});
