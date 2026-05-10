import type { HonuaMapBounds, HonuaMapCoordinate } from './map';
import type { HonuaSceneCoordinate } from './scene';

export interface HonuaMapThemeOptions {
  accent?: string | null;
  background?: string | null;
  foreground?: string | null;
  muted?: string | null;
  surface?: string | null;
  border?: string | null;
  fontFamily?: string | null;
  controlSize?: string | null;
}

export interface HonuaMapEmbedOptions {
  serviceUrl?: string | null;
  layerIds?: readonly string[] | null;
  apiKey?: string | null;
  center?: HonuaMapCoordinate | null;
  zoom?: number | null;
  bounds?: HonuaMapBounds | null;
  basemap?: string | null;
  interactive?: boolean | null;
  search?: boolean | null;
  identify?: boolean | null;
  attribution?: string | null;
  theme?: 'light' | 'dark' | null;
  label?: string | null;
  style?: HonuaMapThemeOptions | null;
}

export interface HonuaMapSnippetOptions {
  packageName?: string;
  elementName?: string;
  includeScript?: boolean;
  includeCredentials?: boolean;
  indent?: string;
}

export interface HonuaMapCdnScriptAttributes {
  nonce?: string | null;
  integrity?: string | null;
  crossOrigin?: 'anonymous' | 'use-credentials' | null;
  referrerPolicy?: ReferrerPolicy | null;
}

export type HonuaSceneCdnScriptAttributes = HonuaMapCdnScriptAttributes;

export interface HonuaMapCdnSnippetOptions {
  scriptUrl?: string;
  elementName?: string;
  includeScript?: boolean;
  includeCredentials?: boolean;
  indent?: string;
  scriptAttributes?: HonuaMapCdnScriptAttributes;
}

export interface HonuaSceneEmbedOptions {
  tilesetUrl?: string | null;
  terrainUrl?: string | null;
  metadataUrl?: string | null;
  ionToken?: string | null;
  cesiumBaseUrl?: string | null;
  center?: HonuaSceneCoordinate | null;
  height?: number | null;
  heading?: number | null;
  pitch?: number | null;
  roll?: number | null;
  theme?: 'light' | 'dark' | null;
  autoload?: boolean | null;
}

export interface HonuaSceneSnippetOptions {
  packageName?: string;
  elementName?: string;
  includeScript?: boolean;
  includeCredentials?: boolean;
  indent?: string;
}

export interface HonuaSceneCdnSnippetOptions {
  scriptUrl?: string;
  elementName?: string;
  includeScript?: boolean;
  includeCredentials?: boolean;
  indent?: string;
  scriptAttributes?: HonuaSceneCdnScriptAttributes;
}

export interface HonuaMapIframeAttributes {
  title?: string | null;
  loading?: 'eager' | 'lazy' | null;
  referrerPolicy?: ReferrerPolicy | null;
  sandbox?: string | readonly string[] | null;
  allow?: string | null;
  width?: string | number | null;
  height?: string | number | null;
  id?: string | null;
  name?: string | null;
  className?: string | null;
  style?: string | null;
}

export interface HonuaMapIframeSnippetOptions {
  iframeUrl?: string;
  includeCredentials?: boolean;
  parentOrigin?: string | null;
  iframe?: HonuaMapIframeAttributes;
  indent?: string;
}

export interface HonuaSceneIframeSnippetOptions {
  iframeUrl?: string;
  includeCredentials?: boolean;
  parentOrigin?: string | null;
  iframe?: HonuaMapIframeAttributes;
  indent?: string;
}

export interface HonuaMapReactSnippetOptions extends HonuaMapSnippetOptions {
  componentName?: string;
}

export interface HonuaMapReactIframeSnippetOptions extends HonuaMapIframeSnippetOptions {
  componentName?: string;
}

export interface HonuaMapVueSnippetOptions extends HonuaMapSnippetOptions {
}

export interface HonuaMapVueIframeSnippetOptions extends HonuaMapIframeSnippetOptions {
}

export interface HonuaMapAngularSnippetOptions extends HonuaMapSnippetOptions {
  componentName?: string;
  selector?: string;
}

export interface HonuaMapAngularIframeSnippetOptions extends HonuaMapIframeSnippetOptions {
  componentName?: string;
  selector?: string;
}

