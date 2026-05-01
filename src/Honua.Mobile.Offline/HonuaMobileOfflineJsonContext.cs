using System.Text.Json;
using System.Text.Json.Serialization;
using Honua.Mobile.Offline.GeoPackage;
using Honua.Mobile.Offline.Sync;
using Honua.Sdk.Abstractions.Scenes;
using Honua.Sdk.Offline.Abstractions;

namespace Honua.Mobile.Offline;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(BoundingBox))]
[JsonSerializable(typeof(HonuaSceneBounds))]
[JsonSerializable(typeof(OfflineOperationPayload))]
[JsonSerializable(typeof(OfflineSyncCheckpoint))]
[JsonSerializable(typeof(OfflineSyncState))]
[JsonSerializable(typeof(string[]))]
internal sealed partial class HonuaMobileOfflineJsonContext : JsonSerializerContext;

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(HonuaScenePackageAsset))]
[JsonSerializable(typeof(HonuaScenePackageByteBudget))]
[JsonSerializable(typeof(HonuaScenePackageLod))]
[JsonSerializable(typeof(HonuaScenePackageManifest))]
internal sealed partial class HonuaMobileScenePackageJsonContext : JsonSerializerContext;

internal static class HonuaMobileOfflineJson
{
    public static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, typeof(T), HonuaMobileOfflineJsonContext.Default);

    public static T? Deserialize<T>(string json)
        => (T?)JsonSerializer.Deserialize(json, typeof(T), HonuaMobileOfflineJsonContext.Default);
}
