using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using XipHub.XipFax.Examples;
using XipHub.XipFax.Sdk.Client;
using XipHub.XipFax.Sdk.Model;
using Xunit;

namespace XipHub.XipFax.Sdk.Tests;

/// <summary>
/// The quickstart, walked against a stand-in of the API that answers as the document says: its free
/// steps first, its charged step said before it and taken only on send, and each request it sends
/// read against the document, as each answer the stand-in gives is. Its pause waits for nothing, so a
/// walk takes no time, and no walk reaches XipFax.
/// </summary>
public sealed class QuickstartSpecs
{
    private const string Key = "xipfax_key_live_example";
    private const string FaxNumber = "+1 202 555 0143";
    private const string PdfLink = "https://example.com/letter.pdf";

    /// <summary>The send the quickstart makes: the number it names, and the one document it added.</summary>
    private static readonly Dictionary<string, object?> ItsSend = new Dictionary<string, object?>(StringComparer.Ordinal)
    {
        ["fax_number"] = FaxNumber,
        ["document_ids"] = new List<object?> { "doc_1" },
    };

    private static bool Asks(string happened) => happened.StartsWith("ask: ", StringComparison.Ordinal);

    /// <summary>
    /// The API as the quickstart finds it, its draft holding documents to begin with, and
    /// addedOnXipFax too once the quickstart adds its own, as documents the client adds on xipfax.com
    /// meanwhile, each by its id.
    /// </summary>
    private sealed class Api
    {
        private readonly Contract contract;
        private readonly List<string> documents;
        private readonly IReadOnlyList<string> addedOnXipFax;
        private int reads;

        public Api(Contract contract, IEnumerable<string> draft, IReadOnlyList<string> addedOnXipFax)
        {
            this.contract = contract;
            documents = draft.ToList();
            this.addedOnXipFax = addedOnXipFax;
        }

        /// <summary>Each request, line and question of the walk, in its order.</summary>
        public List<string> Events { get; } = new List<string>();

        /// <summary>What the document refuses in the stand-in's answers: nothing, where it answers as it says.</summary>
        public List<string> Refused { get; } = new List<string>();

        /// <summary>Answers a request as the API would, and reads the answer against the document.</summary>
        public (int Status, string Answer) Answer(SentRequest request)
        {
            var route = request.Method + " " + request.Uri.AbsolutePath;
            Events.Add(route);
            var (status, answer) = Route(route, request.Body);
            Refused.AddRange(contract.ReadAnswer(request, status, answer).Select(refused => $"the answer to {route}: {refused}"));
            return (status, answer);
        }

        private (int Status, string Answer) Route(string route, byte[] body)
        {
            switch (route)
            {
                case "GET /quote":
                    return (200, Answers.Quote);
                case "GET /offices":
                    return (200, "{\"data\":[" + Answers.Office + "],\"has_more\":false,\"next_cursor\":null}");
                case "GET /draft":
                    return (200, "{\"id\":\"fax_1\",\"documents\":["
                        + string.Join(",", documents.Select(id => Answers.Document(id, "letter.pdf"))) + "]}");
                case "POST /draft/documents":
                    documents.Add("doc_1");
                    documents.AddRange(addedOnXipFax);
                    return (201, "{\"document\":" + Answers.Document("doc_1", "letter.pdf") + ",\"upload\":null}");
                case "DELETE /draft/documents/doc_1":
                    documents.Remove("doc_1");
                    return (204, "");
                case "POST /draft/send":
                    // As the API does, it sends a draft only where the send names its documents, in order.
                    if (!NamesThem(body))
                    {
                        return (409, Answers.Refusal(
                            "draft_changed", "Your fax changed since this page showed it. Look it over, then send it again."));
                    }
                    return (200, "{\"fax\":"
                        + Answers.Fax(new Dictionary<string, object?> { ["status"] = "sending", ["step"] = "queued", ["finished_at"] = null })
                        + ",\"paid_by\":\"plan\",\"confirm_url\":null}");
                case "GET /faxes/fax_1":
                    reads++;
                    return reads < 2
                        ? (200, Answers.Fax(new Dictionary<string, object?> { ["status"] = "sending", ["step"] = "sending", ["finished_at"] = null }))
                        : (200, Answers.Fax());
                case "GET /faxes/fax_1/confirmation":
                    return (200, "{\"url\":\"https://example.com/confirmation.pdf\"}");
                default:
                    return (404, "");
            }
        }