export function applyHonuaMapOptions(
  element: HTMLElement,
  options: HonuaMapEmbedOptions,
): void {
  setOptionalAttribute(element, 'service-url', options.serviceUrl);
  setOptionalAttribute(element, 'layer-ids', serializeList(options.layerIds));
  setOptionalAttribute(element, 'api-key', options.apiKey);
  setOptionalAttribute(element, 'center', serializeCoordinate(options.center));
  setOptionalAttribute(element, 'zoom', serializeNumber(options.zoom));
  setOptionalAttribute(element, 'bbox', serializeBounds(options.bounds));
  setOptionalAttribute(element, 'basemap', options.basemap);
  setBooleanAttribute(element, 'interactive', options.interactive);
  setBooleanAttribute(element, 'search', options.search);
  setBooleanAttribute(element, 'identify', options.identify);
  setOptionalAttribute(element, 'attribution', options.attribution);
  setOptionalAttribute(element, 'theme', options.theme);
  setOptionalAttribute(element, 'label', options.label);

  if (options.style !== undefined) {
    applyHonuaMapTheme(element, options.style);
  }
}

export function applyHonuaMapTheme(element: HTMLElement, theme: HonuaMapThemeOptions | null): void {
  for (const [property, value] of Object.entries(mapThemeVariables(theme))) {
    if (value === undefined) {
      continue;
    }

    if (value === null) {
      element.style.removeProperty(property);
      continue;
    }

    element.style.setProperty(property, value);
  }
}

export function applyHonuaSceneOptions(
  element: HTMLElement,
  options: HonuaSceneEmbedOptions,
): void {
  setOptionalAttribute(element, 'tileset-url', options.tilesetUrl);
  setOptionalAttribute(element, 'terrain-url', options.terrainUrl);
  setOptionalAttribute(element, 'metadata-url', options.metadataUrl);
  setOptionalAttribute(element, 'ion-token', options.ionToken);
  setOptionalAttribute(element, 'cesium-base-url', options.cesiumBaseUrl);
  setOptionalAttribute(element, 'center', serializeCoordinate(options.center));
  setOptionalAttribute(element, 'height', serializeNumber(options.height));
  setOptionalAttribute(element, 'heading', serializeNumber(options.heading));
  setOptionalAttribute(element, 'pitch', serializeNumber(options.pitch));
  setOptionalAttribute(element, 'roll', serializeNumber(options.roll));
  setOptionalAttribute(element, 'theme', options.theme);
  setAutoloadAttribute(element, options.autoload);
}

export function createHonuaMapSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapSnippetOptions = {},
): string {
  const elementName = snippetOptions.elementName ?? 'honua-map';
  assertCustomElementName(elementName);

  const includeScript = snippetOptions.includeScript ?? true;
  const packageName = snippetOptions.packageName ?? '@honua-io/embed';
  const indent = snippetOptions.indent ?? '  ';
  const element = createElementMarkup(elementName, options, {
    includeCredentials: snippetOptions.includeCredentials ?? false,
    indent,
  });

  if (!includeScript) {
    return element;
  }

  const script = elementName === 'honua-map'
    ? [
      '<script type="module">',
      `${indent}import '${escapeJsString(packageName)}';`,
      '</script>',
    ].join('\n')
    : [
      '<script type="module">',
      `${indent}import { defineHonuaMapElement } from '${escapeJsString(packageName)}';`,
      `${indent}defineHonuaMapElement('${escapeJsString(elementName)}');`,
      '</script>',
    ].join('\n');

  return `${script}\n\n${element}`;
}

export function createHonuaSceneSnippet(
  options: HonuaSceneEmbedOptions,
  snippetOptions: HonuaSceneSnippetOptions = {},
): string {
  const elementName = snippetOptions.elementName ?? 'honua-scene';
  assertCustomElementName(elementName);

  const includeScript = snippetOptions.includeScript ?? true;
  const packageName = snippetOptions.packageName ?? '@honua-io/embed';
  const indent = snippetOptions.indent ?? '  ';
  const element = createSceneElementMarkup(elementName, options, {
    includeCredentials: snippetOptions.includeCredentials ?? false,
    indent,
  });

  if (!includeScript) {
    return element;
  }

  const script = elementName === 'honua-scene'
    ? [
      '<script type="module">',
      `${indent}import '${escapeJsString(packageName)}';`,
      '</script>',
    ].join('\n')
    : [
      '<script type="module">',
      `${indent}import { defineHonuaSceneElement } from '${escapeJsString(packageName)}';`,
      `${indent}defineHonuaSceneElement('${escapeJsString(elementName)}');`,
      '</script>',
    ].join('\n');

  return `${script}\n\n${element}`;
}

export function createHonuaMapCdnSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapCdnSnippetOptions = {},
): string {
  const elementName = snippetOptions.elementName ?? 'honua-map';
  assertCustomElementName(elementName);

  const includeScript = snippetOptions.includeScript ?? true;
  const scriptUrl = snippetOptions.scriptUrl ?? 'https://cdn.honua.dev/embed.js';
  const indent = snippetOptions.indent ?? '  ';
  const element = createElementMarkup(elementName, options, {
    includeCredentials: snippetOptions.includeCredentials ?? false,
    indent,
  });

  if (!includeScript) {
    return element;
  }

  const script = createMapCdnScriptMarkup(scriptUrl, elementName, indent, snippetOptions.scriptAttributes);
  return `${script}\n\n${element}`;
}

