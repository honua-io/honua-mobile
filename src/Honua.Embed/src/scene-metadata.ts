import type {
  HonuaSceneCoordinate,
  HonuaSceneOrientation,
} from './scene';

export const HONUA_SCENE_METADATA_SCHEMA = 'honua-scene-metadata/v1';

export type HonuaSceneLayerKind = '3d-tiles' | 'terrain' | 'imagery';

export type HonuaSceneInspectorFieldFormat =
  | 'string'
  | 'number'
  | 'date'
  | 'enum';

export interface HonuaSceneLayerMetadata {
  id: string;
  title: string;
  kind: HonuaSceneLayerKind;
  url: string;
  description?: string;
  visible?: boolean;
  opacity?: number;
}

export interface HonuaSceneViewSpec {
  center: HonuaSceneCoordinate;
  height: number;
  orientation: HonuaSceneOrientation;
}

export interface HonuaSceneBookmarkMetadata {
  id: string;
  title: string;
  description?: string;
  view: HonuaSceneViewSpec;
}

export interface HonuaSceneTimelinePhase {
  id: string;
  title: string;
  startUtc: string;
  endUtc: string;
  visibleLayerIds: string[];
  description?: string;
}

export interface HonuaSceneCompareMode {
  id: string;
  title: string;
  leftLayerIds: string[];
  rightLayerIds: string[];
  description?: string;
}

export interface HonuaSceneInspectorField {
  key: string;
  title: string;
  format?: HonuaSceneInspectorFieldFormat;
  unit?: string;
}

export interface HonuaSceneBoundsMetadata {
  west: number;
  south: number;
  east: number;
  north: number;
}

export interface HonuaSceneTilesetMetadata {
  url: string;
  format?: string;
  mediaType?: string;
  requiresAuthentication?: boolean;
}

export interface HonuaSceneTerrainMetadata {
  url: string;
  format?: string;
  requiresAuthentication?: boolean;
}

export interface HonuaSceneLinkMetadata {
  rel: string;
  href: string;
  type?: string;
  title?: string;
}

export interface HonuaSceneMetadata {
  schema: typeof HONUA_SCENE_METADATA_SCHEMA;
  id: string;
  name: string;
  description?: string;
  center?: (HonuaSceneCoordinate & { height?: number }) | null;
  bounds?: HonuaSceneBoundsMetadata | null;
  tileset?: HonuaSceneTilesetMetadata | null;
  terrain?: HonuaSceneTerrainMetadata | null;
  capabilities?: Record<string, boolean>;
  links?: HonuaSceneLinkMetadata[];
  layers?: HonuaSceneLayerMetadata[];
  bookmarks?: HonuaSceneBookmarkMetadata[];
  timeline?: { phases: HonuaSceneTimelinePhase[] } | null;
  compare?: { modes: HonuaSceneCompareMode[] } | null;
  inspector?: { fields: HonuaSceneInspectorField[] } | null;
}

export type HonuaSceneMetadataErrorCode = 'metadata-invalid';

export class HonuaSceneMetadataError extends Error {
  readonly code: HonuaSceneMetadataErrorCode = 'metadata-invalid';
  readonly path: string;

  constructor(message: string, path: string) {
    super(message);
    this.name = 'HonuaSceneMetadataError';
    this.path = path;
  }
}

