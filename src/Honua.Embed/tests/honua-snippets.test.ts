import { beforeEach, describe, expect, it } from 'vitest';
import {
  applyHonuaMapOptions,
  createHonuaMapAngularIframeSnippet,
  createHonuaMapAngularSnippet,
  createHonuaMapCdnSnippet,
  createHonuaMapIframeSnippet,
  createHonuaMapReactIframeSnippet,
  createHonuaMapReactSnippet,
  createHonuaMapSnippet,
  createHonuaMapVueIframeSnippet,
  createHonuaMapVueSnippet,
  defineHonuaMapElement,
} from '../src/index';

describe('honua map snippets', () => {
  beforeEach(() => {
    defineHonuaMapElement();
    document.body.replaceChildren();
  });

  it('generates a white-label custom-element snippet without credentials by default', () => {
    const snippet = createHonuaMapSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets', 'work-orders'],
      apiKey: 'secret-key',
      center: { latitude: 21.3069, longitude: -157.8583 },
      zoom: 12,
      bounds: {
        minLongitude: -158.3,
        minLatitude: 21.2,
        maxLongitude: -157.6,
        maxLatitude: 21.6,
      },
      basemap: 'streets',
      interactive: true,
      search: true,
      identify: true,
      attribution: 'City GIS',
      theme: 'dark',
      label: 'City asset map',
      style: {
        accent: '#0f766e',
        fontFamily: 'Aptos, sans-serif',
      },
    }, {
      elementName: 'city-asset-map',
    });

    expect(snippet).toContain('defineHonuaMapElement(\'city-asset-map\')');
    expect(snippet).toContain('<city-asset-map');
    expect(snippet).toContain('service-url="https://services.example.test/FeatureServer"');
    expect(snippet).toContain('layer-ids="assets,work-orders"');
    expect(snippet).toContain('bbox="-158.3,21.2,-157.6,21.6"');
    expect(snippet).toContain('interactive');
    expect(snippet).toContain('label="City asset map"');
    expect(snippet).toContain('style="--honua-map-accent: #0f766e; --honua-map-font-family: Aptos, sans-serif"');
    expect(snippet).not.toContain('secret-key');
  });

  it('includes credentials only when explicitly requested', () => {
    const snippet = createHonuaMapSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      apiKey: 'public-browser-key',
    }, {
      includeCredentials: true,
      includeScript: false,
    });

    expect(snippet).toContain('api-key="public-browser-key"');
    expect(snippet).not.toContain('<script');
  });

  it('generates a CDN custom-element snippet without credentials by default', () => {
    const snippet = createHonuaMapCdnSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets'],
      apiKey: 'secret-key',
      interactive: true,
      label: 'City asset map',
    }, {
      scriptAttributes: {
        integrity: 'sha384-example',
        crossOrigin: 'anonymous',
        referrerPolicy: 'strict-origin',
      },
    });

    expect(snippet).toContain(
      '<script type="module" src="https://cdn.honua.dev/embed.js" integrity="sha384-example" crossorigin="anonymous" referrerpolicy="strict-origin"></script>',
    );
    expect(snippet).toContain('<honua-map');
    expect(snippet).toContain('service-url="https://services.example.test/FeatureServer"');
    expect(snippet).toContain('interactive');
    expect(snippet).toContain('label="City asset map"');
    expect(snippet).not.toContain('secret-key');
  });

  it('generates a CDN branded tag snippet with an inline define call', () => {
    const snippet = createHonuaMapCdnSnippet({
      basemap: 'dark',
      apiKey: 'public-browser-key',
    }, {
      scriptUrl: 'https://cdn.example.test/honua/embed.js',
      elementName: 'city-asset-map',
      includeCredentials: true,
      scriptAttributes: {
        nonce: 'nonce-value',
      },
    });

    expect(snippet).toContain('<script type="module" nonce="nonce-value">');
    expect(snippet).toContain(
      'import { defineHonuaMapElement } from \'https://cdn.example.test/honua/embed.js\';',
    );
    expect(snippet).toContain('defineHonuaMapElement(\'city-asset-map\')');
    expect(snippet).toContain('<city-asset-map');
    expect(snippet).toContain('basemap="dark"');
    expect(snippet).toContain('api-key="public-browser-key"');
  });

  it('omits the CDN script when requested', () => {
    const snippet = createHonuaMapCdnSnippet({
      search: true,
    }, {
      includeScript: false,
    });

    expect(snippet).toBe('<honua-map\n  search>\n</honua-map>');
  });

  it('applies map options to an existing element and removes null values', () => {
    const element = document.createElement('honua-map');
    element.setAttribute('service-url', 'https://old.example.test');
    element.setAttribute('interactive', '');
    element.style.setProperty('--honua-map-accent', '#123456');
    element.style.setProperty('--honua-map-background', '#eeeeee');
    document.body.append(element);

    applyHonuaMapOptions(element, {
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets', ''],
      interactive: false,
      search: true,
      style: {
        accent: null,
        surface: '#ffffff',
      },
    });

    expect(element.getAttribute('service-url')).toBe('https://services.example.test/FeatureServer');
    expect(element.getAttribute('layer-ids')).toBe('assets');
    expect(element.hasAttribute('interactive')).toBe(false);
    expect(element.hasAttribute('search')).toBe(true);
    expect(element.style.getPropertyValue('--honua-map-accent')).toBe('');
    expect(element.style.getPropertyValue('--honua-map-background')).toBe('#eeeeee');
    expect(element.style.getPropertyValue('--honua-map-surface')).toBe('#ffffff');
  });

  it('generates an iframe fallback snippet without credentials by default', () => {
    const snippet = createHonuaMapIframeSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets'],
      apiKey: 'secret-key',
      interactive: true,
    });

    const iframe = readIframe(snippet);

    expect(iframe.getAttribute('title')).toBe('Embedded map');
    expect(iframe.getAttribute('loading')).toBe('lazy');
    expect(iframe.getAttribute('referrerpolicy')).toBe('strict-origin-when-cross-origin');
    expect(iframe.getAttribute('sandbox')).toBe('allow-scripts allow-same-origin allow-forms');
    expect(iframe.getAttribute('src')).not.toContain('secret-key');
    expect(snippet).not.toContain('api-key');
  });

  it('includes iframe credentials only when explicitly requested', () => {
    const snippet = createHonuaMapIframeSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      apiKey: 'public-browser-key',
    }, {
      includeCredentials: true,
    });

    const src = readIframe(snippet).getAttribute('src');
    const url = new URL(src ?? '');

    expect(url.searchParams.get('api-key')).toBe('public-browser-key');
  });

  it('serializes map options into iframe query parameters', () => {
    const snippet = createHonuaMapIframeSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      layerIds: ['assets', 'work-orders'],
      center: { latitude: 21.3069, longitude: -157.8583 },
      zoom: 12,
      bounds: {
        minLongitude: -158.3,
        minLatitude: 21.2,
        maxLongitude: -157.6,
        maxLatitude: 21.6,
      },
      basemap: 'streets',
      interactive: true,
      search: true,
      identify: true,
      attribution: 'City GIS',
      theme: 'dark',
      label: 'City asset map',
      style: {
        accent: '#0f766e',
        fontFamily: 'Aptos, sans-serif',
      },
    }, {
      iframeUrl: 'https://embed.example.test/map.html?tenant=city',
      parentOrigin: 'https://portal.example.test/admin/embed',
    });

    const src = readIframe(snippet).getAttribute('src');
    const url = new URL(src ?? '');

    expect(url.origin).toBe('https://embed.example.test');
    expect(url.searchParams.get('tenant')).toBe('city');
    expect(url.searchParams.get('parent-origin')).toBe('https://portal.example.test/admin/embed');
    expect(url.searchParams.get('service-url')).toBe('https://services.example.test/FeatureServer');
    expect(url.searchParams.get('layer-ids')).toBe('assets,work-orders');
    expect(url.searchParams.get('center')).toBe('21.3069,-157.8583');
    expect(url.searchParams.get('zoom')).toBe('12');
    expect(url.searchParams.get('bbox')).toBe('-158.3,21.2,-157.6,21.6');
    expect(url.searchParams.get('basemap')).toBe('streets');
    expect(url.searchParams.get('interactive')).toBe('true');
    expect(url.searchParams.get('search')).toBe('true');
    expect(url.searchParams.get('identify')).toBe('true');
    expect(url.searchParams.get('attribution')).toBe('City GIS');
    expect(url.searchParams.get('theme')).toBe('dark');
    expect(url.searchParams.get('label')).toBe('City asset map');
    expect(url.searchParams.get('--honua-map-accent')).toBe('#0f766e');
    expect(url.searchParams.get('--honua-map-font-family')).toBe('Aptos, sans-serif');
  });

  it('escapes iframe fallback HTML attribute values', () => {
    const snippet = createHonuaMapIframeSnippet({
      label: 'City "Assets" <Map> & Team',
    }, {
      iframe: {
        title: 'City "Assets" <Map> & Team',
      },
    });

    expect(snippet).toContain('title="City &quot;Assets&quot; &lt;Map&gt; &amp; Team"');
    expect(snippet).not.toContain('title="City "Assets" <Map> & Team"');
  });

  it('applies custom iframe fallback options', () => {
    const snippet = createHonuaMapIframeSnippet({
      basemap: 'satellite',
    }, {
      iframeUrl: '/embeds/map.html',
      indent: '    ',
      iframe: {
        title: 'Asset map',
        loading: 'eager',
        referrerPolicy: 'no-referrer',
        sandbox: ['allow-scripts', 'allow-same-origin', '', 'allow-popups'],
        allow: 'geolocation',
        width: '100%',
        height: 420,
        id: 'asset-map-frame',
        name: 'asset-map',
        className: 'embed-frame',
        style: 'border: 0',
      },
    });

    const iframe = readIframe(snippet);

    expect(snippet).toContain('\n    src=');
    expect(iframe.getAttribute('src')).toBe('/embeds/map.html?basemap=satellite');
    expect(iframe.getAttribute('title')).toBe('Asset map');
    expect(iframe.getAttribute('loading')).toBe('eager');
    expect(iframe.getAttribute('referrerpolicy')).toBe('no-referrer');
    expect(iframe.getAttribute('sandbox')).toBe('allow-scripts allow-same-origin allow-popups');
    expect(iframe.getAttribute('allow')).toBe('geolocation');
    expect(iframe.getAttribute('width')).toBe('100%');
    expect(iframe.getAttribute('height')).toBe('420');
    expect(iframe.id).toBe('asset-map-frame');
    expect(iframe.getAttribute('name')).toBe('asset-map');
    expect(iframe.className).toBe('embed-frame');
    expect(iframe.getAttribute('style')).toBe('border: 0');
  });

  it('preserves protocol-relative iframe fallback URLs', () => {
    const snippet = createHonuaMapIframeSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      search: true,
    }, {
      iframeUrl: '//embed.example.test/map.html?tenant=city#map',
    });

    expect(readIframe(snippet).getAttribute('src')).toBe(
      '//embed.example.test/map.html?tenant=city&service-url=https%3A%2F%2Fservices.example.test%2FFeatureServer&search=true#map',
    );
  });

  it('generates a React custom-element component without credentials by default', () => {
    const snippet = createHonuaMapReactSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer?name="Assets"&team=<GIS>',
      layerIds: ['assets'],
      apiKey: 'secret-key',
      interactive: true,
      label: 'City "Assets" <Map> & Team',
      style: {
        accent: '#0f766e',
        fontFamily: 'Aptos, sans-serif',
      },
    }, {
      componentName: 'CityAssetMap',
    });

    expect(snippet).toContain('export function CityAssetMap()');
    expect(snippet).toContain('import \'@honua-io/embed\';');
    expect(snippet).toContain('<honua-map');
    expect(snippet).toContain('service-url="https://services.example.test/FeatureServer?name=&quot;Assets&quot;&amp;team=&lt;GIS&gt;"');
    expect(snippet).toContain('interactive');
    expect(snippet).toContain('label="City &quot;Assets&quot; &lt;Map&gt; &amp; Team"');
    expect(snippet).toContain('style={{ \'--honua-map-accent\': \'#0f766e\', \'--honua-map-font-family\': \'Aptos, sans-serif\' }}');
    expect(snippet).not.toContain('secret-key');
  });

  it('registers custom framework element names when requested', () => {
    const reactSnippet = createHonuaMapReactSnippet({
      search: true,
    }, {
      elementName: 'city-map',
      componentName: 'CityMap',
    });
    const vueSnippet = createHonuaMapVueSnippet({
      identify: true,
    }, {
      elementName: 'city-vue-map',
    });
    const angularSnippet = createHonuaMapAngularSnippet({
      interactive: true,
    }, {
      elementName: 'city-angular-map',
      selector: 'city-angular-map-host',
    });

    expect(reactSnippet).toContain('import { defineHonuaMapElement } from \'@honua-io/embed\';');
    expect(reactSnippet).toContain('defineHonuaMapElement(\'city-map\');');
    expect(reactSnippet).toContain('<city-map');
    expect(vueSnippet).toContain('defineHonuaMapElement(\'city-vue-map\');');
    expect(vueSnippet).toContain('<city-vue-map');
    expect(angularSnippet).toContain('defineHonuaMapElement(\'city-angular-map\');');
    expect(angularSnippet).toContain('<city-angular-map');
  });

  it('omits custom-element registration for framework snippets when requested', () => {
    const snippet = createHonuaMapVueSnippet({
      search: true,
    }, {
      includeScript: false,
    });

    expect(snippet).not.toContain('<script');
    expect(snippet).not.toContain('@honua-io/embed');
  });

  it('generates a React iframe fallback component with serialized iframe options', () => {
    const snippet = createHonuaMapReactIframeSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      apiKey: 'secret-key',
      search: true,
    }, {
      componentName: 'CityAssetMapFrame',
      iframeUrl: 'https://embed.example.test/map.html?tenant=city',
      parentOrigin: 'https://portal.example.test',
      iframe: {
        title: 'City "Assets"',
        referrerPolicy: 'no-referrer',
        className: 'embed-frame',
      },
    });

    expect(snippet).toContain('export function CityAssetMapFrame()');
    expect(snippet).toContain('<iframe');
    expect(snippet).toContain('src="https://embed.example.test/map.html?tenant=city&amp;service-url=https%3A%2F%2Fservices.example.test%2FFeatureServer&amp;search=true&amp;parent-origin=https%3A%2F%2Fportal.example.test"');
    expect(snippet).toContain('title="City &quot;Assets&quot;"');
    expect(snippet).toContain('referrerPolicy="no-referrer"');
    expect(snippet).toContain('className="embed-frame"');
    expect(snippet).not.toContain('secret-key');
  });

  it('generates Vue custom-element and iframe snippets', () => {
    const elementSnippet = createHonuaMapVueSnippet({
      layerIds: ['assets'],
      apiKey: 'secret-key',
      identify: true,
      label: 'City asset map',
    });
    const iframeSnippet = createHonuaMapVueIframeSnippet({
      basemap: 'satellite',
    }, {
      iframeUrl: '/embeds/map.html',
    });

    expect(elementSnippet).toContain('<template>\n  <honua-map');
    expect(elementSnippet).toContain('    layer-ids="assets"');
    expect(elementSnippet).toContain('    identify');
    expect(elementSnippet).toContain('<script setup lang="ts">\nimport \'@honua-io/embed\';\n</script>');
    expect(elementSnippet).not.toContain('secret-key');
    expect(iframeSnippet).toContain('<template>\n  <iframe');
    expect(iframeSnippet).toContain('    src="/embeds/map.html?basemap=satellite"');
    expect(iframeSnippet).not.toContain('<script');
  });

  it('generates Angular custom-element and iframe components', () => {
    const elementSnippet = createHonuaMapAngularSnippet({
      serviceUrl: 'https://services.example.test/FeatureServer',
      apiKey: 'secret-key',
      search: true,
    }, {
      componentName: 'CityAssetMapComponent',
      selector: 'city-asset-map',
    });
    const iframeSnippet = createHonuaMapAngularIframeSnippet({
      basemap: 'dark',
    }, {
      componentName: 'CityAssetMapFrameComponent',
      selector: 'city-asset-map-frame',
      iframeUrl: '/embeds/map.html',
    });

    expect(elementSnippet).toContain('import { Component, CUSTOM_ELEMENTS_SCHEMA } from \'@angular/core\';');
    expect(elementSnippet).toContain('import \'@honua-io/embed\';');
    expect(elementSnippet).toContain('selector: \'city-asset-map\'');
    expect(elementSnippet).toContain('schemas: [CUSTOM_ELEMENTS_SCHEMA]');
    expect(elementSnippet).toContain('<honua-map');
    expect(elementSnippet).toContain('service-url="https://services.example.test/FeatureServer"');
    expect(elementSnippet).toContain('search');
    expect(elementSnippet).toContain('export class CityAssetMapComponent {}');
    expect(elementSnippet).not.toContain('secret-key');
    expect(iframeSnippet).toContain('selector: \'city-asset-map-frame\'');
    expect(iframeSnippet).toContain('<iframe');
    expect(iframeSnippet).toContain('src="/embeds/map.html?basemap=dark"');
    expect(iframeSnippet).toContain('export class CityAssetMapFrameComponent {}');
  });
});

function readIframe(snippet: string): HTMLIFrameElement {
  const template = document.createElement('template');
  template.innerHTML = snippet;
  const iframe = template.content.querySelector('iframe');
  if (!iframe) {
    throw new Error('Expected iframe snippet');
  }

  return iframe;
}