export function createHonuaSceneCdnSnippet(
  options: HonuaSceneEmbedOptions,
  snippetOptions: HonuaSceneCdnSnippetOptions = {},
): string {
  const elementName = snippetOptions.elementName ?? 'honua-scene';
  assertCustomElementName(elementName);

  const includeScript = snippetOptions.includeScript ?? true;
  const scriptUrl = snippetOptions.scriptUrl ?? 'https://cdn.honua.dev/embed.js';
  const indent = snippetOptions.indent ?? '  ';
  const element = createSceneElementMarkup(elementName, options, {
    includeCredentials: snippetOptions.includeCredentials ?? false,
    indent,
  });

  if (!includeScript) {
    return element;
  }

  const script = createCdnScriptMarkup({
    scriptUrl,
    elementName,
    defaultElementName: 'honua-scene',
    defineExportName: 'defineHonuaSceneElement',
    indent,
    attributes: snippetOptions.scriptAttributes,
  });
  return `${script}\n\n${element}`;
}

export function createHonuaMapIframeSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapIframeSnippetOptions = {},
): string {
  const iframeUrl = snippetOptions.iframeUrl ?? 'https://cdn.honua.dev/embed/map.html';
  const src = createIframeSrc(iframeUrl, mapQueryParameters(options, snippetOptions.includeCredentials ?? false), {
    parentOrigin: snippetOptions.parentOrigin,
  });
  const attributes = iframeAttributes(src, snippetOptions.iframe, 'Embedded map');
  const indent = snippetOptions.indent ?? '  ';
  const lines = attributes.map(([name, value]) => (
    `${indent}${name}="${escapeHtmlAttribute(value)}"`
  ));

  return `<iframe\n${lines.join('\n')}>\n</iframe>`;
}

export function createHonuaSceneIframeSnippet(
  options: HonuaSceneEmbedOptions,
  snippetOptions: HonuaSceneIframeSnippetOptions = {},
): string {
  const iframeUrl = snippetOptions.iframeUrl ?? 'https://cdn.honua.dev/embed/scene.html';
  const src = createIframeSrc(iframeUrl, sceneQueryParameters(options, snippetOptions.includeCredentials ?? false), {
    parentOrigin: snippetOptions.parentOrigin,
  });
  const attributes = iframeAttributes(src, snippetOptions.iframe, 'Embedded scene');
  const indent = snippetOptions.indent ?? '  ';
  const lines = attributes.map(([name, value]) => (
    `${indent}${name}="${escapeHtmlAttribute(value)}"`
  ));

  return `<iframe\n${lines.join('\n')}>\n</iframe>`;
}

export function createHonuaMapReactSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapReactSnippetOptions = {},
): string {
  const componentName = snippetOptions.componentName ?? 'HonuaMapEmbed';
  assertJsIdentifier(componentName);
  const elementName = snippetOptions.elementName ?? 'honua-map';
  assertCustomElementName(elementName);
  const packageName = snippetOptions.packageName ?? '@honua-io/embed';
  const indent = snippetOptions.indent ?? '  ';
  const registration = createReactRegistrationSnippet(
    packageName,
    elementName,
    snippetOptions.includeScript ?? true,
    indent,
  );
  const element = createReactElementMarkup(elementName, options, {
    includeCredentials: snippetOptions.includeCredentials ?? false,
    indent: `${indent}${indent}`,
  });

  return [
    ...registration.imports,
    ...(registration.imports.length > 0 ? [''] : []),
    `export function ${componentName}() {`,
    ...registration.effectLines,
    `${indent}return (`,
    element,
    `${indent});`,
    '}',
    ...(registration.helpers.length > 0 ? ['', ...registration.helpers] : []),
  ].join('\n');
}

export function createHonuaMapReactIframeSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapReactIframeSnippetOptions = {},
): string {
  const componentName = snippetOptions.componentName ?? 'HonuaMapEmbed';
  assertJsIdentifier(componentName);
  const indent = snippetOptions.indent ?? '  ';
  const element = createReactIframeMarkup(options, {
    ...snippetOptions,
    indent: `${indent}${indent}`,
  });

  return [
    `export function ${componentName}() {`,
    `${indent}return (`,
    element,
    `${indent});`,
    '}',
  ].join('\n');
}