export function parseHonuaSceneMetadata(input: unknown): HonuaSceneMetadata {
  if (!isObject(input)) {
    throw new HonuaSceneMetadataError(
      'Scene metadata must be a JSON object.',
      '$',
    );
  }

  const schema = readString(input, 'schema', '$.schema');
  if (schema !== undefined && schema !== HONUA_SCENE_METADATA_SCHEMA) {
    throw new HonuaSceneMetadataError(
      `Unsupported scene metadata schema: ${schema}. Expected ${HONUA_SCENE_METADATA_SCHEMA}.`,
      '$.schema',
    );
  }

  const id = readString(input, 'id', '$.id', { required: true });
  const name = readString(input, 'name', '$.name', { required: true });
  const description = readString(input, 'description', '$.description');
  const center = parseCenter(input.center, '$.center');
  const bounds = parseBounds(input.bounds, '$.bounds');
  const tileset = parseTileset(input.tileset, '$.tileset');
  const terrain = parseTerrain(input.terrain, '$.terrain');
  const capabilities = parseCapabilities(input.capabilities, '$.capabilities');
  const links = parseLinks(input.links, '$.links');
  const layers = parseLayers(input.layers, '$.layers');
  const bookmarks = parseBookmarks(input.bookmarks, '$.bookmarks');
  const timeline = parseTimeline(input.timeline, '$.timeline');
  const compare = parseCompare(input.compare, '$.compare');
  const inspector = parseInspector(input.inspector, '$.inspector');

  validateLayerReferences({ layers, timeline, compare });

  return {
    schema: HONUA_SCENE_METADATA_SCHEMA,
    id,
    name,
    ...(description !== undefined && { description }),
    ...(center !== undefined && { center }),
    ...(bounds !== undefined && { bounds }),
    ...(tileset !== undefined && { tileset }),
    ...(terrain !== undefined && { terrain }),
    ...(capabilities !== undefined && { capabilities }),
    ...(links !== undefined && { links }),
    ...(layers !== undefined && { layers }),
    ...(bookmarks !== undefined && { bookmarks }),
    ...(timeline !== undefined && { timeline }),
    ...(compare !== undefined && { compare }),
    ...(inspector !== undefined && { inspector }),
  };
}

function parseCenter(
  value: unknown,
  path: string,
): (HonuaSceneCoordinate & { height?: number }) | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('Center must be an object.', path);
  }

  const latitude = readNumber(value, 'latitude', `${path}.latitude`, { required: true });
  const longitude = readNumber(value, 'longitude', `${path}.longitude`, { required: true });
  const height = readNumber(value, 'height', `${path}.height`);

  return {
    latitude,
    longitude,
    ...(height !== undefined && { height }),
  };
}

function parseBounds(
  value: unknown,
  path: string,
): HonuaSceneBoundsMetadata | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('Bounds must be an object.', path);
  }

  return {
    west: readNumber(value, 'west', `${path}.west`, { required: true }),
    south: readNumber(value, 'south', `${path}.south`, { required: true }),
    east: readNumber(value, 'east', `${path}.east`, { required: true }),
    north: readNumber(value, 'north', `${path}.north`, { required: true }),
  };
}

function parseTileset(
  value: unknown,
  path: string,
): HonuaSceneTilesetMetadata | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('Tileset must be an object.', path);
  }

  return {
    url: readString(value, 'url', `${path}.url`, { required: true }),
    ...maybeAdd('format', readString(value, 'format', `${path}.format`)),
    ...maybeAdd('mediaType', readString(value, 'mediaType', `${path}.mediaType`)),
    ...maybeAdd('requiresAuthentication', readBoolean(value, 'requiresAuthentication', `${path}.requiresAuthentication`)),
  };
}

function parseTerrain(
  value: unknown,
  path: string,
): HonuaSceneTerrainMetadata | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('Terrain must be an object.', path);
  }

  return {
    url: readString(value, 'url', `${path}.url`, { required: true }),
    ...maybeAdd('format', readString(value, 'format', `${path}.format`)),
    ...maybeAdd('requiresAuthentication', readBoolean(value, 'requiresAuthentication', `${path}.requiresAuthentication`)),
  };
}

function parseCapabilities(
  value: unknown,
  path: string,
): Record<string, boolean> | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('Capabilities must be an object.', path);
  }

  const result: Record<string, boolean> = {};
  for (const [key, capability] of Object.entries(value)) {
    if (typeof capability !== 'boolean') {
      throw new HonuaSceneMetadataError(
        `Capability values must be booleans.`,
        `${path}.${key}`,
      );
    }
    result[key] = capability;
  }

  return result;
}

function parseLinks(value: unknown, path: string): HonuaSceneLinkMetadata[] | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!Array.isArray(value)) {
    throw new HonuaSceneMetadataError('Links must be an array.', path);
  }

  return value.map((entry, index) => {
    const itemPath = `${path}[${index}]`;
    if (!isObject(entry)) {
      throw new HonuaSceneMetadataError('Link entries must be objects.', itemPath);
    }

    return {
      rel: readString(entry, 'rel', `${itemPath}.rel`, { required: true }),
      href: readString(entry, 'href', `${itemPath}.href`, { required: true }),
      ...maybeAdd('type', readString(entry, 'type', `${itemPath}.type`)),
      ...maybeAdd('title', readString(entry, 'title', `${itemPath}.title`)),
    };
  });
}

