# Embeddable Map Component

`@honua/embed` provides a framework-agnostic `<honua-map>` custom element for ISV and SaaS integrations.

```html
<script type="module">
  import '@honua/embed';
</script>

<honua-map
  service-url="https://services.honua.example/FeatureServer"
  layer-ids="assets,work-orders"
  center="21.3069,-157.8583"
  zoom="12"
  interactive
  search
  identify>
</honua-map>
```

The component is white-label by default: it does not render Honua branding unless an integrator provides their own attribution. Host applications can style it with CSS custom properties without leaking styles into the map internals.

## Integration Events

```js
const map = document.querySelector('honua-map');

map.addEventListener('honua-map-search', (event) => {
  console.log(event.detail.query);
});

map.addEventListener('honua-map-identify', (event) => {
  console.log(event.detail.x, event.detail.y);
});
```

## First Slice Scope

This initial package establishes the web component API, Shadow DOM encapsulation, declarative attributes, theme hooks, accessible controls, and test coverage. Follow-on work can add a production map renderer, feature loading, generated embed snippets, analytics, and framework-specific wrappers.