export function createHonuaMapVueSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapVueSnippetOptions = {},
): string {
  const elementName = snippetOptions.elementName ?? 'honua-map';
  assertCustomElementName(elementName);
  const packageName = snippetOptions.packageName ?? '@honua-io/embed';
  const indent = snippetOptions.indent ?? '  ';
  const registration = createVueRegistrationLines(
    packageName,
    elementName,
    snippetOptions.includeScript ?? true,
  );
  const element = createElementMarkup(elementName, options, {
    includeCredentials: snippetOptions.includeCredentials ?? false,
    indent,
  });

  return [
    '<template>',
    indentBlock(element, indent),
    '</template>',
    ...(registration.length > 0
      ? [
        '',
        '<script setup lang="ts">',
        ...registration,
        '</script>',
      ]
      : []),
  ].join('\n');
}

export function createHonuaMapVueIframeSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapVueIframeSnippetOptions = {},
): string {
  const indent = snippetOptions.indent ?? '  ';
  const iframe = createHonuaMapIframeSnippet(options, {
    ...snippetOptions,
    indent,
  });

  return [
    '<template>',
    indentBlock(iframe, indent),
    '</template>',
  ].join('\n');
}

export function createHonuaMapAngularSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapAngularSnippetOptions = {},
): string {
  const componentName = snippetOptions.componentName ?? 'HonuaMapEmbedComponent';
  assertJsIdentifier(componentName);
  const selector = snippetOptions.selector ?? 'honua-map-embed';
  assertCustomElementName(selector);
  const elementName = snippetOptions.elementName ?? 'honua-map';
  assertCustomElementName(elementName);
  const packageName = snippetOptions.packageName ?? '@honua-io/embed';
  const indent = snippetOptions.indent ?? '  ';
  const registration = createAngularRegistrationSnippet(
    packageName,
    elementName,
    snippetOptions.includeScript ?? true,
    indent,
  );
  const element = createElementMarkup(elementName, options, {
    includeCredentials: snippetOptions.includeCredentials ?? false,
    indent,
  });

  return createAngularComponentSnippet(componentName, selector, registration, element, indent);
}

export function createHonuaMapAngularIframeSnippet(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapAngularIframeSnippetOptions = {},
): string {
  const componentName = snippetOptions.componentName ?? 'HonuaMapEmbedComponent';
  assertJsIdentifier(componentName);
  const selector = snippetOptions.selector ?? 'honua-map-embed';
  assertCustomElementName(selector);
  const indent = snippetOptions.indent ?? '  ';
  const iframe = createHonuaMapIframeSnippet(options, {
    ...snippetOptions,
    indent,
  });

  return createAngularComponentSnippet(
    componentName,
    selector,
    emptyAngularRegistrationSnippet(),
    iframe,
    indent,
  );
}

function createCdnScriptMarkup(
  config: {
    scriptUrl: string;
    elementName: string;
    defaultElementName: string;
    defineExportName: string;
    indent: string;
    attributes: HonuaMapCdnScriptAttributes | undefined;
  },
): string {
  const {
    scriptUrl,
    elementName,
    defaultElementName,
    defineExportName,
    indent,
    attributes,
  } = config;
  if (elementName !== defaultElementName) {
    const inlineScriptAttributes = serializeHtmlAttributes([
      ['type', 'module'],
      ['nonce', attributes?.nonce],
      ['crossorigin', attributes?.crossOrigin],
      ['referrerpolicy', attributes?.referrerPolicy],
    ]);
    return [
      `<script ${inlineScriptAttributes}>`,
      `${indent}import { ${defineExportName} } from '${escapeJsString(scriptUrl)}';`,
      `${indent}${defineExportName}('${escapeJsString(elementName)}');`,
      '</script>',
    ].join('\n');
  }

  const serialized = serializeHtmlAttributes([
    ['type', 'module'],
    ['src', scriptUrl],
    ['nonce', attributes?.nonce],
    ['integrity', attributes?.integrity],
    ['crossorigin', attributes?.crossOrigin],
    ['referrerpolicy', attributes?.referrerPolicy],
  ]);

  return `<script ${serialized}></script>`;
}

function serializeHtmlAttributes(
  attributes: Array<[string, string | null | undefined]>,
): string {
  return attributes
    .filter((entry): entry is [string, string] => {
      const value = entry[1];
      return value !== undefined && value !== null && value !== '';
    })
    .map(([name, value]) => `${name}="${escapeHtmlAttribute(value)}"`)
    .join(' ');
}

function createMapCdnScriptMarkup(
  scriptUrl: string,
  elementName: string,
  indent: string,
  attributes: HonuaMapCdnScriptAttributes | undefined,
): string {
  return createCdnScriptMarkup({
    scriptUrl,
    elementName,
    defaultElementName: 'honua-map',
    defineExportName: 'defineHonuaMapElement',
    indent,
    attributes,
  });
}

