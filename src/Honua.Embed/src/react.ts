"use client";

import {
  createElement,
  forwardRef,
  useEffect,
  useRef,
  type CSSProperties,
  type ForwardedRef,
  type HTMLAttributes,
  type ReactNode,
} from 'react';
import type { HonuaMapBuilderChangeDetail, HonuaMapBuilderElement } from './builder-element';
import type {
  HonuaElementEventHandler,
  HonuaMapBuilderWrapperOptions,
  HonuaMapWrapperOptions,
} from './framework';
import {
  addHonuaMapBuilderElementEventListeners,
  addHonuaMapElementEventListeners,
  applyHonuaMapBuilderElementOptions,
  applyHonuaMapElementOptions,
} from './framework';
import type {
  HonuaMapConfig,
  HonuaMapElement,
  HonuaMapIdentifyDetail,
  HonuaMapSearchDetail,
} from './map';

type HonuaNativeElementProps = Omit<
  HTMLAttributes<HTMLElement>,
  'children' | 'onChange' | 'onSearch' | 'style'
>;

export interface HonuaMapProps extends HonuaMapWrapperOptions, HonuaNativeElementProps {
  children?: ReactNode;
  style?: CSSProperties;
  onReady?: HonuaElementEventHandler<HonuaMapConfig>;
  onConfigChange?: HonuaElementEventHandler<HonuaMapConfig>;
  onSearch?: HonuaElementEventHandler<HonuaMapSearchDetail>;
  onIdentify?: HonuaElementEventHandler<HonuaMapIdentifyDetail>;
}

export interface HonuaMapBuilderProps extends HonuaMapBuilderWrapperOptions, HonuaNativeElementProps {
  style?: CSSProperties;
  onChange?: HonuaElementEventHandler<HonuaMapBuilderChangeDetail>;
}

export const HonuaMap = forwardRef<HonuaMapElement, HonuaMapProps>(function HonuaMap(
  props,
  forwardedRef,
) {
  const {
    serviceUrl,
    layerIds,
    apiKey,
    center,
    zoom,
    bounds,
    basemap,
    interactive,
    search,
    identify,
    attribution,
    theme,
    label,
    themeStyle,
    onReady,
    onConfigChange,
    onSearch,
    onIdentify,
    children,
    ...nativeProps
  } = props;
  const elementRef = useRef<HonuaMapElement | null>(null);
  const readyObservedRef = useRef(false);

  useEffect(() => {
    const element = elementRef.current;
    if (!element) {
      return undefined;
    }

    return addHonuaMapElementEventListeners(element, {
      ready: onReady ? (detail, event) => {
        readyObservedRef.current = true;
        onReady(detail, event);
      } : undefined,
      configChange: onConfigChange,
      search: onSearch,
      identify: onIdentify,
    });
  });

  useEffect(() => {
    const element = elementRef.current;
    if (!element) {
      return;
    }

    applyHonuaMapElementOptions(element, {
      serviceUrl,
      layerIds,
      apiKey,
      center,
      zoom,
      bounds,
      basemap,
      interactive,
      search,
      identify,
      attribution,
      theme,
      label,
      themeStyle,
    });
    redispatchMissedMapReady(element, readyObservedRef, onReady !== undefined);
  });

  return createElement('honua-map', {
    ...nativeProps,
    ref: assignElementRef(elementRef, forwardedRef),
  }, children);
});

export const HonuaMapBuilder = forwardRef<HonuaMapBuilderElement, HonuaMapBuilderProps>(
  function HonuaMapBuilder(props, forwardedRef) {
    const {
      serviceUrl,
      availableLayers,
      selectedLayerIds,
      layerIds,
      apiKey,
      center,
      zoom,
      bounds,
      basemap,
      interactive,
      search,
      identify,
      attribution,
      theme,
      label,
      themeStyle,
      target,
      elementName,
      includeCredentials,
      scriptUrl,
      iframeUrl,
      parentOrigin,
      onChange,
      ...nativeProps
    } = props;
    const elementRef = useRef<HonuaMapBuilderElement | null>(null);

    useEffect(() => {
      const element = elementRef.current;
      if (!element) {
        return undefined;
      }

      return addHonuaMapBuilderElementEventListeners(element, {
        change: onChange,
      });
    });

    useEffect(() => {
      const element = elementRef.current;
      if (!element) {
        return;
      }

      applyHonuaMapBuilderElementOptions(element, {
        serviceUrl,
        availableLayers,
        selectedLayerIds,
        layerIds,
        apiKey,
        center,
        zoom,
        bounds,
        basemap,
        interactive,
        search,
        identify,
        attribution,
        theme,
        label,
        themeStyle,
        target,
        elementName,
        includeCredentials,
        scriptUrl,
        iframeUrl,
        parentOrigin,
      });
    });

    return createElement('honua-map-builder', {
      ...nativeProps,
      ref: assignElementRef(elementRef, forwardedRef),
    });
  },
);

function assignElementRef<TElement>(
  localRef: { current: TElement | null },
  forwardedRef: ForwardedRef<TElement>,
): (element: TElement | null) => void {
  return (element) => {
    localRef.current = element;

    if (typeof forwardedRef === 'function') {
      forwardedRef(element);
      return;
    }

    if (forwardedRef) {
      forwardedRef.current = element;
    }
  };
}

function redispatchMissedMapReady(
  element: HonuaMapElement,
  readyObservedRef: { current: boolean },
  hasReadyHandler: boolean,
): void {
  if (!hasReadyHandler || readyObservedRef.current) {
    return;
  }

  readyObservedRef.current = true;
  element.dispatchEvent(new CustomEvent<HonuaMapConfig>('honua-map-ready', {
    bubbles: true,
    composed: true,
    detail: element.config,
  }));
}

export type {
  HonuaElementEventHandler,
  HonuaMapBuilderWrapperOptions,
  HonuaMapWrapperOptions,
} from './framework';
