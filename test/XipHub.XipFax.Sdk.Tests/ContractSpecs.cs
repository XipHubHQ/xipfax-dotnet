using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using XipHub.XipFax.Sdk.Api;
using XipHub.XipFax.Sdk.Model;
using Xunit;

namespace XipHub.XipFax.Sdk.Tests;

/// <summary>
/// Each of the SDK's methods sends the request the API's document asks for: its method and path on
/// the document's server, the key as a bearer token, the version, and every parameter and body as the
/// document's schemas take them. The calls are typed by the SDK, so they compile only as the generated
/// code allows, and each request they send is read against the document itself, as each answer the
/// stand-in gives is. Each event the document posts is typed by the SDK as FaxEvent, which the
/// document takes as the SDK writes it. The reader is held to failing too: each thing the document
/// refuses in a request or an answer is refused, by name, so that a spec reading the SDK's requests as
/// fitting can fail.
/// </summary>
public sealed class ContractSpecs
{
    private const string Key = "xipfax_key_live_example";

    /// <summary>One call of each operation, by its operation id, with arguments the document takes.</summary>
    private static readonly Dictionary<string, Func<HttpClient, XipFaxConfiguration, CancellationToken, Task>> Calls = new()
    {
        ["getQuote"] = async (http, settings, cancel) =>
        {
            using var api = new QuoteApi(http, settings);
            await api.GetQuoteAsync(3, cancel);
        },
        ["getDraft"] = async (http, settings, cancel) =>
        {
            using var api = new DraftApi(http, settings);
            await api.GetDraftAsync(cancel);
        },
        ["addDocument"] = async (http, settings, cancel) =>
        {
            using var api = new DraftApi(http, settings);
            await api.AddDocumentAsync(
                "add-letter-1", new NewDocument(name: "letter.pdf", url: "https://example.com/letter.pdf"), cancel);
        },
        ["completeDocument"] = async (http, settings, cancel) =>
        {
            using var api = new DraftApi(http, settings);
            await api.CompleteDocumentAsync("complete-letter-1", "doc_1", cancel);
        },
        ["removeDocument"] = async (http, settings, cancel) =>
        {
            using var api = new DraftApi(http, settings);
            await api.RemoveDocumentAsync("doc_1", cancel);
        },
        ["sendDraft"] = async (http, settings, cancel) =>
        {
            using var api = new DraftApi(http, settings);
            await api.SendDraftAsync(
                "send-letter-1",
                new SendDraft(faxNumber: "+1 202 555 0143", documentIds: new List<string> { "doc_1" }),
                cancel);
        },
        ["listFaxes"] = async (http, settings, cancel) =>
        {
            using var api = new FaxesApi(http, settings);
            await api.ListFaxesAsync(limit: 20, startingAfter: "fax_0", direction: "sent", cancellationToken: cancel);
        },
        ["getFax"] = async (http, settings, cancel) =>
        {
            using var api = new FaxesApi(http, settings);
            await api.GetFaxAsync("fax_1", cancel);
        },
        ["getConfirmation"] = async (http, settings, cancel) =>
        {
            using var api = new FaxesApi(http, settings);
            await api.GetConfirmationAsync("fax_1", cancel);
        },
        ["getPlan"] = async (http, settings, cancel) =>
        {
            using var api = new PlanApi(http, settings);
            await api.GetPlanAsync(cancel);
        },
        ["listOffices"] = async (http, settings, cancel) =>
        {
            using var api = new OfficesApi(http, settings);
            await api.ListOfficesAsync(
                limit: 5, startingAfter: "office_0", search: "Form 2848", place: "OH", cancellationToken: cancel);
        },
        ["registerWebhookEndpoint"] = async (http, settings, cancel) =>
        {
            using var api = new WebhookEndpointsApi(http, settings);
            await api.RegisterWebhookEndpointAsync(
                "register-endpoint-1", new NewWebhookEndpoint(url: "https://example.com/xipfax/events"), cancel);
        },
        ["removeWebhookEndpoint"] = async (http, settings, cancel) =>
        {
            using var api = new WebhookEndpointsApi(http, settings);
            await api.RemoveWebhookEndpointAsync("endpoint_1", cancel);
        },
    };

