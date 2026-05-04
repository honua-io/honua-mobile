import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setupScene, type SceneSetup } from './test-utils';

describe('honua-scene-measure', () => {
  let setup: SceneSetup;

  beforeEach(async () => {
    document.body.replaceChildren();
    setup = await setupScene();
  });

  afterEach(() => {
    setup.cleanup();
  });

  it('renders point/line/polygon mode buttons', () => {
    const control = createMeasureControl(setup);
    const buttons = control.shadowRoot!.querySelectorAll('.modes button');
    expect([...buttons].map((node) => node.getAttribute('data-kind'))).toEqual([
      'point',
      'line',
      'polygon',
    ]);
  });

  it('emits honua-scene-measurement-add on a single point pick', () => {
    const control = createMeasureControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-measurement-add', listener);

    const pointButton = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-kind="point"]',
    )!;
    pointButton.click();

    setup.scene.dispatchEvent(
      new CustomEvent('honua-scene-identify', {
        bubbles: true,
        composed: true,
        detail: {
          x: 10,
          y: 12,
          picked: {
            position: { latitude: 39.95, longitude: -75.16, height: 1500 },
          },
        },
      }),
    );

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      kind: 'point',
      controlId: 'measure',
      points: [{ latitude: 39.95, longitude: -75.16, height: 1500 }],
    });
  });

  it('finalizes a distance measurement when "line" is clicked twice', () => {
    const control = createMeasureControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-measurement-add', listener);

    const lineButton = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-kind="line"]',
    )!;
    lineButton.click();

    setup.scene.dispatchEvent(
      new CustomEvent('honua-scene-identify', {
        bubbles: true,
        composed: true,
        detail: {
          x: 10,
          y: 12,
          picked: { position: { latitude: 39.95, longitude: -75.16, height: 0 } },
        },
      }),
    );
    setup.scene.dispatchEvent(
      new CustomEvent('honua-scene-identify', {
        bubbles: true,
        composed: true,
        detail: {
          x: 11,
          y: 13,
          picked: { position: { latitude: 39.951, longitude: -75.161, height: 0 } },
        },
      }),
    );

    lineButton.click();

    expect(listener).toHaveBeenCalledOnce();
    const detail = listener.mock.calls[0][0].detail;
    expect(detail.kind).toBe('line');
    expect(detail.points).toHaveLength(2);
    expect(detail.distance).toBeGreaterThan(0);
  });

  it('reads cartographic position from honua-scene-identify detail', () => {
    const control = createMeasureControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-measurement-add', listener);

    const pointButton = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-kind="point"]',
    )!;
    pointButton.click();

    setup.scene.dispatchEvent(
      new CustomEvent('honua-scene-identify', {
        bubbles: true,
        composed: true,
        detail: {
          x: 1,
          y: 2,
          picked: { id: 'feature-1' },
          position: { latitude: 21.31, longitude: -157.86, height: 12 },
        },
      }),
    );

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail.points).toEqual([
      { latitude: 21.31, longitude: -157.86, height: 12 },
    ]);
  });

  it('falls back to scene.samplePoint when identify detail lacks a position', () => {
    const control = createMeasureControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-measurement-add', listener);

    const sampleSpy = vi
      .spyOn(setup.scene, 'samplePoint')
      .mockReturnValue({ latitude: 39.95, longitude: -75.16, height: 0 });

    const pointButton = control.shadowRoot!.querySelector<HTMLButtonElement>(
      'button[data-kind="point"]',
    )!;
    pointButton.click();

    setup.scene.dispatchEvent(
      new CustomEvent('honua-scene-identify', {
        bubbles: true,
        composed: true,
        detail: { x: 4, y: 8, picked: null, position: null },
      }),
    );

    expect(sampleSpy).toHaveBeenCalledWith(4, 8);
    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail.points).toEqual([
      { latitude: 39.95, longitude: -75.16, height: 0 },
    ]);

    sampleSpy.mockRestore();
  });

  it('emits honua-scene-measurement-clear on Clear', () => {
    const control = createMeasureControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-measurement-clear', listener);

    const clearButton = control.shadowRoot!.querySelector<HTMLButtonElement>('.clear')!;
    clearButton.click();

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      controlId: 'measure',
      measurementId: 'all',
    });
  });
});

function createMeasureControl(setup: SceneSetup): HTMLElement {
  setup.scene.id = 'scene-under-test';
  const control = document.createElement('honua-scene-measure');
  control.setAttribute('for', '#scene-under-test');
  document.body.append(control);
  return control;
}
