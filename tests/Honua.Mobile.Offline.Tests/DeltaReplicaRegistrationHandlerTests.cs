using System.Net;
using Honua.Mobile.Offline.Sync;

namespace Honua.Mobile.Offline.Tests;

public sealed class DeltaReplicaRegistrationHandlerTests
{
    [Theory]
    [InlineData("rest/services/assets/FeatureServer/createReplica", "perLayer", "perReplica")]
    [InlineData("rest/services/assets/FeatureServer/createReplica", "perReplica", "perReplica")]
    [InlineData("rest/services/assets/FeatureServer/createReplica", "none", "none")]
    [InlineData("rest/services/assets/FeatureServer/synchronizeReplica", "perLayer", "perLayer")]
    [InlineData("rest/services/FeatureServer/createReplica", "perLayer", "perLayer")]
    [InlineData("other/assets/FeatureServer/createReplica", "perLayer", "perLayer")]
    [InlineData("rest/services/assets/FeatureServer/createReplica?syncModel=perLayer", "perLayer", "perLayer")]
    public async Task RegistrationRewritesOnlyTheSdkModelField(string path, string model, string expected)
    {
        var transport = new RecordingHandler();
        using var handler = new DeltaReplicaRegistrationHandler { InnerHandler = transport };
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/gateway/") };
        using var body = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["replicaName"] = "東京 &syncModel=perLayer + one",
            ["syncModel"] = model,
            ["layers"] = "0,1",
            ["f"] = "json",
        });
        using var expectedBody = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["replicaName"] = "東京 &syncModel=perLayer + one",
            ["syncModel"] = expected,
            ["layers"] = "0,1",
            ["f"] = "json",
        });

        using var response = await http.PostAsync(path, body);
        Assert.Equal(await expectedBody.ReadAsStringAsync(), transport.Body);
    }

    [Fact]
    public async Task RegistrationHonorsCancellation()
    {
        var transport = new RecordingHandler();
        using var handler = new DeltaReplicaRegistrationHandler { InnerHandler = transport };
        using var http = new HttpClient(handler);
        using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["syncModel"] = "perLayer" });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            http.PostAsync("https://example.test/rest/services/assets/FeatureServer/createReplica", body, new CancellationToken(true)));
        Assert.Null(transport.Body);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