function parseLayers(
  value: unknown,
  path: string,
): HonuaSceneLayerMetadata[] | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!Array.isArray(value)) {
    throw new HonuaSceneMetadataError('Layers must be an array.', path);
  }

  const seenIds = new Set<string>();
  return value.map((entry, index) => {
    const itemPath = `${path}[${index}]`;
    if (!isObject(entry)) {
      throw new HonuaSceneMetadataError('Layer entries must be objects.', itemPath);
    }

    const id = readString(entry, 'id', `${itemPath}.id`, { required: true });
    if (seenIds.has(id)) {
      throw new HonuaSceneMetadataError(
        `Duplicate layer id "${id}".`,
        `${itemPath}.id`,
      );
    }
    seenIds.add(id);

    const kind = readString(entry, 'kind', `${itemPath}.kind`, { required: true });
    if (kind !== '3d-tiles' && kind !== 'terrain' && kind !== 'imagery') {
      throw new HonuaSceneMetadataError(
        `Unsupported layer kind "${kind}". Expected "3d-tiles", "terrain", or "imagery".`,
        `${itemPath}.kind`,
      );
    }

    const opacity = readNumber(entry, 'opacity', `${itemPath}.opacity`);
    if (opacity !== undefined && (opacity < 0 || opacity > 1)) {
      throw new HonuaSceneMetadataError(
        `Layer opacity must be between 0 and 1.`,
        `${itemPath}.opacity`,
      );
    }

    return {
      id,
      title: readString(entry, 'title', `${itemPath}.title`, { required: true }),
      kind: kind as HonuaSceneLayerKind,
      url: readString(entry, 'url', `${itemPath}.url`, { required: true }),
      ...maybeAdd('description', readString(entry, 'description', `${itemPath}.description`)),
      ...maybeAdd('visible', readBoolean(entry, 'visible', `${itemPath}.visible`)),
      ...maybeAdd('opacity', opacity),
    };
  });
}

function parseBookmarks(
  value: unknown,
  path: string,
): HonuaSceneBookmarkMetadata[] | undefined {
  if (value === undefined || value === null) {
    return undefined;
  }

  if (!Array.isArray(value)) {
    throw new HonuaSceneMetadataError('Bookmarks must be an array.', path);
  }

  const seenIds = new Set<string>();
  return value.map((entry, index) => {
    const itemPath = `${path}[${index}]`;
    if (!isObject(entry)) {
      throw new HonuaSceneMetadataError('Bookmark entries must be objects.', itemPath);
    }

    const id = readString(entry, 'id', `${itemPath}.id`, { required: true });
    if (seenIds.has(id)) {
      throw new HonuaSceneMetadataError(
        `Duplicate bookmark id "${id}".`,
        `${itemPath}.id`,
      );
    }
    seenIds.add(id);

    return {
      id,
      title: readString(entry, 'title', `${itemPath}.title`, { required: true }),
      ...maybeAdd('description', readString(entry, 'description', `${itemPath}.description`)),
      view: parseView(entry.view, `${itemPath}.view`),
    };
  });
}

function parseView(value: unknown, path: string): HonuaSceneViewSpec {
  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('View must be an object.', path);
  }

  if (!isObject(value.center)) {
    throw new HonuaSceneMetadataError('View center must be an object.', `${path}.center`);
  }

  return {
    center: {
      latitude: readNumber(value.center, 'latitude', `${path}.center.latitude`, { required: true }),
      longitude: readNumber(value.center, 'longitude', `${path}.center.longitude`, { required: true }),
    },
    height: readNumber(value, 'height', `${path}.height`, { required: true }),
    orientation: parseOrientation(value.orientation, `${path}.orientation`),
  };
}

function parseOrientation(value: unknown, path: string): HonuaSceneOrientation {
  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('Orientation must be an object.', path);
  }

  return {
    heading: readNumber(value, 'heading', `${path}.heading`, { required: true }),
    pitch: readNumber(value, 'pitch', `${path}.pitch`, { required: true }),
    roll: readNumber(value, 'roll', `${path}.roll`, { required: true }),
  };
}