        /// <summary>Whether a send's body names the draft's documents, in their order.</summary>
        private bool NamesThem(byte[] body)
        {
            object? sent;
            try
            {
                sent = Json.Parse(body);
            }
            catch (System.Text.Json.JsonException)
            {
                return false;
            }
            return Json.Get(sent as Dictionary<string, object?>, "document_ids") is List<object?> ids
                && ids.SequenceEqual(documents.Cast<object?>());
        }
    }

    /// <summary>
    /// What a walk saw: each request, line and question in its order, the requests themselves, and the
    /// API's refusal where one stopped it.
    /// </summary>
    private sealed record Walk(List<string> Events, IReadOnlyList<SentRequest> Requests, ApiException? Refused)
    {
        /// <summary>The walk's requests alone, by their routes.</summary>
        public List<string> Routes() =>
            Events.Where(happened => !happened.StartsWith("say: ", StringComparison.Ordinal) && !Asks(happened)).ToList();

        /// <summary>The body of the walk's one POST /draft/send, in plain values.</summary>
        public object? Sent() =>
            Json.Parse(Assert.Single(Requests, request => request.Method == "POST" && request.Uri.AbsolutePath == "/draft/send").Body);
    }

    /// <summary>
    /// Walks the quickstart against the API, its draft holding draft and gaining addedOnXipFax, typed
    /// being what you type when it asks, and reads each request it sent, and each answer it was given,
    /// against the document.
    /// </summary>
    private static async Task<Walk> WalkOf(string[] draft, string[] addedOnXipFax, string typed, string? to, string? pdfUrl)
    {
        var contract = Contract.Load();
        var api = new Api(contract, draft, addedOnXipFax);
        var standIn = new StandIn(api.Answer);
        var input = new QuickstartInput(
            to,
            pdfUrl,
            line => api.Events.Add("say: " + line),
            question =>
            {
                api.Events.Add("ask: " + question);
                return typed;
            },
            (_, _) => Task.CompletedTask);

        ApiException? refused = null;
        using (var http = standIn.Client())
        {
            try
            {
                await Quickstart.RunAsync(http, new XipFaxConfiguration(Key), input, TestContext.Current.CancellationToken);
            }
            catch (ApiException refusal)
            {
                refused = refusal;
            }
        }

        Assert.Empty(api.Refused);
        foreach (var request in standIn.Requests())
        {
            var (_, inRequest) = contract.ReadRequest(request, ApiVersion.Value);
            Assert.True(inRequest.Count == 0, $"{request.Method} {request.Uri.AbsoluteUri}: {Text.QuoteAll(inRequest)}");
        }
        return new Walk(api.Events, standIn.Requests(), refused);
    }

    [Fact]
    public async Task TheQuickstartTakesTheFreeStepsAloneWhereItHasNoNumberOrPdf()
    {
        var walk = await WalkOf(Array.Empty<string>(), Array.Empty<string>(), "send", null, null);

        Assert.Null(walk.Refused);
        Assert.Equal(new[] { "GET /quote", "GET /offices" }, walk.Routes());
        Assert.DoesNotContain(walk.Events, Asks);
    }