    /// <summary>
    /// What the stand-in answers each operation's success with, by its operation id: none for the two
    /// that answer 204.
    /// </summary>
    private static readonly Dictionary<string, string> AnswerOf = new()
    {
        ["getQuote"] = Answers.Quote,
        ["getDraft"] = "{\"id\":\"fax_1\",\"documents\":[]}",
        ["addDocument"] = "{\"document\":" + Answers.Document("doc_1", "letter.pdf") + ",\"upload\":null}",
        ["completeDocument"] = Answers.Document("doc_1", "letter.pdf"),
        ["sendDraft"] = "{\"fax\":"
            + Answers.Fax(new Dictionary<string, object?> { ["status"] = "sending", ["step"] = "queued", ["finished_at"] = null })
            + ",\"paid_by\":\"plan\",\"confirm_url\":null}",
        ["listFaxes"] = "{\"data\":[" + Answers.Fax() + "],\"has_more\":false,\"next_cursor\":null}",
        ["getFax"] = Answers.Fax(),
        ["getConfirmation"] = "{\"url\":\"https://example.com/confirmation.pdf\"}",
        ["getPlan"] = Answers.NoPlan,
        ["listOffices"] = "{\"data\":[" + Answers.Office + "],\"has_more\":false,\"next_cursor\":null}",
        ["registerWebhookEndpoint"] = "{\"id\":\"endpoint_1\",\"url\":\"https://example.com/xipfax/events\","
            + "\"created_at\":\"2026-11-07T02:00:00Z\",\"secret\":\"whsec_c3RhbmQtaW4=\"}",
    };

    /// <summary>The id of each operation the document names, a spec of each.</summary>
    public static TheoryData<string> OperationIds()
    {
        var ids = new TheoryData<string>();
        foreach (var operation in Contract.Load().Operations)
        {
            ids.Add(operation.Id);
        }
        return ids;
    }

