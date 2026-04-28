import { beforeEach, describe, expect, it, vi } from 'vitest';
import { defineHonuaMapElement, HonuaMapElement } from '../src/index';

describe('honua-map', () => {
  beforeEach(() => {
    defineHonuaMapElement();
    document.body.replaceChildren();
  });

  it('defines the custom element idempotently', () => {
    const first = defineHonuaMapElement();
    const second = defineHonuaMapElement();

    expect(first).toBe(HonuaMapElement);
    expect(second).toBe(HonuaMapElement);
    expect(customElements.get('honua-map')).toBe(HonuaMapElement);
  });

  it('parses declarative attributes into map config', () => {
    const element = document.createElement('honua-map');
    element.setAttribute('service-url', 'https://services.honua.test/FeatureServer');
    element.setAttribute('layer-ids', 'parcels, roads, zoning');
    element.setAttribute('api-key', 'secret-key');
    element.setAttribute('center', '21.3069,-157.8583');
    element.setAttribute('zoom', '13');
    element.setAttribute('bbox', '-158.3,21.2,-157.6,21.6');
    element.setAttribute('basemap', 'satellite');
    element.setAttribute('interactive', '');
    element.setAttribute('search', '');
    element.setAttribute('identify', '');
    element.setAttribute('attribution', 'City GIS');

    document.body.append(element);

    expect(element.config).toMatchObject({
      serviceUrl: 'https://services.honua.test/FeatureServer',
      layerIds: ['parcels', 'roads', 'zoning'],
      apiKey: 'secret-key',
      center: { latitude: 21.3069, longitude: -157.8583 },
      zoom: 13,
      bounds: {
        minLongitude: -158.3,
        minLatitude: 21.2,
        maxLongitude: -157.6,
        maxLatitude: 21.6,
      },
      basemap: 'satellite',
      interactive: true,
      search: true,
      identify: true,
      attribution: 'City GIS',
    });
  });

  it('treats false boolean attribute values as disabled', () => {
    const element = document.createElement('honua-map');
    element.setAttribute('interactive', 'false');
    element.setAttribute('search', '0');
    element.setAttribute('identify', 'no');

    document.body.append(element);

    expect(element.config).toMatchObject({
      interactive: false,
      search: false,
      identify: false,
    });
    expect(element.shadowRoot!.querySelector<HTMLElement>('.map')!.tabIndex).toBe(-1);
  });

  it('renders layer chips and omits api keys from the shadow DOM', () => {
    const element = document.createElement('honua-map');
    element.setAttribute('layer-ids', 'assets, work-orders');
    element.setAttribute('api-key', 'do-not-render');

    document.body.append(element);

    const layerText = [...element.shadowRoot!.querySelectorAll('.layer')]
      .map((node) => node.textContent);

    expect(layerText).toEqual(['assets', 'work-orders']);
    expect(element.shadowRoot!.textContent).not.toContain('do-not-render');
  });

  it('dispatches search events from the optional search control', () => {
    const element = document.createElement('honua-map');
    element.setAttribute('search', '');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-map-search', listener);

    const input = element.shadowRoot!.querySelector<HTMLInputElement>('input[type="search"]')!;
    const form = element.shadowRoot!.querySelector<HTMLFormElement>('form')!;
    input.value = 'hydrants';
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail.query).toBe('hydrants');
  });

  it('keeps disabled controls inert even when events are dispatched programmatically', () => {
    const element = document.createElement('honua-map');
    element.setAttribute('zoom', '10');
    element.setAttribute('search', 'false');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-map-search', listener);

    const input = element.shadowRoot!.querySelector<HTMLInputElement>('input[type="search"]')!;
    const form = element.shadowRoot!.querySelector<HTMLFormElement>('form')!;
    const zoomIn = element.shadowRoot!.querySelector<HTMLButtonElement>('[data-action="zoom-in"]')!;
    input.value = 'hydrants';
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    zoomIn.click();

    expect(listener).not.toHaveBeenCalled();
    expect(element.config.zoom).toBe(10);
  });

  it('dispatches identify events only when identify is enabled', () => {
    const element = document.createElement('honua-map');
    document.body.append(element);
    const listener = vi.fn();
    element.addEventListener('honua-map-identify', listener);

    element.identifyAt(12, 34);
    expect(listener).not.toHaveBeenCalled();

    element.setAttribute('identify', '');
    element.identifyAt(12, 34);

    expect(listener).toHaveBeenCalledOnce();
    expect(listener.mock.calls[0][0].detail).toMatchObject({ x: 12, y: 34 });
    expect(element.shadowRoot!.querySelector<HTMLOutputElement>('.popup')!.dataset.open).toBe('true');
  });

  it('updates view through the public API and keyboard zoom controls', () => {
    const element = document.createElement('honua-map');
    element.setAttribute('interactive', '');
    document.body.append(element);

    element.setView({ latitude: 20.75, longitude: -156.45 }, 9);
    expect(element.getAttribute('center')).toBe('20.75,-156.45');
    expect(element.getAttribute('zoom')).toBe('9');

    const map = element.shadowRoot!.querySelector<HTMLElement>('.map')!;
    map.dispatchEvent(new KeyboardEvent('keydown', { key: '+', bubbles: true }));
    expect(element.config.zoom).toBe(10);
  });

  it('does not show Honua branding by default', () => {
    const element = document.createElement('honua-map');
    document.body.append(element);

    const visibleText = [
      ...element.shadowRoot!.querySelectorAll('.layer, .meta, .popup'),
    ].map((node) => node.textContent).join(' ');

    expect(visibleText.toLowerCase()).not.toContain('honua');
  });
});
