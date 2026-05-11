// @vitest-environment node
import { describe, expect, it } from 'vitest';

describe('server-side package imports', () => {
  it('can import the root package without browser globals', async () => {
    expect(globalThis.document).toBeUndefined();
    expect(globalThis.HTMLElement).toBeUndefined();

    const embed = await import('../src/index');

    expect(embed.HonuaMapElement).toBeTypeOf('function');
    expect(embed.HonuaSceneElement).toBeTypeOf('function');
    expect(embed.defineHonuaMapElement).toBeTypeOf('function');
    expect(embed.defineHonuaSceneElement).toBeTypeOf('function');
  });

  it('reports a clear error when defining elements without a custom element registry', async () => {
    const { defineHonuaMapElement } = await import('../src/map');

    expect(() => defineHonuaMapElement()).toThrow(/requires a browser DOM/);
  });
});