    /// <summary>The key and the version, signed as the SDK signs each request, and more headers.</summary>
    private static Dictionary<string, string[]> Signed(params (string Name, string Value)[] more)
    {
        var headers = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = new[] { "Bearer " + Key },
            ["Xip-Version"] = new[] { ApiVersion.Value },
        };
        foreach (var (name, value) in more)
        {
            headers[name] = new[] { value };
        }
        return headers;
    }

    [Fact]
    public void TheSdkHasACallForEveryOperationTheDocumentNamesAndNoOther()
    {
        var ids = Contract.Sorted(Contract.Load().Operations.Select(operation => operation.Id));

        Assert.Equal(ids, Contract.Sorted(Calls.Keys));
    }

    [Fact]
    public void TheSdkNamesTheVersionTheDocumentIsTheOneXipVersionTakes()
    {
        var contract = Contract.Load();

        Assert.Equal(contract.Version, ApiVersion.Value);
        foreach (var operation in contract.Operations)
        {
            Assert.Empty(contract.VersionRefused(operation, ApiVersion.Value));
        }
    }

    [Theory]
    [MemberData(nameof(OperationIds))]
    public async Task EachCallSendsItsRequestAsTheDocumentAsks(string operationId)
    {
        var contract = Contract.Load();
        var operation = contract.Operations.Single(each => each.Id == operationId);
        Assert.True(Calls.TryGetValue(operationId, out var call), $"The specs make no call of {operationId}.");
        var answer = AnswerOf.TryGetValue(operationId, out var given) ? given : "";
        var standIn = StandIn.Same(operation.Success, answer);

        using (var http = standIn.Client())
        {
            await call(http, new XipFaxConfiguration(Key), TestContext.Current.CancellationToken);
        }

        var request = Assert.Single(standIn.Requests());
        Assert.Equal("Bearer " + Key, Assert.Single(request.Values("Authorization")));
        var (id, refused) = contract.ReadRequest(request, ApiVersion.Value);
        Assert.Equal(operationId, id);
        Assert.Empty(refused);
        Assert.Empty(contract.ReadAnswer(request, operation.Success, answer));
    }

    [Fact]
    public void EachEventTheDocumentPostsIsAFaxEventItTakes()
    {
        var contract = Contract.Load();
        // The fax each event tells of, by the event's type: one for each type the document posts.
        var faxes = new Dictionary<string, string>
        {
            ["fax.delivered"] = Answers.Fax(new Dictionary<string, object?>
            {
                ["price"] = new Dictionary<string, object?> { ["amount_minor"] = 100, ["currency"] = "USD" },
            }),
            ["fax.failed"] = Answers.Fax(new Dictionary<string, object?> { ["status"] = "failed", ["failure"] = "busy" }),
            ["fax.received"] = Answers.Fax(new Dictionary<string, object?>
            {
                ["id"] = "fax_2",
                ["direction"] = "received",
                ["status"] = "received",
                ["from_number"] = "+12025550111",
                ["to_number"] = "+12025550100",
                ["pages"] = 3,
                ["finished_at"] = null,
                ["time_zone"] = null,
            }),
        };
        Assert.Equal(Contract.Sorted(contract.Webhooks.Keys), Contract.Sorted(faxes.Keys));

        foreach (var (name, fax) in faxes)
        {
            var data = JsonConvert.DeserializeObject<Fax>(fax);
            Assert.NotNull(data);
            var written = JsonConvert.SerializeObject(
                new FaxEvent(id: "event_1", type: name, apiVersion: ApiVersion.Value, createdAt: "2026-11-07T02:03:00Z", data: data));

            Assert.Empty(contract.Check(contract.Webhooks[name], Json.Parse(System.Text.Encoding.UTF8.GetBytes(written)), name));
            var read = JsonConvert.DeserializeObject<FaxEvent>(written);
            Assert.Equal(data.Id, read?.Data?.Id);
        }
    }

    [Fact]
    public void TheReaderTakesARequestThatFits()
    {
        var request = SentRequest.Of("GET", "https://api.xipfax.com/quote?pages=3", Signed(), "");

        var (id, refused) = Contract.Load().ReadRequest(request, ApiVersion.Value);

        Assert.Equal("getQuote", id);
        Assert.Empty(refused);
    }

    [Fact]
    public void TheReaderRefusesNoKeyTheVersionTwiceAPagePastTheMostAndAParameterItDoesntTake()
    {
        var headers = new Dictionary<string, string[]> { ["Xip-Version"] = new[] { ApiVersion.Value, ApiVersion.Value } };
        var request = SentRequest.Of("GET", "https://api.xipfax.com/quote?pages=251&color=red", headers, "");

        var (id, refused) = Contract.Load().ReadRequest(request, ApiVersion.Value);

        Assert.Equal("getQuote", id);
        Assert.Equal(
            new[]
            {
                "Authorization is [], not one bearer key",
                $"Xip-Version is [\"{ApiVersion.Value}\" \"{ApiVersion.Value}\"], not {ApiVersion.Value}",
                "header Xip-Version is sent 2 times",
                "query pages is 251, past its maximum of 250",
                "query color is no parameter of getQuote",
            },
            refused);
    }

    [Fact]
    public void TheReaderRefusesABodyWithAMemberItDoesntTakeASizeUnderTheLeastAndNoName()
    {
        var request = SentRequest.Of(
            "POST",
            "https://api.xipfax.com/draft/documents",
            Signed(("Content-Type", "application/json"), ("Idempotency-Key", "add-letter-1")),
            "{\"size_bytes\":0,\"colour\":\"red\"}");

        var (id, refused) = Contract.Load().ReadRequest(request, ApiVersion.Value);

        Assert.Equal("addDocument", id);
        Assert.Equal(
            new[] { "body.colour is no member it takes", "body.size_bytes is 0, past its minimum of 1", "body.name is missing" },
            refused);
    }

    [Fact]
    public void TheReaderReadsAPatternsEndAsTheEndOfTheText()
    {
        // .NET's $ also matches before a last line break, where the document's pattern doesn't: a name
        // that ends in one is refused.
        var request = SentRequest.Of(
            "POST",
            "https://api.xipfax.com/draft/documents",
            Signed(("Content-Type", "application/json"), ("Idempotency-Key", "add-letter-1")),
            "{\"name\":\"letter.pdf\\n\"}");

        var (id, refused) = Contract.Load().ReadRequest(request, ApiVersion.Value);

        Assert.Equal("addDocument", id);
        Assert.Equal(
            new[] { "body.name is \"letter.pdf\\n\", not of the pattern ^[^\\p{Cc}\\p{Zl}\\p{Zp}]+$" },
            refused);
    }

    [Fact]
    public void TheReaderRefusesABodyWhereTheOperationTakesNone()
    {
        var request = SentRequest.Of("DELETE", "https://api.xipfax.com/draft/documents/doc_1", Signed(), "{}");

        var (id, refused) = Contract.Load().ReadRequest(request, ApiVersion.Value);

        Assert.Equal("removeDocument", id);
        Assert.Equal(new[] { "a body the operation takes none of" }, refused);
    }

    [Fact]
    public void TheReaderRefusesARequestToAnotherServer()
    {
        var request = SentRequest.Of("GET", "https://api.example.com/quote?pages=3", Signed(), "");

        var (id, refused) = Contract.Load().ReadRequest(request, ApiVersion.Value);

        Assert.Equal("", id);
        Assert.Equal(new[] { "GET https://api.example.com/quote?pages=3 is no operation of the document" }, refused);
    }

    [Fact]
    public void TheReaderRefusesAFaxWhosePagesAreWordsAndWhoseStatusTheDocumentDoesntList()
    {
        var request = SentRequest.Of("GET", "https://api.xipfax.com/faxes/fax_1", new Dictionary<string, string[]>(), "");
        var answer = Answers.Fax(new Dictionary<string, object?> { ["pages"] = "two", ["status"] = "archived" });

        Assert.Equal(
            new[]
            {
                "answer.pages is string, not [integer null]",
                "answer.status is archived, none of [draft sending delivered failed received]",
            },
            Contract.Load().ReadAnswer(request, 200, answer));
    }

    [Fact]
    public void TheReaderRefusesAStatusTheOperationDoesntGive()
    {
        var request = SentRequest.Of("GET", "https://api.xipfax.com/plan", new Dictionary<string, string[]>(), "");

        Assert.Equal(new[] { "getPlan gives no 418" }, Contract.Load().ReadAnswer(request, 418, ""));
    }

    [Fact]
    public void TheReaderRefusesABodyWhereTheAnswerCarriesNone()
    {
        var request = SentRequest.Of("DELETE", "https://api.xipfax.com/draft/documents/doc_1", new Dictionary<string, string[]>(), "");

        Assert.Equal(new[] { "removeDocument's 204 carries no body" }, Contract.Load().ReadAnswer(request, 204, "{}"));
    }

    [Fact]
    public void TheReaderRefusesASchemaKeywordItDoesntRead()
    {
        var schema = new Dictionary<string, object?>
        {
            ["oneOf"] = new List<object?> { new Dictionary<string, object?> { ["type"] = "string" } },
        };

        Assert.Equal(
            new[] { "value: the contract reads no oneOf, which its schema gives" },
            Contract.Load().Check(schema, "text", "value"));
    }
}
