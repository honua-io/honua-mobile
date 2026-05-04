import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setupScene, type SceneSetup } from './test-utils';

describe('honua-scene-inspector', () => {
  let setup: SceneSetup;

  beforeEach(async () => {
    document.body.replaceChildren();
    setup = await setupScene();
  });

  afterEach(() => {
    setup.cleanup();
  });

  it('shows the empty state until a feature is selected', () => {
    const control = createInspectorControl(setup);
    expect(control.shadowRoot!.querySelector<HTMLElement>('[data-empty]')!.hidden).toBe(false);
    expect(control.shadowRoot!.querySelector<HTMLElement>('.fields')!.hidden).toBe(true);
  });

  it('emits honua-scene-feature-select on a scene identify event', () => {
    const control = createInspectorControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-feature-select', listener);

    setup.scene.dispatchEvent(
      new CustomEvent('honua-scene-identify', {
        bubbles: true,
        composed: true,
        detail: {
          x: 10,
          y: 20,
          picked: {
            id: 'asset-42',
            properties: {
              id: 'asset-42',
              phase: 'phase-2',
              elevation: 12,
            },
          },
        },
      }),
    );

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      featureId: 'asset-42',
      attributes: {
        id: 'asset-42',
        phase: 'phase-2',
        elevation: 12,
      },
      controlId: 'inspector',
    });
  });

  it('renders inspector field titles from metadata', () => {
    const control = createInspectorControl(setup);
    setup.scene.dispatchEvent(
      new CustomEvent('honua-scene-identify', {
        bubbles: true,
        composed: true,
        detail: {
          x: 0,
          y: 0,
          picked: {
            id: 'asset-1',
            getProperty: (key: string) => {
              if (key === 'id') return 'asset-1';
              if (key === 'phase') return 'phase-1';
              if (key === 'elevation') return 5;
              return undefined;
            },
          },
        },
      }),
    );

    const fields = control.shadowRoot!.querySelectorAll('dt');
    const titles = [...fields].map((node) => node.textContent);
    expect(titles).toEqual(['Asset ID', 'Phase', 'Elevation']);
  });
});

function createInspectorControl(setup: SceneSetup): HTMLElement {
  setup.scene.id = 'scene-under-test';
  const control = document.createElement('honua-scene-inspector');
  control.setAttribute('for', '#scene-under-test');
  document.body.append(control);
  return control;
}
