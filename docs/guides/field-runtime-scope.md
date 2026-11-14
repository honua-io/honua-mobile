# Field runtime package scope

The mobile packages concentrate on native location and capture, encrypted
offline storage, sync and display integration. The unused native AR anchoring,
plugin host and mobile routing facade are removed as part of #421. No reference
app or field template consumed those APIs. Their dedicated tests are retired
with them; field storage, sync, location and display coverage remains.

This is a breaking alpha API change. Remove `AddHonuaRouting`,
`AddHonuaNativeSceneAnchoring` and `AddHonuaMapPluginHost` registrations.
Applications that need routing can reference the published
`Honua.Sdk.GeoServices` package and configure its routing client directly.
Mobile client routing options and current-location routing helpers are removed;
applications supply their own location when calling the standalone SDK.
No replacement AR runtime or plugin loader is included.

Historical AR and plugin design documents remain for context and do not describe
the shipped field packages. The browser embed package has a separate transfer
tracked in #425. Scene metadata and package storage are unchanged by this change.

Validate the remaining SDK, Offline and MAUI library suites and formatting on the
build host. Platform builds and the Collect physical-phone journey remain
separate acceptance evidence; this cleanup does not claim that journey passed.