function createAngularComponentSnippet(
  componentName: string,
  selector: string,
  registration: AngularRegistrationSnippet,
  template: string,
  indent: string,
): string {
  const lines = [
    `import { ${['Component', 'CUSTOM_ELEMENTS_SCHEMA', ...registration.coreImports].join(', ')} } from '@angular/core';`,
    '',
    '@Component({',
    `${indent}selector: '${escapeJsString(selector)}',`,
    `${indent}standalone: true,`,
    `${indent}schemas: [CUSTOM_ELEMENTS_SCHEMA],`,
    `${indent}template: \``,
    indentTemplateLiteral(template, indent),
    `${indent}\`,`,
    '})',
    registration.classBody.length > 0
      ? [
        `export class ${componentName}${registration.classImplements} {`,
        ...registration.classBody,
        '}',
      ].join('\n')
      : `export class ${componentName} {}`,
  ];

  return lines.join('\n');
}

interface ReactRegistrationSnippet {
  imports: string[];
  effectLines: string[];
  helpers: string[];
}

interface AngularRegistrationSnippet {
  coreImports: string[];
  classImplements: string;
  classBody: string[];
}

function emptyAngularRegistrationSnippet(): AngularRegistrationSnippet {
  return { coreImports: [], classImplements: '', classBody: [] };
}

function createReactRegistrationSnippet(
  packageName: string,
  elementName: string,
  includeScript: boolean,
  indent: string,
): ReactRegistrationSnippet {
  if (!includeScript) {
    return { imports: [], effectLines: [], helpers: [] };
  }

  return {
    imports: ['import { useEffect } from \'react\';'],
    effectLines: [
      `${indent}useEffect(() => {`,
      `${indent}${indent}void registerHonuaMapElement();`,
      `${indent}}, []);`,
      '',
    ],
    helpers: createBrowserRegistrationHelper(packageName, elementName, ''),
  };
}

function createVueRegistrationLines(
  packageName: string,
  elementName: string,
  includeScript: boolean,
): string[] {
  if (!includeScript) {
    return [];
  }

  return [
    'import { onMounted } from \'vue\';',
    '',
    'onMounted(() => {',
    '  void registerHonuaMapElement();',
    '});',
    '',
    ...createBrowserRegistrationHelper(packageName, elementName, ''),
  ];
}

function createAngularRegistrationSnippet(
  packageName: string,
  elementName: string,
  includeScript: boolean,
  indent: string,
): AngularRegistrationSnippet {
  if (!includeScript) {
    return emptyAngularRegistrationSnippet();
  }

  return {
    coreImports: ['AfterViewInit'],
    classImplements: ' implements AfterViewInit',
    classBody: [
      `${indent}ngAfterViewInit(): void {`,
      `${indent}${indent}void this.registerHonuaMapElement();`,
      `${indent}}`,
      '',
      `${indent}private async registerHonuaMapElement(): Promise<void> {`,
      ...createBrowserRegistrationBody(packageName, elementName, `${indent}${indent}`),
      `${indent}}`,
    ],
  };
}

function createBrowserRegistrationHelper(
  packageName: string,
  elementName: string,
  indent: string,
): string[] {
  return [
    `${indent}async function registerHonuaMapElement(): Promise<void> {`,
    ...createBrowserRegistrationBody(packageName, elementName, `${indent}  `),
    `${indent}}`,
  ];
}

function createBrowserRegistrationBody(
  packageName: string,
  elementName: string,
  indent: string,
): string[] {
  const escapedPackageName = escapeJsString(packageName);
  const escapedElementName = escapeJsString(elementName);
  const loadLine = elementName === 'honua-map'
    ? `${indent}await import('${escapedPackageName}');`
    : `${indent}const { defineHonuaMapElement } = await import('${escapedPackageName}');`;
  const defineLine = elementName === 'honua-map'
    ? null
    : `${indent}defineHonuaMapElement('${escapedElementName}');`;
  return [
    `${indent}if (typeof window === 'undefined') {`,
    `${indent}  return;`,
    `${indent}}`,
    '',
    loadLine,
    ...(defineLine ? [defineLine] : []),
  ];
}

function createElementMarkup(
  elementName: string,
  options: HonuaMapEmbedOptions,
  config: Required<Pick<HonuaMapSnippetOptions, 'includeCredentials' | 'indent'>>,
): string {
  const attributes = mapAttributes(options, config.includeCredentials);
  const style = serializeTheme(options.style);
  if (style) {
    attributes.push(['style', style]);
  }

  if (attributes.length === 0) {
    return `<${elementName}></${elementName}>`;
  }

  const lines = attributes.map(([name, value]) => value === true
    ? `${config.indent}${name}`
    : `${config.indent}${name}="${escapeHtmlAttribute(value)}"`);

  return `<${elementName}\n${lines.join('\n')}>\n</${elementName}>`;
}

