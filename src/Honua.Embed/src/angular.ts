import {
  Component,
  CUSTOM_ELEMENTS_SCHEMA,
  ElementRef,
  EventEmitter,
  Input,
  NgModule,
  Output,
  ViewChild,
  type AfterViewInit,
  type OnChanges,
  type OnDestroy,
} from '@angular/core';
import type { HonuaMapBuilderChangeDetail, HonuaMapBuilderElement } from './builder-element';
import type { HonuaMapBuilderLayerOption } from './builder';
import type {
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
  HonuaMapBounds,
  HonuaMapConfig,
  HonuaMapCoordinate,
  HonuaMapElement,
  HonuaMapIdentifyDetail,
  HonuaMapSearchDetail,
} from './map';
import type { HonuaMapEmbedBuilderTarget, HonuaMapThemeOptions } from './snippets';

@Component({
  selector: 'honua-map-wrapper',
  standalone: true,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  template: '<honua-map #element><ng-content /></honua-map>',
})
export class HonuaMapComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('element', { static: true })
  elementRef?: ElementRef<HonuaMapElement>;

  @Input() serviceUrl: string | null | undefined = undefined;
  @Input() layerIds: readonly string[] | null | undefined = undefined;
  @Input() apiKey: string | null | undefined = undefined;
  @Input() center: HonuaMapCoordinate | null | undefined = undefined;
  @Input() zoom: number | null | undefined = undefined;
  @Input() bounds: HonuaMapBounds | null | undefined = undefined;
  @Input() basemap: string | null | undefined = undefined;
  @Input() interactive: boolean | null | undefined = undefined;
  @Input() search: boolean | null | undefined = undefined;
  @Input() identify: boolean | null | undefined = undefined;
  @Input() attribution: string | null | undefined = undefined;
  @Input() theme: 'light' | 'dark' | null | undefined = undefined;
  @Input() label: string | null | undefined = undefined;
  @Input() themeStyle: HonuaMapThemeOptions | null | undefined = undefined;

  @Output() readonly ready = new EventEmitter<HonuaMapConfig>();
  @Output() readonly configChange = new EventEmitter<HonuaMapConfig>();
  @Output() readonly searchEvent = new EventEmitter<HonuaMapSearchDetail>();
  @Output() readonly identifyEvent = new EventEmitter<HonuaMapIdentifyDetail>();

  #viewReady = false;
  #disconnect = noop;

  ngAfterViewInit(): void {
    this.#viewReady = true;
    const element = this.elementRef?.nativeElement;
    if (!element) {
      return;
    }

    this.#disconnect = addHonuaMapElementEventListeners(element, {
      ready: (detail) => this.ready.emit(detail),
      configChange: (detail) => this.configChange.emit(detail),
      search: (detail) => this.searchEvent.emit(detail),
      identify: (detail) => this.identifyEvent.emit(detail),
    });
    this.#apply();
  }

  ngOnChanges(): void {
    this.#apply();
  }

  ngOnDestroy(): void {
    this.#disconnect();
  }

  #apply(): void {
    if (!this.#viewReady || !this.elementRef?.nativeElement) {
      return;
    }

    applyHonuaMapElementOptions(this.elementRef.nativeElement, this.options);
  }

  get element(): HonuaMapElement | null {
    return this.elementRef?.nativeElement ?? null;
  }

  get options(): HonuaMapWrapperOptions {
    return {
      serviceUrl: this.serviceUrl,
      layerIds: this.layerIds,
      apiKey: this.apiKey,
      center: this.center,
      zoom: this.zoom,
      bounds: this.bounds,
      basemap: this.basemap,
      interactive: this.interactive,
      search: this.search,
      identify: this.identify,
      attribution: this.attribution,
      theme: this.theme,
      label: this.label,
      themeStyle: this.themeStyle,
    };
  }
}

