using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Newtonsoft.Json;
using XipHub.XipFax.Sdk.Api;
using XipHub.XipFax.Sdk.Client;
using XipHub.XipFax.Sdk.Model;
using Xunit;

namespace XipHub.XipFax.Sdk.Tests;

/// <summary>
/// What XipFaxConfiguration, the SDK's hand-written part, makes each request carry, and what the SDK
/// makes of an answer it didn't expect.
/// </summary>
public sealed class ConfigurationSpecs
{
    /// <summary>The one request GET /plan sends with the settings, to a stand-in of its own.</summary>
    private static async Task<SentRequest> PlanRequestOf(XipFaxConfiguration settings)
    {
        var standIn = StandIn.Same(200, Answers.NoPlan);
        using (var http = standIn.Client())
        using (var api = new PlanApi(http, settings))
        {
            await api.GetPlanAsync(TestContext.Current.CancellationToken);
        }
        return Assert.Single(standIn.Requests());
    }

    [Fact]
    public void ItRefusesToStartWithoutAKey()
    {
        foreach (var apiKey in new[] { null, "", "   " })
        {
            var refused = Assert.Throws<ArgumentException>(() => new XipFaxConfiguration(apiKey));
            Assert.Equal("apiKey", refused.ParamName);
        }
    }

    [Fact]
    public void ItRefusesAnAddressThatIsntHttpOrHttps()
    {
        foreach (var basePath in new[] { "", "api.example.com", "ftp://api.example.com" })
        {
            var refused = Assert.Throws<ArgumentException>(() => new XipFaxConfiguration("key-a", basePath));
            Assert.Equal("basePath", refused.ParamName);
        }
    }

    [Fact]
    public async Task ItSendsToTheApisOwnAddressUnlessGivenAnother()
    {
        var own = await PlanRequestOf(new XipFaxConfiguration("key-a"));
        Assert.Equal("https://api.xipfax.com/plan", own.Uri.AbsoluteUri);

        var other = await PlanRequestOf(new XipFaxConfiguration("key-a", "https://api.example.com/"));
        Assert.Equal("https://api.example.com/plan", other.Uri.AbsoluteUri);
    }

    [Fact]
    public async Task EachClientKeepsItsKeyToItself()
    {
        var first = new XipFaxConfiguration("key-a");
        var second = new XipFaxConfiguration("key-b");

        var a = await PlanRequestOf(first);
        var b = await PlanRequestOf(second);

        Assert.Equal("Bearer key-a", Assert.Single(a.Values("Authorization")));
        Assert.Equal("Bearer key-b", Assert.Single(b.Values("Authorization")));
    }

    [Fact]
    public async Task EachRequestNamesTheVersionTheSdkIsGeneratedFrom()
    {
        var request = await PlanRequestOf(new XipFaxConfiguration("key-a"));

        Assert.Equal(ApiVersion.Value, Assert.Single(request.Values("Xip-Version")));
    }

    [Fact]
    public async Task EachRequestNamesTheSdkAndItsRelease()
    {
        var request = await PlanRequestOf(new XipFaxConfiguration("key-a"));

        Assert.Equal("xipfax-dotnet/" + Configuration.Version, Assert.Single(request.Values("User-Agent")));
    }

    [Fact]
    public async Task ARefusalIsTheApisOwnError()
    {
        var refusal = Answers.Refusal("authentication_failed", "That key isn’t one of XipFax’s.");
        var standIn = StandIn.Same(401, refusal);
        using var http = standIn.Client();
        using var api = new PlanApi(http, new XipFaxConfiguration("key-a"));

        var refused = await Assert.ThrowsAsync<ApiException>(() => api.GetPlanAsync(TestContext.Current.CancellationToken));

        Assert.Equal(401, refused.ErrorCode);
        var error = JsonConvert.DeserializeObject<ApiError>(Assert.IsType<string>(refused.ErrorContent));
        Assert.NotNull(error);
        Assert.Equal("authentication_failed", error.Error.Code);
        Assert.Equal("req_1", error.Error.RequestId);
    }

    [Fact]
    public async Task AnAnswerThatGainsAMemberIsReadAndCarriesItThrough()
    {
        // The document's Additions rule: within a version, an answer may gain a member, and an open set
        // a new value. Here the fax gains tracking, and its status is one the SDK has never seen.
        var gained = Answers.Fax(new Dictionary<string, object?>
        {
            ["status"] = "archived",
            ["tracking"] = new Dictionary<string, object?> { ["carrier"] = "stand-in" },
        });
        var standIn = StandIn.Same(200, gained);
        using var http = standIn.Client();
        using var api = new FaxesApi(http, new XipFaxConfiguration("key-a"));

        var fax = await api.GetFaxAsync("fax_1", TestContext.Current.CancellationToken);

        Assert.Equal("archived", fax.Status);
        using var carried = JsonDocument.Parse(fax.ToJson());
        Assert.Equal("stand-in", carried.RootElement.GetProperty("tracking").GetProperty("carrier").GetString());
    }
}