function createSceneElementMarkup(
  elementName: string,
  options: HonuaSceneEmbedOptions,
  config: Required<Pick<HonuaSceneSnippetOptions, 'includeCredentials' | 'indent'>>,
): string {
  const attributes = sceneAttributes(options, config.includeCredentials);
  if (attributes.length === 0) {
    return `<${elementName}></${elementName}>`;
  }

  const lines = attributes.map(([name, value]) => value === true
    ? `${config.indent}${name}`
    : `${config.indent}${name}="${escapeHtmlAttribute(value)}"`);

  return `<${elementName}\n${lines.join('\n')}>\n</${elementName}>`;
}

function createReactElementMarkup(
  elementName: string,
  options: HonuaMapEmbedOptions,
  config: Required<Pick<HonuaMapSnippetOptions, 'includeCredentials' | 'indent'>>,
): string {
  const attributes = mapAttributes(options, config.includeCredentials);
  const style = reactStyleObject(options.style);
  const attributeIndent = `${config.indent}  `;
  const lines = attributes.map(([name, value]) => value === true
    ? `${attributeIndent}${name}`
    : `${attributeIndent}${name}="${escapeHtmlAttribute(value)}"`);
  if (style) {
    lines.push(`${attributeIndent}style={${style}}`);
  }

  if (lines.length === 0) {
    return `${config.indent}<${elementName} />`;
  }

  return `${config.indent}<${elementName}\n${lines.join('\n')}\n${config.indent}/>`;
}

function createReactIframeMarkup(
  options: HonuaMapEmbedOptions,
  snippetOptions: HonuaMapIframeSnippetOptions,
): string {
  const iframeUrl = snippetOptions.iframeUrl ?? 'https://cdn.honua.dev/embed/map.html';
  const src = createIframeSrc(iframeUrl, mapQueryParameters(options, snippetOptions.includeCredentials ?? false), {
    parentOrigin: snippetOptions.parentOrigin,
  });
  const attributes = iframeAttributes(src, snippetOptions.iframe, 'Embedded map');
  const indent = snippetOptions.indent ?? '    ';
  const attributeIndent = `${indent}  `;
  const lines = attributes.map(([name, value]) => {
    const attributeName = reactIframeAttributeName(name);
    return `${attributeIndent}${attributeName}="${escapeHtmlAttribute(value)}"`;
  });

  return `${indent}<iframe\n${lines.join('\n')}\n${indent}/>`;
}

