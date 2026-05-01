export * from './map';
export * from './scene';
export * from './scene-package-cache';
export * from './display-adapter';
export type {
  HonuaEmbedConfigByTarget,
  HonuaEmbedContribution,
  HonuaEmbedControlOptions,
  HonuaEmbedExtension,
  HonuaEmbedExtensionCleanup,
  HonuaEmbedExtensionContext,
  HonuaEmbedExtensionDescriptor,
  HonuaEmbedExtensionErrorDetail,
  HonuaEmbedExtensionRegistration,
  HonuaEmbedTarget,
} from './extensions';
export {
  listHonuaEmbedExtensions,
  registerHonuaEmbedExtension,
} from './extensions';
export * from './snippets';

import { defineHonuaMapElement } from './map';
import { defineHonuaSceneElement } from './scene';

if (typeof customElements !== 'undefined') {
  defineHonuaMapElement();
  defineHonuaSceneElement();
}
