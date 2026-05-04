import type { HonuaSceneElement } from '../scene';
import type {
  HonuaSceneBookmarkMetadata,
  HonuaSceneViewSpec,
} from '../scene-metadata';
import { HonuaSceneLink } from './scene-link';
import {
  CONTROL_BASE_STYLES,
  controlTemplate,
  upgradeProperty,
} from './shared';

export interface HonuaSceneBookmarkApplyDetail {
  bookmarkId: string;
  view: HonuaSceneViewSpec;
  controlId: string;
}

const ELEMENT_NAME = 'honua-scene-bookmarks';
const CONTROL_ID = 'bookmarks';

const template = controlTemplate(`
  ${CONTROL_BASE_STYLES}
  <style>
    .bookmark {
      width: 100%;
      text-align: left;
    }

    .bookmark-title {
      font-weight: 500;
    }

    .bookmark-description {
      color: var(--honua-control-muted);
      font-size: 12px;
    }

    .empty {
      color: var(--honua-control-muted);
      font-style: italic;
    }
  </style>
  <section class="control" part="control">
    <header part="header">
      <h2 class="title" part="title">Bookmarks</h2>
    </header>
    <div class="body" part="body">
      <p class="empty" data-empty>No bookmarks for this scene yet.</p>
      <ul class="list" part="list" hidden></ul>
    </div>
  </section>
`);

export class HonuaSceneBookmarksElement extends HTMLElement {
  static get observedAttributes(): string[] {
    return ['for', 'heading'];
  }

  readonly #root: ShadowRoot;
  readonly #link: HonuaSceneLink;

  constructor() {
    super();
    this.#root = this.attachShadow({ mode: 'open' });
    this.#root.append(template.content.cloneNode(true));
    this.#link = new HonuaSceneLink({
      host: this,
      controlId: CONTROL_ID,
      onSceneChange: () => this.#render(),
    });
  }

  connectedCallback(): void {
    upgradeProperty(this, 'sceneSelector');
    this.#applyHeading();
    this.#link.connect();
    this.#render();
  }

  disconnectedCallback(): void {
    this.#link.disconnect();
  }

  attributeChangedCallback(name: string): void {
    if (name === 'for') {
      this.#link.refresh('attribute');
      return;
    }

    if (name === 'heading') {
      this.#applyHeading();
    }
  }

  get scene(): HonuaSceneElement | null {
    return this.#link.scene;
  }

  apply(bookmarkId: string): boolean {
    const scene = this.#link.scene;
    const bookmark = scene?.metadata?.bookmarks?.find((entry) => entry.id === bookmarkId);
    if (!scene || !bookmark) {
      this.#link.emitError('bookmark-not-found', `Unknown bookmark "${bookmarkId}".`);
      return false;
    }

    scene.applyView(bookmark.view);
    this.dispatchEvent(new CustomEvent<HonuaSceneBookmarkApplyDetail>('honua-scene-bookmark-apply', {
      bubbles: true,
      composed: true,
      detail: { bookmarkId, view: bookmark.view, controlId: CONTROL_ID },
    }));
    return true;
  }

  refresh(): void {
    this.#render();
  }

  #applyHeading(): void {
    const heading = this.getAttribute('heading');
    const node = this.#root.querySelector<HTMLElement>('.title');
    if (node) {
      node.textContent = heading ?? 'Bookmarks';
    }
  }

  #render(): void {
    const list = this.#root.querySelector<HTMLUListElement>('.list')!;
    const empty = this.#root.querySelector<HTMLElement>('.empty')!;
    list.replaceChildren();

    const bookmarks = this.#link.scene?.metadata?.bookmarks ?? [];
    if (bookmarks.length === 0) {
      list.hidden = true;
      empty.hidden = false;
      return;
    }

    list.hidden = false;
    empty.hidden = true;

    for (const bookmark of bookmarks) {
      list.append(this.#renderBookmark(bookmark));
    }
  }

  #renderBookmark(bookmark: HonuaSceneBookmarkMetadata): HTMLLIElement {
    const item = document.createElement('li');
    item.dataset.bookmarkId = bookmark.id;

    const button = document.createElement('button');
    button.type = 'button';
    button.className = 'bookmark';
    button.dataset.bookmarkId = bookmark.id;

    const title = document.createElement('div');
    title.className = 'bookmark-title';
    title.textContent = bookmark.title;
    button.append(title);

    if (bookmark.description) {
      const description = document.createElement('div');
      description.className = 'bookmark-description';
      description.textContent = bookmark.description;
      button.append(description);
    }

    button.addEventListener('click', () => this.apply(bookmark.id));
    item.append(button);
    return item;
  }
}

export function defineHonuaSceneBookmarksElement(name = ELEMENT_NAME): CustomElementConstructor {
  const existing = customElements.get(name);
  if (existing) {
    return existing;
  }
  customElements.define(name, HonuaSceneBookmarksElement);
  return HonuaSceneBookmarksElement;
}

declare global {
  interface HTMLElementTagNameMap {
    'honua-scene-bookmarks': HonuaSceneBookmarksElement;
  }
}