function createIframeSrc(
  iframeUrl: string,
  parameters: Array<[string, string]>,
  config: {
    parentOrigin?: string | null;
  },
): string {
  const url = new URL(iframeUrl, 'https://cdn.honua.dev');
  for (const [name, value] of parameters) {
    url.searchParams.set(name, value);
  }

  const parentOrigin = config.parentOrigin?.trim();
  if (parentOrigin) {
    url.searchParams.set('parent-origin', parentOrigin);
  }

  if (isAbsoluteUrl(iframeUrl)) {
    return url.toString();
  }

  const query = url.searchParams.toString();
  const hash = url.hash;
  if (isProtocolRelativeUrl(iframeUrl)) {
    return `//${url.host}${url.pathname}${query ? `?${query}` : ''}${hash}`;
  }

  const base = `${url.pathname}${query ? `?${query}` : ''}${hash}`;
  return iframeUrl.startsWith('/') ? base : base.replace(/^\//, '');
}

function iframeAttributes(
  src: string,
  custom: HonuaMapIframeAttributes | undefined,
  defaultTitle: string,
): Array<[string, string]> {
  return [
    ['src', src],
    ['title', custom?.title ?? defaultTitle],
    ['loading', custom?.loading ?? 'lazy'],
    ['referrerpolicy', custom?.referrerPolicy ?? 'strict-origin-when-cross-origin'],
    ['sandbox', serializeSandbox(custom?.sandbox) ?? 'allow-scripts allow-same-origin allow-forms'],
    ['allow', custom?.allow],
    ['width', serializeAttributeValue(custom?.width)],
    ['height', serializeAttributeValue(custom?.height)],
    ['id', custom?.id],
    ['name', custom?.name],
    ['class', custom?.className],
    ['style', custom?.style],
  ].filter((entry): entry is [string, string] => {
    const value = entry[1];
    return value !== undefined && value !== null && value !== '';
  });
}

function mapAttributes(
  options: HonuaMapEmbedOptions,
  includeCredentials: boolean,
): Array<[string, string | true]> {
  return [
    ['service-url', options.serviceUrl],
    ['layer-ids', serializeList(options.layerIds)],
    ['api-key', includeCredentials ? options.apiKey : undefined],
    ['center', serializeCoordinate(options.center)],
    ['zoom', serializeNumber(options.zoom)],
    ['bbox', serializeBounds(options.bounds)],
    ['basemap', options.basemap],
    ['interactive', serializeBoolean(options.interactive)],
    ['search', serializeBoolean(options.search)],
    ['identify', serializeBoolean(options.identify)],
    ['attribution', options.attribution],
    ['theme', options.theme],
    ['label', options.label],
  ].filter((entry): entry is [string, string | true] => {
    const value = entry[1];
    return value !== undefined && value !== null && value !== '';
  });
}

function sceneAttributes(
  options: HonuaSceneEmbedOptions,
  includeCredentials: boolean,
): Array<[string, string | true]> {
  return [
    ['tileset-url', options.tilesetUrl],
    ['terrain-url', options.terrainUrl],
    ['metadata-url', options.metadataUrl],
    ['ion-token', includeCredentials ? options.ionToken : undefined],
    ['center', serializeCoordinate(options.center)],
    ['height', serializeNumber(options.height)],
    ['heading', serializeNumber(options.heading)],
    ['pitch', serializeNumber(options.pitch)],
    ['roll', serializeNumber(options.roll)],
    ['theme', options.theme],
    ['autoload', serializeAutoload(options.autoload)],
    ['cesium-base-url', options.cesiumBaseUrl],
  ].filter((entry): entry is [string, string | true] => {
    const value = entry[1];
    return value !== undefined && value !== null && value !== '';
  });
}

function sceneQueryParameters(
  options: HonuaSceneEmbedOptions,
  includeCredentials: boolean,
): Array<[string, string]> {
  return sceneAttributes(options, includeCredentials)
    .map(([name, value]): [string, string] => [name, value === true ? 'true' : value]);
}

function serializeTheme(theme: HonuaMapThemeOptions | null | undefined): string | null {
  const declarations = Object.entries(mapThemeVariables(theme))
    .filter((entry): entry is [string, string] => typeof entry[1] === 'string')
    .map(([property, value]) => `${property}: ${value}`);

  return declarations.length === 0 ? null : declarations.join('; ');
}

function mapThemeVariables(theme: HonuaMapThemeOptions | null | undefined): Record<string, string | null | undefined> {
  if (theme === null) {
    return {
      '--honua-map-accent': null,
      '--honua-map-background': null,
      '--honua-map-foreground': null,
      '--honua-map-muted': null,
      '--honua-map-surface': null,
      '--honua-map-border': null,
      '--honua-map-font-family': null,
      '--honua-map-control-size': null,
    };
  }

  return {
    '--honua-map-accent': theme?.accent,
    '--honua-map-background': theme?.background,
    '--honua-map-foreground': theme?.foreground,
    '--honua-map-muted': theme?.muted,
    '--honua-map-surface': theme?.surface,
    '--honua-map-border': theme?.border,
    '--honua-map-font-family': theme?.fontFamily,
    '--honua-map-control-size': theme?.controlSize,
  };
}

function mapQueryParameters(
  options: HonuaMapEmbedOptions,
  includeCredentials: boolean,
): Array<[string, string]> {
  const parameters = mapAttributes(options, includeCredentials)
    .map(([name, value]): [string, string] => [name, value === true ? 'true' : value]);

  for (const [property, value] of Object.entries(mapThemeVariables(options.style))) {
    if (typeof value === 'string' && value !== '') {
      parameters.push([property, value]);
    }
  }

  return parameters;
}

function reactStyleObject(theme: HonuaMapThemeOptions | null | undefined): string | null {
  const declarations = Object.entries(mapThemeVariables(theme))
    .filter((entry): entry is [string, string] => typeof entry[1] === 'string')
    .map(([property, value]) => `'${escapeJsString(property)}': '${escapeJsString(value)}'`);

  if (declarations.length === 0) {
    return null;
  }

  return `{ ${declarations.join(', ')} }`;
}

function reactIframeAttributeName(name: string): string {
  if (name === 'class') {
    return 'className';
  }

  if (name === 'referrerpolicy') {
    return 'referrerPolicy';
  }

  return name;
}

function indentBlock(value: string, indent: string): string {
  return value
    .split('\n')
    .map((line) => `${indent}${line}`)
    .join('\n');
}

function indentTemplateLiteral(value: string, indent: string): string {
  return value
    .replaceAll('\\', '\\\\')
    .replaceAll('`', '\\`')
    .replaceAll('${', '\\${')
    .split('\n')
    .map((line) => `${indent}${indent}${line}`)
    .join('\n');
}

function setOptionalAttribute(element: HTMLElement, name: string, value: string | null | undefined): void {
  if (value === undefined) {
    return;
  }

  if (value === null || value === '') {
    element.removeAttribute(name);
    return;
  }

  element.setAttribute(name, value);
}

function setBooleanAttribute(element: HTMLElement, name: string, value: boolean | null | undefined): void {
  if (value === undefined) {
    return;
  }

  if (value) {
    element.setAttribute(name, '');
    return;
  }

  element.removeAttribute(name);
}

function setAutoloadAttribute(element: HTMLElement, value: boolean | null | undefined): void {
  if (value === undefined) {
    return;
  }

  if (value === null) {
    element.removeAttribute('autoload');
    return;
  }

  if (value) {
    element.setAttribute('autoload', '');
    return;
  }

  element.setAttribute('autoload', 'false');
}

function serializeBoolean(value: boolean | null | undefined): true | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  return value ? true : null;
}