    [Fact]
    public async Task TheQuickstartSaysTheFaxIsRealAndChargedSendsItOnlyOnceSendIsTypedAndFollowsIt()
    {
        var walk = await WalkOf(Array.Empty<string>(), Array.Empty<string>(), "send", FaxNumber, PdfLink);

        Assert.Null(walk.Refused);
        Assert.Equal(
            new[]
            {
                "GET /quote",
                "GET /offices",
                "GET /draft",
                "POST /draft/documents",
                "POST /draft/send",
                "GET /faxes/fax_1",
                "GET /faxes/fax_1",
                "GET /faxes/fax_1/confirmation",
            },
            walk.Routes());
        var added = walk.Events.IndexOf("POST /draft/documents");
        var warned = walk.Events.IndexOf("say: " + Quickstart.RealAndCharged);
        var asked = walk.Events.FindIndex(Asks);
        var sent = walk.Events.IndexOf("POST /draft/send");
        Assert.True(
            added >= 0 && warned > added && asked > warned && sent > asked,
            $"it added at {added}, warned at {warned}, asked at {asked} and sent at {sent}: {Text.QuoteAll(walk.Events)}");
        Assert.Contains("say: Added letter.pdf, 2 pages.", walk.Events);

        Assert.True(Json.Same(ItsSend, walk.Sent()), "the send was " + Text.Of(walk.Sent()));
        var posts = walk.Requests.Where(request => request.Method == "POST").ToList();
        var keys = posts.Select(request => Assert.Single(request.Values("Idempotency-Key"))).Distinct(StringComparer.Ordinal);
        Assert.Equal(posts.Count, keys.Count());
    }

    [Fact]
    public async Task TheQuickstartSendsNothingOnAnyOtherAnswerAndTakesItsDocumentBackOutOfTheDraft()
    {
        var walk = await WalkOf(Array.Empty<string>(), Array.Empty<string>(), "no", FaxNumber, PdfLink);

        Assert.Null(walk.Refused);
        Assert.Equal(
            new[] { "GET /quote", "GET /offices", "GET /draft", "POST /draft/documents", "DELETE /draft/documents/doc_1" },
            walk.Routes());
    }

    [Fact]
    public async Task TheQuickstartNamesOnlyTheDocumentItAddedSoADraftThatGainedAnotherOnXipFaxIsRefused()
    {
        var walk = await WalkOf(Array.Empty<string>(), new[] { "doc_2" }, "send", FaxNumber, PdfLink);

        var refused = walk.Refused;
        Assert.NotNull(refused);
        Assert.Equal(409, refused.ErrorCode);
        var error = JsonConvert.DeserializeObject<ApiError>(Assert.IsType<string>(refused.ErrorContent));
        Assert.NotNull(error);
        Assert.Equal("draft_changed", error.Error.Code);
        Assert.Equal(
            new[] { "GET /quote", "GET /offices", "GET /draft", "POST /draft/documents", "POST /draft/send" },
            walk.Routes());
        Assert.True(Json.Same(ItsSend, walk.Sent()), "the send was " + Text.Of(walk.Sent()));
    }

    [Fact]
    public async Task TheQuickstartAddsNothingToADraftThatAlreadyHoldsDocuments()
    {
        var walk = await WalkOf(new[] { "doc_0" }, Array.Empty<string>(), "send", FaxNumber, PdfLink);

        Assert.Null(walk.Refused);
        Assert.Equal(new[] { "GET /quote", "GET /offices", "GET /draft" }, walk.Routes());
    }

    /// <summary>Links, each with the name the quickstart gives its PDF.</summary>
    public static TheoryData<string, string> Links() => new TheoryData<string, string>
    {
        { "https://example.com/letter.pdf", "letter.pdf" },
        { "https://example.com/forms/Form%202848.pdf", "Form 2848.pdf" },
        { "https://example.com/forms/", "forms" },
        { "https://example.com", "document.pdf" },
        { "https://example.com/%FF.pdf", "%FF.pdf" },
        { "https://example.com/" + new string('é', 300), new string('é', 255) },
    };

    [Theory]
    [MemberData(nameof(Links))]
    public void TheDocumentIsNamedAsItsLinkEnds(string link, string name)
    {
        Assert.Equal(name, Quickstart.NameOf(link));
    }
}