function parseTimeline(
  value: unknown,
  path: string,
): { phases: HonuaSceneTimelinePhase[] } | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('Timeline must be an object.', path);
  }

  if (!Array.isArray(value.phases)) {
    throw new HonuaSceneMetadataError('Timeline phases must be an array.', `${path}.phases`);
  }

  const seenIds = new Set<string>();
  const phases = value.phases.map((entry: unknown, index: number) => {
    const itemPath = `${path}.phases[${index}]`;
    if (!isObject(entry)) {
      throw new HonuaSceneMetadataError('Timeline phase entries must be objects.', itemPath);
    }

    const id = readString(entry, 'id', `${itemPath}.id`, { required: true });
    if (seenIds.has(id)) {
      throw new HonuaSceneMetadataError(
        `Duplicate timeline phase id "${id}".`,
        `${itemPath}.id`,
      );
    }
    seenIds.add(id);

    const startUtc = readString(entry, 'startUtc', `${itemPath}.startUtc`, { required: true });
    const endUtc = readString(entry, 'endUtc', `${itemPath}.endUtc`, { required: true });
    requireFiniteIso(startUtc, `${itemPath}.startUtc`);
    requireFiniteIso(endUtc, `${itemPath}.endUtc`);

    return {
      id,
      title: readString(entry, 'title', `${itemPath}.title`, { required: true }),
      startUtc,
      endUtc,
      visibleLayerIds: readStringArray(entry, 'visibleLayerIds', `${itemPath}.visibleLayerIds`),
      ...maybeAdd('description', readString(entry, 'description', `${itemPath}.description`)),
    };
  });

  return { phases };
}

function parseCompare(
  value: unknown,
  path: string,
): { modes: HonuaSceneCompareMode[] } | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('Compare must be an object.', path);
  }

  if (!Array.isArray(value.modes)) {
    throw new HonuaSceneMetadataError('Compare modes must be an array.', `${path}.modes`);
  }

  const seenIds = new Set<string>();
  const modes = value.modes.map((entry: unknown, index: number) => {
    const itemPath = `${path}.modes[${index}]`;
    if (!isObject(entry)) {
      throw new HonuaSceneMetadataError('Compare mode entries must be objects.', itemPath);
    }

    const id = readString(entry, 'id', `${itemPath}.id`, { required: true });
    if (seenIds.has(id)) {
      throw new HonuaSceneMetadataError(
        `Duplicate compare mode id "${id}".`,
        `${itemPath}.id`,
      );
    }
    seenIds.add(id);

    return {
      id,
      title: readString(entry, 'title', `${itemPath}.title`, { required: true }),
      leftLayerIds: readStringArray(entry, 'leftLayerIds', `${itemPath}.leftLayerIds`),
      rightLayerIds: readStringArray(entry, 'rightLayerIds', `${itemPath}.rightLayerIds`),
      ...maybeAdd('description', readString(entry, 'description', `${itemPath}.description`)),
    };
  });

  return { modes };
}

function parseInspector(
  value: unknown,
  path: string,
): { fields: HonuaSceneInspectorField[] } | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  if (!isObject(value)) {
    throw new HonuaSceneMetadataError('Inspector must be an object.', path);
  }

  if (!Array.isArray(value.fields)) {
    throw new HonuaSceneMetadataError('Inspector fields must be an array.', `${path}.fields`);
  }

  const fields = value.fields.map((entry: unknown, index: number) => {
    const itemPath = `${path}.fields[${index}]`;
    if (!isObject(entry)) {
      throw new HonuaSceneMetadataError('Inspector field entries must be objects.', itemPath);
    }

    const format = readString(entry, 'format', `${itemPath}.format`);
    if (
      format !== undefined &&
      format !== 'string' &&
      format !== 'number' &&
      format !== 'date' &&
      format !== 'enum'
    ) {
      throw new HonuaSceneMetadataError(
        `Unsupported inspector field format "${format}".`,
        `${itemPath}.format`,
      );
    }

    return {
      key: readString(entry, 'key', `${itemPath}.key`, { required: true }),
      title: readString(entry, 'title', `${itemPath}.title`, { required: true }),
      ...maybeAdd('format', format as HonuaSceneInspectorFieldFormat | undefined),
      ...maybeAdd('unit', readString(entry, 'unit', `${itemPath}.unit`)),
    };
  });

  return { fields };
}

