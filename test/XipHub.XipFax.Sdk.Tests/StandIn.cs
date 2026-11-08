using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XipHub.XipFax.Sdk.Tests;

/// <summary>A request the stand-in was given, its headers and its body as they were sent.</summary>
internal sealed class SentRequest
{
    private readonly IReadOnlyDictionary<string, List<string>> headers;

    public SentRequest(string method, Uri uri, IReadOnlyDictionary<string, List<string>> headers, byte[] body)
    {
        Method = method;
        Uri = uri;
        this.headers = headers;
        Body = body;
    }

    public string Method { get; }

    public Uri Uri { get; }

    public byte[] Body { get; }

    /// <summary>The values a header was sent with, in their order: none where it wasn't sent.</summary>
    public IReadOnlyList<string> Values(string name) =>
        headers.TryGetValue(name, out var values) ? values : Array.Empty<string>();

    /// <summary>A request of method to address, with headers and body as they are given.</summary>
    public static SentRequest Of(string method, string address, IDictionary<string, string[]> headers, string body)
    {
        var kept = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var header in headers)
        {
            kept[header.Key] = header.Value.ToList();
        }
        return new SentRequest(method, new Uri(address), kept, Encoding.UTF8.GetBytes(body));
    }
}

/// <summary>
/// Answers the SDK's requests in place of the API, in the specs: the handler of an HttpClient, which
/// keeps each request it is given, with its body, and answers it as its answer says. Nothing it does
/// reaches a network.
/// </summary>
internal sealed class StandIn : HttpMessageHandler
{
    private readonly Func<SentRequest, (int Status, string Answer)> answer;
    private readonly object gate = new object();
    private readonly List<SentRequest> requests = new List<SentRequest>();

    /// <param name="answer">
    /// The status and the JSON body the stand-in answers a request with; an empty body answers none.
    /// </param>
    public StandIn(Func<SentRequest, (int Status, string Answer)> answer)
    {
        this.answer = answer;
    }

    /// <summary>A stand-in that answers every request with one status and one body.</summary>
    public static StandIn Same(int status, string answer) => new StandIn(_ => (status, answer));

    /// <summary>An HttpClient whose every request goes to the stand-in, which outlives it.</summary>
    public HttpClient Client() => new HttpClient(this, disposeHandler: false);

    /// <summary>Each request the stand-in was given, in its order.</summary>
    public IReadOnlyList<SentRequest> Requests()
    {
        lock (gate)
        {
            return requests.ToList();
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[] body = request.Content == null
            ? Array.Empty<byte>()
            : await request.Content.ReadAsByteArrayAsync(cancellationToken);

        var headers = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Keep(IEnumerable<KeyValuePair<string, IEnumerable<string>>> sent)
        {
            foreach (var header in sent)
            {
                if (!headers.TryGetValue(header.Key, out var values))
                {
                    values = new List<string>();
                    headers[header.Key] = values;
                }
                values.AddRange(header.Value);
            }
        }
        Keep(request.Headers);
        if (request.Content != null)
        {
            Keep(request.Content.Headers);
        }

        var kept = new SentRequest(
            request.Method.Method,
            request.RequestUri ?? throw new InvalidOperationException("The SDK sent a request with no address."),
            headers,
            body);
        lock (gate)
        {
            requests.Add(kept);
        }

        var (status, text) = answer(kept);
        return new HttpResponseMessage((HttpStatusCode)status)
        {
            RequestMessage = request,
            Content = text.Length > 0
                ? new StringContent(text, Encoding.UTF8, "application/json")
                : new ByteArrayContent(Array.Empty<byte>()),
        };
    }
}
