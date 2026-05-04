import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setupScene, SAMPLE_METADATA, type SceneSetup } from './test-utils';

describe('honua-scene-layers', () => {
  let setup: SceneSetup;

  beforeEach(async () => {
    document.body.replaceChildren();
    setup = await setupScene();
  });

  afterEach(() => {
    setup.cleanup();
  });

  it('renders one toggle per metadata layer', () => {
    const control = createLayersControl(setup);
    const items = control.shadowRoot!.querySelectorAll('[data-layer-id]');
    expect(items).toHaveLength(SAMPLE_METADATA.layers!.length);
  });

  it('emits honua-scene-layer-toggle and updates the scene', () => {
    const control = createLayersControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-layer-toggle', listener);
    setup.scene.setLayerVisibility('as-built', true);

    const checkbox = control.shadowRoot!.querySelector<HTMLInputElement>(
      'input[type="checkbox"]',
    )!;
    checkbox.checked = false;
    checkbox.dispatchEvent(new Event('change', { bubbles: true }));

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      layerId: 'as-built',
      visible: false,
      controlId: 'layers',
    });
    expect(setup.scene.getLayer('as-built')?.visible).toBe(false);
  });

  it('emits honua-scene-layer-opacity when the slider moves', () => {
    const control = createLayersControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-layer-opacity', listener);

    const range = control.shadowRoot!.querySelector<HTMLInputElement>(
      'input[type="range"]',
    )!;
    range.value = '0.5';
    range.dispatchEvent(new Event('input', { bubbles: true }));

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      layerId: 'as-built',
      opacity: 0.5,
      controlId: 'layers',
    });
    expect(setup.scene.getLayer('as-built')?.opacity).toBe(0.5);
  });

  it('rerenders when the scene metadata changes', () => {
    const control = createLayersControl(setup);
    expect(control.shadowRoot!.querySelectorAll('[data-layer-id]')).toHaveLength(2);

    setup.scene.metadata = {
      ...SAMPLE_METADATA,
      layers: [SAMPLE_METADATA.layers![0]],
    };

    expect(control.shadowRoot!.querySelectorAll('[data-layer-id]')).toHaveLength(1);
  });

  it('renders the empty state when no scene is bound', () => {
    const control = document.createElement('honua-scene-layers');
    control.setAttribute('for', '#missing');
    document.body.append(control);

    const empty = control.shadowRoot!.querySelector<HTMLElement>('.empty')!;
    expect(empty.hidden).toBe(false);
    expect(control.shadowRoot!.querySelectorAll('[data-layer-id]')).toHaveLength(0);
  });
});

function createLayersControl(setup: SceneSetup): HTMLElement {
  setup.scene.id = 'scene-under-test';
  const control = document.createElement('honua-scene-layers');
  control.setAttribute('for', '#scene-under-test');
  document.body.append(control);
  return control;
}
