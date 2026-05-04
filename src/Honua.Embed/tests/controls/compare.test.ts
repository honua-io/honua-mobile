import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setupScene, type SceneSetup } from './test-utils';

describe('honua-scene-compare', () => {
  let setup: SceneSetup;

  beforeEach(async () => {
    document.body.replaceChildren();
    setup = await setupScene();
  });

  afterEach(() => {
    setup.cleanup();
  });

  it('renders compare modes and side buttons', () => {
    const control = createCompareControl(setup);
    expect(control.shadowRoot!.querySelectorAll('.mode')).toHaveLength(1);
    expect(control.shadowRoot!.querySelectorAll('.mode-buttons button')).toHaveLength(3);
  });

  it('toggles layer visibility when activating a side', () => {
    const control = createCompareControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-compare-set', listener);

    const button = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-mode-id="design-vs-asbuilt"][data-side="left"]',
    )!;
    button.click();

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      modeId: 'design-vs-asbuilt',
      side: 'left',
      leftLayerIds: ['design-overlay'],
      rightLayerIds: ['as-built'],
      controlId: 'compare',
    });
    expect(setup.scene.getLayer('design-overlay')?.visible).toBe(true);
    expect(setup.scene.getLayer('as-built')?.visible).toBe(false);
  });

  it('shows both sides when "both" is selected', () => {
    const control = createCompareControl(setup);
    const button = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-mode-id="design-vs-asbuilt"][data-side="both"]',
    )!;
    button.click();

    expect(setup.scene.getLayer('design-overlay')?.visible).toBe(true);
    expect(setup.scene.getLayer('as-built')?.visible).toBe(true);
  });
});

function createCompareControl(setup: SceneSetup): HTMLElement {
  setup.scene.id = 'scene-under-test';
  const control = document.createElement('honua-scene-compare');
  control.setAttribute('for', '#scene-under-test');
  document.body.append(control);
  return control;
}
