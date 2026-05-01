export * from './map';
export * from './scene';
export * from './scene-package-cache';
export * from './display-adapter';

import { defineHonuaMapElement } from './map';
import { defineHonuaSceneElement } from './scene';

if (typeof customElements !== 'undefined') {
  defineHonuaMapElement();
  defineHonuaSceneElement();
}
