import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { setupScene, type SceneSetup } from './test-utils';

describe('honua-scene-bookmarks', () => {
  let setup: SceneSetup;

  beforeEach(async () => {
    document.body.replaceChildren();
    setup = await setupScene();
  });

  afterEach(() => {
    setup.cleanup();
  });

  it('renders one entry per metadata bookmark', () => {
    const control = createBookmarksControl(setup);
    expect(control.shadowRoot!.querySelectorAll('button.bookmark')).toHaveLength(2);
  });

  it('emits honua-scene-bookmark-apply and applies the view', () => {
    const control = createBookmarksControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-bookmark-apply', listener);

    const button = control.shadowRoot!.querySelector<HTMLButtonElement>('button.bookmark[data-bookmark-id="entrance"]')!;
    button.click();

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      bookmarkId: 'entrance',
      controlId: 'bookmarks',
    });
    expect(setup.scene.getAttribute('center')).toBe('39.95,-75.16');
    expect(setup.scene.getAttribute('height')).toBe('80');
  });

  it('emits honua-scene-control-error when calling apply with an unknown id', () => {
    const control = createBookmarksControl(setup);
    const listener = vi.fn();
    control.addEventListener('honua-scene-control-error', listener);

    const result = (control as unknown as { apply(id: string): boolean }).apply('does-not-exist');
    expect(result).toBe(false);
    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      controlId: 'bookmarks',
      kind: 'bookmark-not-found',
    });
  });
});

function createBookmarksControl(setup: SceneSetup): HTMLElement {
  setup.scene.id = 'scene-under-test';
  const control = document.createElement('honua-scene-bookmarks');
  control.setAttribute('for', '#scene-under-test');
  document.body.append(control);
  return control;
}