function validateLayerReferences(args: {
  layers: HonuaSceneLayerMetadata[] | undefined;
  timeline: { phases: HonuaSceneTimelinePhase[] } | null | undefined;
  compare: { modes: HonuaSceneCompareMode[] } | null | undefined;
}): void {
  const known = new Set<string>(['primary', ...(args.layers?.map((layer) => layer.id) ?? [])]);
  if (args.timeline) {
    args.timeline.phases.forEach((phase, index) => {
      phase.visibleLayerIds.forEach((layerId, layerIndex) => {
        if (!known.has(layerId)) {
          throw new HonuaSceneMetadataError(
            `Timeline phase references unknown layer "${layerId}".`,
            `$.timeline.phases[${index}].visibleLayerIds[${layerIndex}]`,
          );
        }
      });
    });
  }

  if (args.compare) {
    args.compare.modes.forEach((mode, index) => {
      mode.leftLayerIds.forEach((layerId, layerIndex) => {
        if (!known.has(layerId)) {
          throw new HonuaSceneMetadataError(
            `Compare mode references unknown left layer "${layerId}".`,
            `$.compare.modes[${index}].leftLayerIds[${layerIndex}]`,
          );
        }
      });
      mode.rightLayerIds.forEach((layerId, layerIndex) => {
        if (!known.has(layerId)) {
          throw new HonuaSceneMetadataError(
            `Compare mode references unknown right layer "${layerId}".`,
            `$.compare.modes[${index}].rightLayerIds[${layerIndex}]`,
          );
        }
      });
    });
  }
}

interface ReadOptions {
  required?: boolean;
}

interface RequiredReadOptions extends ReadOptions {
  required: true;
}

function readString(
  source: Record<string, unknown>,
  key: string,
  path: string,
  options: RequiredReadOptions,
): string;
function readString(
  source: Record<string, unknown>,
  key: string,
  path: string,
  options?: ReadOptions,
): string | undefined;
function readString(
  source: Record<string, unknown>,
  key: string,
  path: string,
  options: ReadOptions = {},
): string | undefined {
  const value = source[key];
  if (value === undefined || value === null) {
    if (options.required) {
      throw new HonuaSceneMetadataError(`Missing required string at ${path}.`, path);
    }
    return undefined;
  }

  if (typeof value !== 'string') {
    throw new HonuaSceneMetadataError(`Expected string at ${path}.`, path);
  }

  return value;
}

function readNumber(
  source: Record<string, unknown>,
  key: string,
  path: string,
  options: RequiredReadOptions,
): number;
function readNumber(
  source: Record<string, unknown>,
  key: string,
  path: string,
  options?: ReadOptions,
): number | undefined;
function readNumber(
  source: Record<string, unknown>,
  key: string,
  path: string,
  options: ReadOptions = {},
): number | undefined {
  const value = source[key];
  if (value === undefined || value === null) {
    if (options.required) {
      throw new HonuaSceneMetadataError(`Missing required number at ${path}.`, path);
    }
    return undefined;
  }

  if (typeof value !== 'number' || !Number.isFinite(value)) {
    throw new HonuaSceneMetadataError(`Expected finite number at ${path}.`, path);
  }

  return value;
}

function readBoolean(
  source: Record<string, unknown>,
  key: string,
  path: string,
): boolean | undefined {
  const value = source[key];
  if (value === undefined || value === null) {
    return undefined;
  }

  if (typeof value !== 'boolean') {
    throw new HonuaSceneMetadataError(`Expected boolean at ${path}.`, path);
  }

  return value;
}

function readStringArray(
  source: Record<string, unknown>,
  key: string,
  path: string,
): string[] {
  const value = source[key];
  if (value === undefined || value === null) {
    return [];
  }

  if (!Array.isArray(value)) {
    throw new HonuaSceneMetadataError(`Expected an array at ${path}.`, path);
  }

  return value.map((entry, index) => {
    if (typeof entry !== 'string') {
      throw new HonuaSceneMetadataError(`Expected string at ${path}[${index}].`, `${path}[${index}]`);
    }
    return entry;
  });
}

function requireFiniteIso(value: string, path: string): void {
  const parsed = Date.parse(value);
  if (!Number.isFinite(parsed)) {
    throw new HonuaSceneMetadataError(
      `Expected an ISO-8601 timestamp at ${path}.`,
      path,
    );
  }
}

function isObject(value: unknown): value is Record<string, unknown> {
  return typeof value === 'object' && value !== null && !Array.isArray(value);
}

function maybeAdd<K extends string, V>(key: K, value: V | undefined): { [P in K]?: V } {
  return value === undefined ? ({} as { [P in K]?: V }) : ({ [key]: value } as { [P in K]?: V });
}
