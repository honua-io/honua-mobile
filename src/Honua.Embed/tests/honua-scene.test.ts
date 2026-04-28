import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineHonuaSceneElement, HonuaSceneElement } from '../src/index';

describe('honua-scene', () => {
  beforeEach(() => {
    defineHonuaSceneElement();
    document.body.replaceChildren();
  });

  it('defines the custom element idempotently', () => {
    const first = defineHonuaSceneElement();
    const second = defineHonuaSceneElement();

    expect(first).toBe(HonuaSceneElement);
    expect(second).toBe(HonuaSceneElement);
    expect(customElements.get('honua-scene')).toBe(HonuaSceneElement);
  });

  it('parses declarative attributes into scene config', () => {
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/tileset.json');
    element.setAttribute('terrain-url', 'https://data.example.test/terrain');
    element.setAttribute('ion-token', 'secret-ion-token');
    element.setAttribute('cesium-base-url', '/assets/cesium');
    element.setAttribute('center', '21.3069,-157.8583');
    element.setAttribute('height', '1800');
    element.setAttribute('heading', '30');
    element.setAttribute('pitch', '-35');
    element.setAttribute('roll', '2');
    element.setAttribute('theme', 'light');
    element.setAttribute('autoload', 'false');

    document.body.append(element);

    expect(element.config).toMatchObject({
      tilesetUrl: 'https://data.example.test/tileset.json',
      terrainUrl: 'https://data.example.test/terrain',
      ionToken: 'secret-ion-token',
      cesiumBaseUrl: '/assets/cesium/',
      center: { latitude: 21.3069, longitude: -157.8583 },
      height: 1800,
      orientation: {
        heading: 30,
        pitch: -35,
        roll: 2,
      },
      theme: 'light',
      autoload: false,
    });
  });

  it('updates camera attributes through the public API', () => {
    const element = document.createElement('honua-scene');
    document.body.append(element);

    element.setView(
      { latitude: 20.75, longitude: -156.45 },
      2500,
      { heading: 90, pitch: -50, roll: 0 },
    );

    expect(element.getAttribute('center')).toBe('20.75,-156.45');
    expect(element.getAttribute('height')).toBe('2500');
    expect(element.getAttribute('heading')).toBe('90');
    expect(element.getAttribute('pitch')).toBe('-50');
    expect(element.getAttribute('roll')).toBe('0');
  });

  it('emits an actionable load error when WebGL is unavailable', async () => {
    const element = document.createElement('honua-scene');
    element.setAttribute('tileset-url', 'https://data.example.test/tileset.json');
    element.setAttribute('autoload', 'false');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-scene-load-error', listener);

    await element.load();

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({
      source: 'webgl',
      message: '3D scenes require WebGL support in the host browser.',
    });
  });

  it('does not render access tokens or Honua branding by default', () => {
    const element = document.createElement('honua-scene');
    element.setAttribute('ion-token', 'do-not-render');
    document.body.append(element);

    const visibleText = [
      ...element.shadowRoot!.querySelectorAll('.status'),
    ].map((node) => node.textContent).join(' ');

    expect(visibleText).not.toContain('do-not-render');
    expect(visibleText.toLowerCase()).not.toContain('honua');
  });
});