@Component({
  selector: 'honua-map-builder-wrapper',
  standalone: true,
  schemas: [CUSTOM_ELEMENTS_SCHEMA],
  template: '<honua-map-builder #element></honua-map-builder>',
})
export class HonuaMapBuilderComponent implements AfterViewInit, OnChanges, OnDestroy {
  @ViewChild('element', { static: true })
  elementRef?: ElementRef<HonuaMapBuilderElement>;

  @Input() serviceUrl: string | null | undefined = undefined;
  @Input() availableLayers: readonly HonuaMapBuilderLayerOption[] | null | undefined = undefined;
  @Input() selectedLayerIds: readonly string[] | null | undefined = undefined;
  @Input() layerIds: readonly string[] | null | undefined = undefined;
  @Input() apiKey: string | null | undefined = undefined;
  @Input() center: HonuaMapCoordinate | null | undefined = undefined;
  @Input() zoom: number | null | undefined = undefined;
  @Input() bounds: HonuaMapBounds | null | undefined = undefined;
  @Input() basemap: string | null | undefined = undefined;
  @Input() interactive: boolean | null | undefined = undefined;
  @Input() search: boolean | null | undefined = undefined;
  @Input() identify: boolean | null | undefined = undefined;
  @Input() attribution: string | null | undefined = undefined;
  @Input() theme: 'light' | 'dark' | null | undefined = undefined;
  @Input() label: string | null | undefined = undefined;
  @Input() themeStyle: HonuaMapThemeOptions | null | undefined = undefined;
  @Input() target: HonuaMapEmbedBuilderTarget | null | undefined = undefined;
  @Input() elementName: string | null | undefined = undefined;
  @Input() includeCredentials: boolean | null | undefined = undefined;
  @Input() scriptUrl: string | null | undefined = undefined;
  @Input() iframeUrl: string | null | undefined = undefined;
  @Input() parentOrigin: string | null | undefined = undefined;

  @Output() readonly builderChange = new EventEmitter<HonuaMapBuilderChangeDetail>();

  #viewReady = false;
  #disconnect = noop;

  ngAfterViewInit(): void {
    this.#viewReady = true;
    const element = this.elementRef?.nativeElement;
    if (!element) {
      return;
    }

    this.#disconnect = addHonuaMapBuilderElementEventListeners(element, {
      change: (detail) => this.builderChange.emit(detail),
    });
    this.#apply();
  }

  ngOnChanges(): void {
    this.#apply();
  }

  ngOnDestroy(): void {
    this.#disconnect();
  }

  #apply(): void {
    if (!this.#viewReady || !this.elementRef?.nativeElement) {
      return;
    }

    applyHonuaMapBuilderElementOptions(this.elementRef.nativeElement, this.options);
  }

  get element(): HonuaMapBuilderElement | null {
    return this.elementRef?.nativeElement ?? null;
  }

  get options(): HonuaMapBuilderWrapperOptions {
    return {
      serviceUrl: this.serviceUrl,
      availableLayers: this.availableLayers,
      selectedLayerIds: this.selectedLayerIds,
      layerIds: this.layerIds,
      apiKey: this.apiKey,
      center: this.center,
      zoom: this.zoom,
      bounds: this.bounds,
      basemap: this.basemap,
      interactive: this.interactive,
      search: this.search,
      identify: this.identify,
      attribution: this.attribution,
      theme: this.theme,
      label: this.label,
      themeStyle: this.themeStyle,
      target: this.target,
      elementName: this.elementName,
      includeCredentials: this.includeCredentials,
      scriptUrl: this.scriptUrl,
      iframeUrl: this.iframeUrl,
      parentOrigin: this.parentOrigin,
    };
  }
}

@NgModule({
  imports: [HonuaMapComponent, HonuaMapBuilderComponent],
  exports: [HonuaMapComponent, HonuaMapBuilderComponent],
})
export class HonuaEmbedModule {
}

function noop(): void {
}

export type {
  HonuaMapBuilderWrapperOptions,
  HonuaMapWrapperOptions,
} from './framework';
