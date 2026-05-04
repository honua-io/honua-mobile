import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setupScene, type SceneSetup } from './test-utils';

describe('honua-scene-timeline', () => {
  let setup: SceneSetup;

  beforeEach(async () => {
    document.body.replaceChildren();
    setup = await setupScene();
  });

  afterEach(() => {
    setup.cleanup();
  });

  it('renders one tab per timeline phase', () => {
    const control = createTimelineControl(setup);
    expect(control.shadowRoot!.querySelectorAll('button.phase')).toHaveLength(2);
  });

  it('emits honua-scene-timeline-change and toggles layer visibility', () => {
    const control = createTimelineControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-timeline-change', listener);

    const phase = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-phase-id="phase-2"]',
    )!;
    phase.click();

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      phaseId: 'phase-2',
      visibleLayerIds: ['as-built', 'design-overlay'],
      controlId: 'timeline',
    });
    expect(setup.scene.getLayer('as-built')?.visible).toBe(true);
    expect(setup.scene.getLayer('design-overlay')?.visible).toBe(true);
  });

  it('hides layers that are not part of the active phase', () => {
    const control = createTimelineControl(setup);
    const phase = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-phase-id="phase-1"]',
    )!;
    phase.click();
    expect(setup.scene.getLayer('design-overlay')?.visible).toBe(false);
    expect(setup.scene.getLayer('as-built')?.visible).toBe(true);
  });

  it('toggles the implicit primary layer when phases reference it', async () => {
    setup.cleanup();
    setup = await setupScene({
      schema: 'honua-scene-metadata/v1',
      id: 'with-primary',
      name: 'Scene with implicit primary',
      layers: [
        {
          id: 'as-built',
          title: 'As-built',
          kind: '3d-tiles',
          url: 'https://example.test/as-built/tileset.json',
          visible: true,
          opacity: 1,
        },
      ],
      timeline: {
        phases: [
          {
            id: 'primary-only',
            title: 'Primary only',
            startUtc: '2026-01-01T00:00:00Z',
            endUtc: '2026-01-31T00:00:00Z',
            visibleLayerIds: ['primary'],
          },
          {
            id: 'as-built-only',
            title: 'As-built only',
            startUtc: '2026-02-01T00:00:00Z',
            endUtc: '2026-02-28T00:00:00Z',
            visibleLayerIds: ['as-built'],
          },
        ],
      },
    });
    await setup.scene.addLayer({
      id: 'primary',
      title: 'Primary',
      kind: '3d-tiles',
      url: 'https://example.test/primary/tileset.json',
      visible: true,
      opacity: 1,
    });

    const control = createTimelineControl(setup);
    const primaryOnly = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-phase-id="primary-only"]',
    )!;
    primaryOnly.click();
    expect(setup.scene.getLayer('primary')?.visible).toBe(true);
    expect(setup.scene.getLayer('as-built')?.visible).toBe(false);

    const asBuiltOnly = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-phase-id="as-built-only"]',
    )!;
    asBuiltOnly.click();
    expect(setup.scene.getLayer('primary')?.visible).toBe(false);
    expect(setup.scene.getLayer('as-built')?.visible).toBe(true);
  });
});

function createTimelineControl(setup: SceneSetup): HTMLElement {
  setup.scene.id = 'scene-under-test';
  const control = document.createElement('honua-scene-timeline');
  control.setAttribute('for', '#scene-under-test');
  document.body.append(control);
  return control;
}
