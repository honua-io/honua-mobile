export const HonuaHTMLElementBase: typeof HTMLElement =
  typeof HTMLElement === 'undefined'
    ? (class {} as unknown as typeof HTMLElement)
    : HTMLElement;

export function createHonuaTemplate(html: string): HTMLTemplateElement {
  if (typeof document === 'undefined') {
    return {
      content: {
        cloneNode: () => {
          throw new Error('Honua embed elements require a browser DOM.');
        },
      },
      innerHTML: html,
    } as unknown as HTMLTemplateElement;
  }

  const template = document.createElement('template');
  template.innerHTML = html;
  return template;
}

export function canDefineHonuaCustomElements(): boolean {
  return typeof customElements !== 'undefined'
    && typeof document !== 'undefined'
    && typeof HTMLElement !== 'undefined';
}

export function assertHonuaDomAvailable(name: string): void {
  if (!canDefineHonuaCustomElements()) {
    throw new Error(`${name} requires a browser DOM with custom elements support.`);
  }
}

export function defineHonuaCustomElement(
  name: string,
  constructor: CustomElementConstructor,
): CustomElementConstructor {
  assertHonuaDomAvailable(name);

  const existing = customElements.get(name);
  if (existing) {
    return existing;
  }

  customElements.define(name, constructor);
  return constructor;
}
