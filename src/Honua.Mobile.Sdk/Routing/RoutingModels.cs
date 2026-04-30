using Honua.Sdk.Abstractions.Routing;
using Honua.Sdk.GeoServices.Routing;

namespace Honua.Mobile.Sdk.Routing;

/// <summary>
/// Supplies the current device location to routing builders or callers.
/// Platform packages can implement this with MAUI/Android/iOS GPS APIs.
/// </summary>
public interface IRoutingLocationProvider
{
    /// <summary>
    /// Resolves the current device location.
    /// </summary>
    ValueTask<RoutingLocation> GetCurrentLocationAsync(CancellationToken ct = default);
}

/// <summary>
/// Mobile-only helpers for pairing SDK routing with platform location providers.
/// </summary>
public static class HonuaRoutingClientMobileExtensions
{
    /// <summary>
    /// Gets directions from the current location resolved by <paramref name="locationProvider"/>.
    /// </summary>
    public static async Task<RouteResult> GetDirectionsFromCurrentLocationAsync(
        this IHonuaRoutingClient client,
        IRoutingLocationProvider locationProvider,
        RoutingLocation destination,
        IReadOnlyList<RoutingLocation>? waypoints = null,
        RouteSolveOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(locationProvider);
        ArgumentNullException.ThrowIfNull(destination);

        var origin = await locationProvider.GetCurrentLocationAsync(ct).ConfigureAwait(false);
        return await client.GetDirectionsAsync(
            new RouteDirectionsRequest
            {
                Origin = origin,
                Destination = destination,
                Waypoints = waypoints,
                Options = options ?? new RouteSolveOptions(),
            },
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the route origin from the current location resolved by <paramref name="locationProvider"/>.
    /// </summary>
    public static async Task<RouteDirectionsBuilder> FromCurrentLocationAsync(
        this RouteDirectionsBuilder builder,
        IRoutingLocationProvider locationProvider,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(locationProvider);

        var origin = await locationProvider.GetCurrentLocationAsync(ct).ConfigureAwait(false);
        return builder.From(origin);
    }
}
