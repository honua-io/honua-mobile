import { readFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { dirname, resolve } from 'node:path';
import { describe, expect, it } from 'vitest';
import {
  HonuaSceneMetadataError,
  parseHonuaSceneMetadata,
} from '../src/scene-metadata';

const SDK_FIXTURE_PATH = resolve(
  dirname(fileURLToPath(import.meta.url)),
  '../../..',
  'tests/Honua.Mobile.Sdk.Tests/Fixtures/Scenes/scene-metadata.json',
);

const VALID_DOCUMENT = {
  schema: 'honua-scene-metadata/v1',
  id: 'demo',
  name: 'Demo',
  description: 'Demo scene',
  center: { latitude: 39.95, longitude: -75.16, height: 1500 },
  bounds: { west: -75.18, south: 39.93, east: -75.14, north: 39.97 },
  tileset: { url: 'https://example.test/tileset.json', format: '3d-tiles' },
  capabilities: { '3d-tiles': true },
  layers: [
    {
      id: 'as-built',
      title: 'As-built capture',
      kind: '3d-tiles',
      url: 'https://example.test/as-built/tileset.json',
      visible: true,
      opacity: 1,
    },
    {
      id: 'design-overlay',
      title: 'Design overlay',
      kind: '3d-tiles',
      url: 'https://example.test/design/tileset.json',
      visible: false,
      opacity: 0.85,
    },
  ],
  bookmarks: [
    {
      id: 'site-overview',
      title: 'Site overview',
      view: {
        center: { latitude: 39.95, longitude: -75.16 },
        height: 2400,
        orientation: { heading: 0, pitch: -45, roll: 0 },
      },
    },
  ],
  timeline: {
    phases: [
      {
        id: 'phase-1',
        title: 'Phase 1',
        startUtc: '2026-01-01T00:00:00Z',
        endUtc: '2026-03-31T00:00:00Z',
        visibleLayerIds: ['as-built'],
      },
    ],
  },
  compare: {
    modes: [
      {
        id: 'design-vs-asbuilt',
        title: 'Design vs As-built',
        leftLayerIds: ['design-overlay'],
        rightLayerIds: ['as-built'],
      },
    ],
  },
  inspector: {
    fields: [
      { key: 'id', title: 'Asset ID' },
      { key: 'phase', title: 'Phase', format: 'string' },
      { key: 'lastSurveyUtc', title: 'Last survey', format: 'date' },
    ],
  },
};

function expectMetadataError(
  fn: () => void,
  expectations: { path?: string; messagePattern?: RegExp },
): void {
  let caught: unknown;
  try {
    fn();
  } catch (error) {
    caught = error;
  }
  expect(caught).toBeInstanceOf(HonuaSceneMetadataError);
  const error = caught as HonuaSceneMetadataError;
  expect(error.code).toBe('metadata-invalid');
  if (expectations.path !== undefined) {
    expect(error.path).toBe(expectations.path);
  }
  if (expectations.messagePattern) {
    expect(error.message).toMatch(expectations.messagePattern);
  }
}

describe('parseHonuaSceneMetadata', () => {
  it('parses a valid construction-themed metadata document', () => {
    const metadata = parseHonuaSceneMetadata(VALID_DOCUMENT);

    expect(metadata.schema).toBe('honua-scene-metadata/v1');
    expect(metadata.id).toBe('demo');
    expect(metadata.layers).toHaveLength(2);
    expect(metadata.bookmarks).toHaveLength(1);
    expect(metadata.timeline?.phases[0].visibleLayerIds).toEqual(['as-built']);
    expect(metadata.compare?.modes[0].leftLayerIds).toEqual(['design-overlay']);
    expect(metadata.inspector?.fields[0].key).toBe('id');
  });

  it('rejects unknown schema with a stable code and path', () => {
    expectMetadataError(
      () => parseHonuaSceneMetadata({ ...VALID_DOCUMENT, schema: 'honua-scene-metadata/v999' }),
      { path: '$.schema' },
    );
  });

  it('points to the offending path on a validation failure', () => {
    expectMetadataError(
      () =>
        parseHonuaSceneMetadata({
          ...VALID_DOCUMENT,
          layers: [
            {
              id: 'as-built',
              title: 'As-built',
              kind: '3d-tiles',
              url: 'https://example.test/tileset.json',
              opacity: 5,
            },
          ],
        }),
      { path: '$.layers[0].opacity', messagePattern: /opacity/i },
    );
  });

  it('flags duplicate layer ids', () => {
    const document = {
      ...VALID_DOCUMENT,
      layers: [
        ...VALID_DOCUMENT.layers,
        { ...VALID_DOCUMENT.layers[0] },
      ],
    };

    expectMetadataError(() => parseHonuaSceneMetadata(document), { path: '$.layers[2].id' });
  });

  it('rejects timeline phases that reference unknown layers', () => {
    const document = {
      ...VALID_DOCUMENT,
      timeline: {
        phases: [
          {
            id: 'phase-1',
            title: 'Phase 1',
            startUtc: '2026-01-01T00:00:00Z',
            endUtc: '2026-03-31T00:00:00Z',
            visibleLayerIds: ['ghost-layer'],
          },
        ],
      },
    };

    expectMetadataError(() => parseHonuaSceneMetadata(document), {
      path: '$.timeline.phases[0].visibleLayerIds[0]',
    });
  });

  it('rejects compare modes that reference unknown layers', () => {
    const document = {
      ...VALID_DOCUMENT,
      compare: {
        modes: [
          {
            id: 'invalid',
            title: 'Invalid',
            leftLayerIds: ['ghost-layer'],
            rightLayerIds: [],
          },
        ],
      },
    };

    expectMetadataError(() => parseHonuaSceneMetadata(document), {
      path: '$.compare.modes[0].leftLayerIds[0]',
    });
  });

  it('accepts the implicit "primary" layer reference', () => {
    const document = {
      ...VALID_DOCUMENT,
      timeline: {
        phases: [
          {
            id: 'p',
            title: 'Phase',
            startUtc: '2026-01-01T00:00:00Z',
            endUtc: '2026-03-31T00:00:00Z',
            visibleLayerIds: ['primary'],
          },
        ],
      },
    };

    expect(() => parseHonuaSceneMetadata(document)).not.toThrow();
  });

  it('requires required string fields', () => {
    expectMetadataError(
      () => parseHonuaSceneMetadata({ schema: 'honua-scene-metadata/v1' }),
      { path: '$.id' },
    );
  });

  it('rejects non-finite numbers', () => {
    expectMetadataError(
      () =>
        parseHonuaSceneMetadata({
          ...VALID_DOCUMENT,
          center: { latitude: NaN, longitude: 10 },
        }),
      { path: '$.center.latitude' },
    );
  });

  it('parses the SDK scene-metadata fixture without a schema field', () => {
    const raw = readFileSync(SDK_FIXTURE_PATH, 'utf-8');
    const document = JSON.parse(raw) as Record<string, unknown>;

    expect(document.schema).toBeUndefined();

    const metadata = parseHonuaSceneMetadata(document);

    expect(metadata.schema).toBe('honua-scene-metadata/v1');
    expect(metadata.id).toBe('downtown-honolulu');
    expect(metadata.tileset?.url).toBe(
      'https://api.honua.test/api/scenes/downtown-honolulu/tileset.json',
    );
    expect(metadata.terrain?.url).toBe(
      'https://api.honua.test/api/scenes/downtown-honolulu/terrain',
    );
  });

  it('rejects malformed timeline timestamps', () => {
    expectMetadataError(
      () =>
        parseHonuaSceneMetadata({
          ...VALID_DOCUMENT,
          timeline: {
            phases: [
              {
                id: 'phase-1',
                title: 'Phase 1',
                startUtc: 'not-a-date',
                endUtc: '2026-03-31T00:00:00Z',
                visibleLayerIds: ['as-built'],
              },
            ],
          },
        }),
      { path: '$.timeline.phases[0].startUtc' },
    );
  });
});
