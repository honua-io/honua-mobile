import { beforeEach, describe, expect, it, vi } from 'vitest';
import {
  bindHonuaMapGovernance,
  createHonuaMapGovernanceEvent,
  defineHonuaMapElement,
  evaluateHonuaMapPolicy,
  type HonuaMapConfig,
  type HonuaMapElement,
  type HonuaMapGovernanceEvent,
} from '../src/index';

describe('honua map governance', () => {
  beforeEach(() => {
    defineHonuaMapElement();
    document.body.replaceChildren();
  });

  it('records redacted view and interaction analytics from map events', () => {
    const events: HonuaMapGovernanceEvent[] = [];
    const element = createConfiguredMap();
    const binding = bindHonuaMapGovernance(element, {
      sink: (event) => events.push(event),
      integrationId: 'portal-embed',
      origin: 'https://portal.example.test',
      metadata: { tenantId: 'city' },
      clock: () => new Date('2026-05-23T17:15:00Z'),
    });

    document.body.append(element);
    const input = element.shadowRoot!.querySelector<HTMLInputElement>('input[type="search"]')!;
    const form = element.shadowRoot!.querySelector<HTMLFormElement>('form')!;
    input.value = 'hydrants';
    form.dispatchEvent(new SubmitEvent('submit', { bubbles: true, cancelable: true }));
    element.identifyAt(10, 20);
    binding.disconnect();

    expect(events.map((event) => event.type)).toEqual(['view', 'search', 'identify']);
    expect(events[0]).toMatchObject({
      integrationId: 'portal-embed',
      origin: 'https://portal.example.test',
      serviceOrigin: 'https://services.example.test',
      layerIds: ['assets', 'work-orders'],
      apiKeyPresent: true,
      metadata: { tenantId: 'city' },
      occurredAt: '2026-05-23T17:15:00.000Z',
    });
    expect(events[1].searchQuery).toBe('hydrants');
    expect(events[2].identifyPoint).toEqual({ x: 10, y: 20 });
    expect(JSON.stringify(events)).not.toContain('secret-browser-key');
  });

  it('emits policy-denied events when host-provided policy blocks the embed', () => {
    const events: HonuaMapGovernanceEvent[] = [];
    const denied = vi.fn();
    const element = createConfiguredMap({ apiKey: null, layerIds: 'assets,restricted' });
    element.addEventListener('honua-map-policy-denied', denied);

    bindHonuaMapGovernance(element, {
      sink: (event) => events.push(event),
      origin: 'https://portal.example.test',
      policy: {
        requiredApiKey: true,
        allowedOrigins: ['https://portal.example.test'],
        allowedServiceOrigins: ['https://services.example.test'],
        allowedLayerIds: ['assets'],
      },
    });
    document.body.append(element);

    expect(events).toHaveLength(1);
    expect(events[0].type).toBe('policy-denied');
    expect(events[0].policyDecision?.allowed).toBe(false);
    expect(events[0].policyDecision?.reasons).toEqual([
      'API key is required for this embed.',
      'Layer ids are not allowed for this API key: restricted',
    ]);
    expect(denied).toHaveBeenCalledOnce();
    expect(denied.mock.calls[0][0].detail.decision.allowed).toBe(false);
  });

  it('records a single view when binding after the element is connected', () => {
    const events: HonuaMapGovernanceEvent[] = [];
    const element = createConfiguredMap();
    document.body.append(element);

    const binding = bindHonuaMapGovernance(element, {
      sink: (event) => events.push(event),
    });
    element.dispatchEvent(new CustomEvent('honua-map-ready', {
      bubbles: true,
      composed: true,
      detail: element.config,
    }));
    binding.disconnect();

    expect(events.map((event) => event.type)).toEqual(['view']);
  });

  it('evaluates origin, service, layer, and rate-limit policy without server DTOs', () => {
    const config = createConfig();

    const decision = evaluateHonuaMapPolicy(config, {
      requiredApiKey: true,
      allowedOrigins: ['https://other.example.test'],
      allowedServiceOrigins: ['https://services.example.test'],
      allowedLayerIds: ['assets'],
      rateLimit: {
        remaining: 0,
        resetAt: '2026-05-23T18:00:00Z',
      },
    }, {
      origin: 'https://portal.example.test',
    });

    expect(decision.allowed).toBe(false);
    expect(decision.reasons).toEqual([
      'Embed origin is not allowed for this API key.',
      'Layer ids are not allowed for this API key: work-orders',
      'API key rate limit is exhausted until 2026-05-23T18:00:00Z.',
    ]);
  });

  it('creates safe analytics snapshots from config without carrying credentials', () => {
    const event = createHonuaMapGovernanceEvent(createConfig(), 'config-change', {
      occurredAt: '2026-05-23T17:20:00.000Z',
      integrationId: 'builder-preview',
      origin: 'https://portal.example.test',
    });

    expect(event).toMatchObject({
      type: 'config-change',
      integrationId: 'builder-preview',
      apiKeyPresent: true,
      serviceUrl: 'https://services.example.test/FeatureServer',
      serviceOrigin: 'https://services.example.test',
    });
    expect(JSON.stringify(event)).not.toContain('secret-browser-key');
  });
});

function createConfiguredMap(options: {
  apiKey?: string | null;
  layerIds?: string;
} = {}): HonuaMapElement {
  const element = document.createElement('honua-map') as HonuaMapElement;
  element.setAttribute('service-url', 'https://services.example.test/FeatureServer');
  element.setAttribute('layer-ids', options.layerIds ?? 'assets,work-orders');
  if (options.apiKey !== null) {
    element.setAttribute('api-key', options.apiKey ?? 'secret-browser-key');
  }

  element.setAttribute('search', '');
  element.setAttribute('identify', '');
  return element;
}

function createConfig(): HonuaMapConfig {
  return {
    serviceUrl: 'https://services.example.test/FeatureServer',
    layerIds: ['assets', 'work-orders'],
    apiKey: 'secret-browser-key',
    center: null,
    zoom: 10,
    bounds: null,
    basemap: 'streets',
    interactive: true,
    search: true,
    identify: true,
    attribution: null,
    theme: 'light',
    label: 'Embedded map',
  };
}
