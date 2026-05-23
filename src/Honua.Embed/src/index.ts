export * from './map';
export * from './scene';
export * from './scene-metadata';
export * from './scene-package-cache';
export * from './display-adapter';
export * from './builder';
export * from './builder-element';
export type {
  HonuaEmbedConfigByTarget,
  HonuaEmbedContribution,
  HonuaEmbedControlOptions,
  HonuaEmbedExtension,
  HonuaEmbedExtensionCleanup,
  HonuaEmbedExtensionContext,
  HonuaEmbedExtensionDescriptor,
  HonuaEmbedExtensionErrorDetail,
  HonuaEmbedExtensionPermission,
  HonuaEmbedExtensionRegistration,
  HonuaEmbedExtensionRegistrationOptions,
  HonuaEmbedExtensionTrustState,
  HonuaEmbedTarget,
} from './extensions';
export {
  listHonuaEmbedExtensions,
  registerHonuaEmbedExtension,
} from './extensions';
export * from './snippets';
export * from './controls';

import { defineHonuaMapElement } from './map';
import { defineHonuaSceneElement } from './scene';
import { defineHonuaSceneControls } from './controls';
import { defineHonuaMapBuilderElement } from './builder-element';
import { canDefineHonuaCustomElements } from './dom';

if (canDefineHonuaCustomElements()) {
  defineHonuaMapElement();
  defineHonuaSceneElement();
  defineHonuaSceneControls();
  defineHonuaMapBuilderElement();
}