function serializeAutoload(value: boolean | null | undefined): string | true | null | undefined {
  if (value === undefined || value === null) {
    return value;
  }

  return value ? true : 'false';
}

function serializeList(value: readonly string[] | null | undefined): string | null | undefined {
  if (value === undefined) {
    return undefined;
  }

  if (value === null) {
    return null;
  }

  const serialized = value
    .map((item) => item.trim())
    .filter(Boolean)
    .join(',');
  return serialized || null;
}

function serializeCoordinate(value: HonuaMapCoordinate | null | undefined): string | null | undefined {
  if (value === undefined || value === null) {
    return value;
  }

  return `${value.latitude},${value.longitude}`;
}

function serializeBounds(value: HonuaMapBounds | null | undefined): string | null | undefined {
  if (value === undefined || value === null) {
    return value;
  }

  return `${value.minLongitude},${value.minLatitude},${value.maxLongitude},${value.maxLatitude}`;
}

function serializeNumber(value: number | null | undefined): string | null | undefined {
  if (value === undefined || value === null) {
    return value;
  }

  return String(value);
}

function serializeSandbox(value: string | readonly string[] | null | undefined): string | null | undefined {
  if (isReadonlyStringArray(value)) {
    return value
      .map((item) => item.trim())
      .filter(Boolean)
      .join(' ') || null;
  }

  return value;
}

function serializeAttributeValue(value: string | number | null | undefined): string | null | undefined {
  if (value === undefined || value === null) {
    return value;
  }

  return String(value);
}

function isAbsoluteUrl(value: string): boolean {
  return /^[a-z][a-z0-9+.-]*:/i.test(value);
}

function isProtocolRelativeUrl(value: string): boolean {
  return value.startsWith('//');
}

function isReadonlyStringArray(value: unknown): value is readonly string[] {
  return Array.isArray(value);
}

function escapeHtmlAttribute(value: string | true): string {
  return String(value)
    .replaceAll('&', '&amp;')
    .replaceAll('"', '&quot;')
    .replaceAll('<', '&lt;')
    .replaceAll('>', '&gt;');
}

function escapeJsString(value: string): string {
  return value
    .replaceAll('\\', '\\\\')
    .replaceAll('\'', '\\\'')
    .replaceAll('<', '\\x3C')
    .replaceAll('\n', '\\n')
    .replaceAll('\r', '\\r');
}

function assertCustomElementName(name: string): void {
  if (!/^[a-z][.0-9_a-z-]*-[.0-9_a-z-]*$/.test(name)) {
    throw new Error(`Invalid custom element name: ${name}`);
  }
}

function assertJsIdentifier(name: string): void {
  if (!/^[$A-Z_a-z][$\w]*$/.test(name) || JS_RESERVED_WORDS.has(name)) {
    throw new Error(`Invalid JavaScript identifier: ${name}`);
  }
}

const JS_RESERVED_WORDS = new Set([
  'await',
  'break',
  'case',
  'catch',
  'class',
  'const',
  'continue',
  'debugger',
  'default',
  'delete',
  'do',
  'else',
  'enum',
  'export',
  'extends',
  'false',
  'finally',
  'for',
  'function',
  'if',
  'implements',
  'import',
  'in',
  'instanceof',
  'interface',
  'let',
  'new',
  'null',
  'package',
  'private',
  'protected',
  'public',
  'return',
  'static',
  'super',
  'switch',
  'this',
  'throw',
  'true',
  'try',
  'typeof',
  'var',
  'void',
  'while',
  'with',
  'yield',
]);
