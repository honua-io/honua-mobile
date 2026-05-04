export {
  HonuaSceneLayersElement,
  defineHonuaSceneLayersElement,
  type HonuaSceneLayerOpacityDetail,
  type HonuaSceneLayerToggleDetail,
} from './layers';
export {
  HonuaSceneBookmarksElement,
  defineHonuaSceneBookmarksElement,
  type HonuaSceneBookmarkApplyDetail,
} from './bookmarks';
export {
  HonuaSceneTimelineElement,
  defineHonuaSceneTimelineElement,
  type HonuaSceneTimelineChangeDetail,
} from './timeline';
export {
  HonuaSceneCompareElement,
  defineHonuaSceneCompareElement,
  type HonuaSceneCompareSetDetail,
} from './compare';
export {
  HonuaSceneInspectorElement,
  defineHonuaSceneInspectorElement,
  type HonuaSceneFeatureSelectDetail,
} from './inspector';
export {
  HonuaSceneMeasureElement,
  defineHonuaSceneMeasureElement,
  type HonuaSceneMeasurementAddDetail,
  type HonuaSceneMeasurementClearDetail,
  type HonuaSceneMeasurementKind,
  type HonuaSceneMeasurementPoint,
} from './measure';
export {
  HonuaSceneLink,
  type HonuaSceneControlErrorDetail,
  type HonuaSceneLinkChangeReason,
  type HonuaSceneLinkOptions,
} from './scene-link';

import { defineHonuaSceneBookmarksElement } from './bookmarks';
import { defineHonuaSceneCompareElement } from './compare';
import { defineHonuaSceneInspectorElement } from './inspector';
import { defineHonuaSceneLayersElement } from './layers';
import { defineHonuaSceneMeasureElement } from './measure';
import { defineHonuaSceneTimelineElement } from './timeline';

export function defineHonuaSceneControls(): void {
  defineHonuaSceneLayersElement();
  defineHonuaSceneBookmarksElement();
  defineHonuaSceneTimelineElement();
  defineHonuaSceneCompareElement();
  defineHonuaSceneInspectorElement();
  defineHonuaSceneMeasureElement();
}
