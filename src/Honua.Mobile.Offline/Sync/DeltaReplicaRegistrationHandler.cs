// Copyright (c) Honua, Inc. and contributors.
// Licensed under the Apache License, Version 2.0. See the LICENSE file in the repository root.

using System.Text;

namespace Honua.Mobile.Offline.Sync;

/// <summary>
/// Requests replica-wide generations for new GeoServices replicas used by
/// <see cref="DeltaDownloadEngine"/>, which persists one generation per service.
/// </summary>
/// <remarks>
/// Configure this handler on the dedicated HTTP client passed to the SDK replica client.
/// The SDK reads a replica-wide generation but some releases request per-layer generations.
/// Existing replicas and subsequent requests keep their original cursor semantics.
/// </remarks>
public sealed class DeltaReplicaRegistrationHandler : DelegatingHandler
{
    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Method == HttpMethod.Post &&
            request.RequestUri?.AbsolutePath.EndsWith("/FeatureServer/createReplica", StringComparison.OrdinalIgnoreCase) == true &&
            request.Content is FormUrlEncodedContent originalContent)
        {
            var fields = (await originalContent.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)).Split('&');
            var modelIndex = Array.IndexOf(fields, "syncModel=perLayer");
            if (modelIndex >= 0)
            {
                fields[modelIndex] = "syncModel=perReplica";
                using var content = new StringContent(string.Join('&', fields), Encoding.UTF8, "application/x-www-form-urlencoded");
                request.Content = content;
                try
                {
                    return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    request.Content = originalContent;
                }
            }
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
