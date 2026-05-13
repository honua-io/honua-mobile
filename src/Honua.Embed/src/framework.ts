import {
  type HonuaMapBuilderChangeDetail,
  HonuaMapBuilderElement,
  defineHonuaMapBuilderElement,
} from './builder-element';
import type { HonuaMapBuilderInput } from './builder';
import { canDefineHonuaCustomElements } from './dom';
import {
  type HonuaMapConfig,
  type HonuaMapElement,
  type HonuaMapIdentifyDetail,
  type HonuaMapSearchDetail,
  defineHonuaMapElement,
} from './map';
import {
  applyHonuaMapOptions,
  type HonuaMapEmbedBuilderTarget,
  type HonuaMapEmbedOptions,
  type HonuaMapThemeOptions,
} from './snippets';

export type HonuaElementEventHandler<TDetail> = (
  detail: TDetail,
  event: CustomEvent<TDetail>,
) => void;

export interface HonuaFrameworkElementNames {
  map?: string;
  mapBuilder?: string;
}

export interface HonuaMapWrapperOptions extends Omit<HonuaMapEmbedOptions, 'style'> {
  themeStyle?: HonuaMapThemeOptions | null;
}

export interface HonuaMapBuilderWrapperOptions extends Omit<HonuaMapBuilderInput, 'style'> {
  themeStyle?: HonuaMapThemeOptions | null;
  target?: HonuaMapEmbedBuilderTarget | null;
  elementName?: string | null;
  includeCredentials?: boolean | null;
  scriptUrl?: string | null;
  iframeUrl?: string | null;
  parentOrigin?: string | null;
}

export interface HonuaMapElementEventHandlers {
  ready?: HonuaElementEventHandler<HonuaMapConfig>;
  configChange?: HonuaElementEventHandler<HonuaMapConfig>;
  search?: HonuaElementEventHandler<HonuaMapSearchDetail>;
  identify?: HonuaElementEventHandler<HonuaMapIdentifyDetail>;
}

export interface HonuaMapBuilderElementEventHandlers {
  change?: HonuaElementEventHandler<HonuaMapBuilderChangeDetail>;
}

export function defineHonuaFrameworkElements(names: HonuaFrameworkElementNames = {}): void {
  if (!canDefineHonuaCustomElements()) {
    return;
  }

  defineHonuaMapElement(names.map);
  defineHonuaMapBuilderElement(names.mapBuilder);
}

export function applyHonuaMapElementOptions(
  element: HTMLElement,
  options: HonuaMapWrapperOptions,
): void {
  defineHonuaFrameworkElements();
  applyHonuaMapOptions(element, toDeclarativeMapOptions(options));
}

export function applyHonuaMapBuilderElementOptions(
  element: HonuaMapBuilderElement,
  options: HonuaMapBuilderWrapperOptions,
): void {
  defineHonuaFrameworkElements();
  const {
    target,
    elementName,
    includeCredentials,
    scriptUrl,
    iframeUrl,
    parentOrigin,
    themeStyle,
    ...builderInput
  } = options;

  setOptionalAttribute(element, 'target', target);
  setOptionalAttribute(element, 'element-name', elementName);
  setBooleanAttribute(element, 'include-credentials', includeCredentials);
  setOptionalAttribute(element, 'script-url', scriptUrl);
  setOptionalAttribute(element, 'iframe-url', iframeUrl);
  setOptionalAttribute(element, 'parent-origin', parentOrigin);
  element.value = {
    ...builderInput,
    style: themeStyle,
  };
}

export function addHonuaMapElementEventListeners(
  element: HonuaMapElement,
  handlers: HonuaMapElementEventHandlers,
): () => void {
  return combineCleanups([
    addCustomEventListener(element, 'honua-map-ready', handlers.ready),
    addCustomEventListener(element, 'honua-map-config-change', handlers.configChange),
    addCustomEventListener(element, 'honua-map-search', handlers.search),
    addCustomEventListener(element, 'honua-map-identify', handlers.identify),
  ]);
}

export function addHonuaMapBuilderElementEventListeners(
  element: HonuaMapBuilderElement,
  handlers: HonuaMapBuilderElementEventHandlers,
): () => void {
  return addCustomEventListener(element, 'honua-map-builder-change', handlers.change);
}

function addCustomEventListener<TDetail>(
  element: HTMLElement,
  eventName: string,
  handler: HonuaElementEventHandler<TDetail> | null | undefined,
): () => void {
  if (!handler) {
    return noop;
  }

  const listener: EventListener = (event) => {
    handler((event as CustomEvent<TDetail>).detail, event as CustomEvent<TDetail>);
  };
  element.addEventListener(eventName, listener);
  return () => element.removeEventListener(eventName, listener);
}

function combineCleanups(cleanups: ReadonlyArray<() => void>): () => void {
  return () => {
    for (const cleanup of cleanups) {
      cleanup();
    }
  };
}

function noop(): void {
}

function setOptionalAttribute(
  element: HTMLElement,
  name: string,
  value: string | null | undefined,
): void {
  if (value === null || value === undefined || value === '') {
    element.removeAttribute(name);
    return;
  }

  element.setAttribute(name, value);
}

function setBooleanAttribute(
  element: HTMLElement,
  name: string,
  value: boolean | null | undefined,
): void {
  if (value === true) {
    element.setAttribute(name, '');
    return;
  }

  if (value === false || value === null || value === undefined) {
    element.removeAttribute(name);
  }
}

function toDeclarativeMapOptions(options: HonuaMapWrapperOptions): HonuaMapEmbedOptions {
  return {
    serviceUrl: optionalToNull(options.serviceUrl),
    layerIds: optionalToNull(options.layerIds),
    apiKey: optionalToNull(options.apiKey),
    center: optionalToNull(options.center),
    zoom: optionalToNull(options.zoom),
    bounds: optionalToNull(options.bounds),
    basemap: optionalToNull(options.basemap),
    interactive: optionalToNull(options.interactive),
    search: optionalToNull(options.search),
    identify: optionalToNull(options.identify),
    attribution: optionalToNull(options.attribution),
    theme: optionalToNull(options.theme),
    label: optionalToNull(options.label),
    style: optionalToNull(options.themeStyle),
  };
}

function optionalToNull<TValue>(value: TValue | null | undefined): TValue | null {
  return value === undefined ? null : value;
}
