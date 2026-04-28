export * from './map';
export * from './scene';

import { defineHonuaMapElement } from './map';
import { defineHonuaSceneElement } from './scene';

if (typeof customElements !== 'undefined') {
  defineHonuaMapElement();
  defineHonuaSceneElement();
}
