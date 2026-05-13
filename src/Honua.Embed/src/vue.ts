import {
  defineComponent,
  h,
  onBeforeUnmount,
  onMounted,
  ref,
  watch,
  type PropType,
} from 'vue';
import type { HonuaMapBuilderChangeDetail, HonuaMapBuilderElement } from './builder-element';
import type { HonuaMapBuilderWrapperOptions, HonuaMapWrapperOptions } from './framework';
import {
  addHonuaMapBuilderElementEventListeners,
  addHonuaMapElementEventListeners,
  applyHonuaMapBuilderElementOptions,
  applyHonuaMapElementOptions,
} from './framework';
import type {
  HonuaMapBounds,
  HonuaMapConfig,
  HonuaMapCoordinate,
  HonuaMapElement,
  HonuaMapIdentifyDetail,
  HonuaMapSearchDetail,
} from './map';
import type { HonuaMapBuilderLayerOption } from './builder';
import type { HonuaMapEmbedBuilderTarget, HonuaMapThemeOptions } from './snippets';

export type HonuaVueMapReadyHandler = (
  detail: HonuaMapConfig,
  event: CustomEvent<HonuaMapConfig>,
) => void;
export type HonuaVueMapConfigChangeHandler = HonuaVueMapReadyHandler;
export type HonuaVueMapSearchHandler = (
  detail: HonuaMapSearchDetail,
  event: CustomEvent<HonuaMapSearchDetail>,
) => void;
export type HonuaVueMapIdentifyHandler = (
  detail: HonuaMapIdentifyDetail,
  event: CustomEvent<HonuaMapIdentifyDetail>,
) => void;
export type HonuaVueMapBuilderChangeHandler = (
  detail: HonuaMapBuilderChangeDetail,
  event: CustomEvent<HonuaMapBuilderChangeDetail>,
) => void;

interface HonuaVueMapProps extends HonuaMapWrapperOptions {
}

interface HonuaVueMapBuilderProps extends HonuaMapBuilderWrapperOptions {
}

const mapProps = {
  serviceUrl: { type: String as PropType<string | null>, default: undefined },
  layerIds: { type: Array as PropType<readonly string[] | null>, default: undefined },
  apiKey: { type: String as PropType<string | null>, default: undefined },
  center: { type: Object as PropType<HonuaMapCoordinate | null>, default: undefined },
  zoom: { type: Number as PropType<number | null>, default: undefined },
  bounds: { type: Object as PropType<HonuaMapBounds | null>, default: undefined },
  basemap: { type: String as PropType<string | null>, default: undefined },
  interactive: { type: Boolean as PropType<boolean | null>, default: undefined },
  search: { type: Boolean as PropType<boolean | null>, default: undefined },
  identify: { type: Boolean as PropType<boolean | null>, default: undefined },
  attribution: { type: String as PropType<string | null>, default: undefined },
  theme: { type: String as PropType<'light' | 'dark' | null>, default: undefined },
  label: { type: String as PropType<string | null>, default: undefined },
  themeStyle: { type: Object as PropType<HonuaMapThemeOptions | null>, default: undefined },
};

const builderProps = {
  ...mapProps,
  availableLayers: {
    type: Array as PropType<readonly HonuaMapBuilderLayerOption[] | null>,
    default: undefined,
  },
  selectedLayerIds: { type: Array as PropType<readonly string[] | null>, default: undefined },
  target: { type: String as PropType<HonuaMapEmbedBuilderTarget | null>, default: undefined },
  elementName: { type: String as PropType<string | null>, default: undefined },
  includeCredentials: { type: Boolean as PropType<boolean | null>, default: undefined },
  scriptUrl: { type: String as PropType<string | null>, default: undefined },
  iframeUrl: { type: String as PropType<string | null>, default: undefined },
  parentOrigin: { type: String as PropType<string | null>, default: undefined },
};

export const HonuaMap = defineComponent({
  name: 'HonuaMap',
  inheritAttrs: false,
  props: mapProps,
  emits: ['ready', 'config-change', 'search', 'identify'],
  setup(props, { attrs, emit, expose, slots }) {
    const element = ref<HonuaMapElement | null>(null);
    let disconnect = noop;

    const apply = () => {
      if (element.value) {
        applyHonuaMapElementOptions(element.value, mapOptionsFromProps(props));
      }
    };

    onMounted(() => {
      if (!element.value) {
        return;
      }

      disconnect = addHonuaMapElementEventListeners(element.value, {
        ready: (detail, event) => emit('ready', detail, event),
        configChange: (detail, event) => emit('config-change', detail, event),
        search: (detail, event) => emit('search', detail, event),
        identify: (detail, event) => emit('identify', detail, event),
      });
      apply();
    });
    onBeforeUnmount(() => disconnect());
    watch(props, apply, { deep: true });
    expose({ element });

    return () => h('honua-map', {
      ...attrs,
      ref: element,
    }, slots.default?.());
  },
});

export const HonuaMapBuilder = defineComponent({
  name: 'HonuaMapBuilder',
  inheritAttrs: false,
  props: builderProps,
  emits: ['change'],
  setup(props, { attrs, emit, expose }) {
    const element = ref<HonuaMapBuilderElement | null>(null);
    let disconnect = noop;

    const apply = () => {
      if (element.value) {
        applyHonuaMapBuilderElementOptions(element.value, builderOptionsFromProps(props));
      }
    };

    onMounted(() => {
      if (!element.value) {
        return;
      }

      disconnect = addHonuaMapBuilderElementEventListeners(element.value, {
        change: (detail, event) => emit('change', detail, event),
      });
      apply();
    });
    onBeforeUnmount(() => disconnect());
    watch(props, apply, { deep: true });
    expose({ element });

    return () => h('honua-map-builder', {
      ...attrs,
      ref: element,
    });
  },
});

function mapOptionsFromProps(props: Readonly<HonuaVueMapProps>): HonuaMapWrapperOptions {
  return {
    serviceUrl: props.serviceUrl,
    layerIds: props.layerIds,
    apiKey: props.apiKey,
    center: props.center,
    zoom: props.zoom,
    bounds: props.bounds,
    basemap: props.basemap,
    interactive: props.interactive,
    search: props.search,
    identify: props.identify,
    attribution: props.attribution,
    theme: props.theme,
    label: props.label,
    themeStyle: props.themeStyle,
  };
}

function builderOptionsFromProps(props: Readonly<HonuaVueMapBuilderProps>): HonuaMapBuilderWrapperOptions {
  return {
    ...mapOptionsFromProps(props),
    availableLayers: props.availableLayers,
    selectedLayerIds: props.selectedLayerIds,
    target: props.target,
    elementName: props.elementName,
    includeCredentials: props.includeCredentials,
    scriptUrl: props.scriptUrl,
    iframeUrl: props.iframeUrl,
    parentOrigin: props.parentOrigin,
  };
}

function noop(): void {
}

export type {
  HonuaMapBuilderWrapperOptions,
  HonuaMapWrapperOptions,
} from './framework';
