# XipFax for .NET

Send faxes from your code with [XipFax](https://xipfax.com): quote a fax, add your PDFs, send them to a
number or a government office, and follow each fax until it's delivered. The SDK is generated from
the API's own document, so its methods and types are the API's. It's a .NET Standard 2.0 library, so
it runs on every .NET from .NET Core 2.0 and on .NET Framework 4.6.1 or later, and it takes
Newtonsoft.Json and Polly.

## Install

```sh
dotnet add package XipHub.XipFax.Sdk
```

## Make an API key

Sign in to the [developer console](https://developer.xipfax.com/console/), open API keys and make a
key. You see it whole just once, so copy it then. Every key is live: what it sends is real.

Keep the key on your server. A key in a web page or an app is anyone's.

## Use it

```csharp
using System;
using System.Net.Http;
using Newtonsoft.Json;
using XipHub.XipFax.Sdk;
using XipHub.XipFax.Sdk.Api;
using XipHub.XipFax.Sdk.Client;
using XipHub.XipFax.Sdk.Model;

var settings = new XipFaxConfiguration(Environment.GetEnvironmentVariable("XIPFAX_API_KEY"));
using var http = new HttpClient();
var quotes = new QuoteApi(http, settings);

try
{
    var quote = await quotes.GetQuoteAsync(3);
    Console.WriteLine($"{quote.PriceShown}: {quote.Terms}");
}
catch (ApiException refused) when (refused.ErrorContent is string body)
{
    var error = JsonConvert.DeserializeObject<ApiError>(body)?.Error;
    Console.Error.WriteLine($"{refused.ErrorCode} {error?.Code}: {error?.Message}");
}
```

`XipFaxConfiguration` holds your key, and each request carries it. Make each API from it and one
`HttpClient`, which every API can share: the SDK's APIs are the
[API reference](https://developer.xipfax.com/docs/api/)'s sections, `QuoteApi`, `DraftApi`,
`FaxesApi`, `PlanApi`, `OfficesApi` and `WebhookEndpointsApi`. Each method takes what its request
carries as its arguments, `GetQuoteAsync(3)` or `AddDocumentAsync(idempotencyKey, document)`, and a
`CancellationToken` last. Each has a form that waits, `GetQuote(3)`, and a form that answers the
status and the headers too, `GetQuoteWithHttpInfoAsync(3)`.

Where the API refuses, the method throws an `ApiException`: its `ErrorCode` is the status, and its
`ErrorContent` the answer's body, which `JsonConvert.DeserializeObject<ApiError>` reads as the API's
error object, whose `Error.Code` is the one to branch on. A request that never reached XipFax throws
what `HttpClient` threw.

`new XipFaxConfiguration(apiKey, basePath)` sends to another address than
`https://api.xipfax.com`.

## The quickstart

[`examples/Quickstart`](https://github.com/XipHubHQ/xipfax-dotnet/tree/main/examples/Quickstart)
takes the steps of [XipFax's quickstart](https://developer.xipfax.com/docs/guides/quickstart/)
through the SDK. Run it from a clone of this repository, with the .NET 10 SDK:

```sh
XIPFAX_API_KEY=xipfax_key_live_… dotnet run --project examples/Quickstart
```

That takes the free steps alone: what a fax costs, and the offices that take Form 2848 from Ohio,
with their fax numbers.

To send a fax too, name a fax number of yours and a public https link to a PDF:

```sh
XIPFAX_TO='+1 202 555 0143' XIPFAX_PDF_URL=https://example.com/letter.pdf \
  XIPFAX_API_KEY=xipfax_key_live_… dotnet run --project examples/Quickstart
```

**That step sends a real fax, and it's charged.** Its pages count on your plan, or where you have no
plan its price is held on the card you saved, and taken once it's delivered. A fax that fails costs
nothing. The quickstart says so before it sends, and sends only once you type `send`. Then it
follows the fax until it's delivered, and gives the link to its confirmation.

## Methods

| Method                                             | Request                                        | What it does                                                                         |
| -------------------------------------------------- | ---------------------------------------------- | ------------------------------------------------------------------------------------ |
| `QuoteApi.GetQuoteAsync`                           | `GET /quote`                                   | The price of a fax of so many pages, sent without a plan.                            |
| `DraftApi.GetDraftAsync`                           | `GET /draft`                                   | Your draft, the fax you prepare, its documents in the order it sends them.           |
| `DraftApi.AddDocumentAsync`                        | `POST /draft/documents`                        | Adds a PDF to the draft, by a link XipFax fetches or by an upload.                   |
| `DraftApi.CompleteDocumentAsync`                   | `POST /draft/documents/{document_id}/complete` | Reads an uploaded PDF back, its pages counted.                                       |
| `DraftApi.RemoveDocumentAsync`                     | `DELETE /draft/documents/{document_id}`        | Takes a document out of the draft.                                                   |
| `DraftApi.SendDraftAsync`                          | `POST /draft/send`                             | Sends the draft to a number or an office.                                            |
| `FaxesApi.ListFaxesAsync`                          | `GET /faxes`                                   | The faxes you sent and received, newest first, a page at a time.                     |
| `FaxesApi.GetFaxAsync`                             | `GET /faxes/{fax_id}`                          | A fax, where it stands now.                                                          |
| `FaxesApi.GetConfirmationAsync`                    | `GET /faxes/{fax_id}/confirmation`             | A delivered fax's confirmation, by a link that reads it for a few minutes.           |
| `PlanApi.GetPlanAsync`                             | `GET /plan`                                    | Your plan, or none.                                                                  |
| `OfficesApi.ListOfficesAsync`                      | `GET /offices`                                 | The government offices XipFax gives the fax numbers of.                              |
| `WebhookEndpointsApi.RegisterWebhookEndpointAsync` | `POST /webhook-endpoints`                      | Registers an endpoint of your server for every event, its signing secret shown once. |
| `WebhookEndpointsApi.RemoveWebhookEndpointAsync`   | `DELETE /webhook-endpoints/{endpoint_id}`      | Takes an endpoint away: no event goes to it from then on.                            |

### Upload a PDF from your disk

Give its size, send its bytes to the link the answer gives, then complete it:

```csharp
var pdf = await File.ReadAllBytesAsync("letter.pdf");
var drafts = new DraftApi(http, settings);
var added = await drafts.AddDocumentAsync(addKey, new NewDocument(name: "letter.pdf", sizeBytes: pdf.Length));
if (added.Upload == null)
{
    throw new InvalidOperationException("no link to upload to: the request was sent again with its key");
}

using var put = new HttpRequestMessage(HttpMethod.Put, added.Upload.Url) { Content = new ByteArrayContent(pdf) };
foreach (var header in added.Upload.Headers)
{
    if (!put.Headers.TryAddWithoutValidation(header.Key, header.Value))
    {
        put.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
    }
}
using var uploaded = await http.SendAsync(put);
uploaded.EnsureSuccessStatusCode();

await drafts.CompleteDocumentAsync(completeKey, added.Document.Id);
```

`addKey` and `completeKey` are keys of your own, one for each request, as the next section says. The
upload goes with no key of yours: the SDK puts your key on its own requests alone, never on the
`HttpClient`.

## Retries and `Idempotency-Key`

`AddDocumentAsync`, `CompleteDocumentAsync`, `SendDraftAsync` and `RegisterWebhookEndpointAsync`
take an `Idempotency-Key` of your own, such as a GUID, which you keep with the request. Sent again
with the same key within 24 hours, the request is answered as it was the first time rather than done
twice, so a retry after a timeout can't send a fax twice. The SDK makes up no key itself: one it made
would be new on each try, and protect nothing.

The answer to a request sent again keeps none of its signed links, nor an endpoint's secret:
`Upload`, a document's `Url` and an endpoint's `Secret` come back null. So an upload that
`AddDocumentAsync` gave, asked for again with its key, has no link to send the bytes to. Take that
document out with `RemoveDocumentAsync`, and add it again with a new key. An endpoint whose first
answer you lost has no secret to check its deliveries by: remove it with
`RemoveWebhookEndpointAsync`, and register it again with a new key.

## Webhooks

`RegisterWebhookEndpointAsync` registers an endpoint of your server, which every event goes to from
then on: when a fax you sent is delivered or fails for good, and when a fax sent to your number
arrives. Its answer shows the endpoint's signing secret once, so keep it then.
`RemoveWebhookEndpointAsync` takes an endpoint away, as revoking the key that registered it does.
Both take an API key, not an app's token.

Check each delivery's `webhook-signature` with that secret, against its body exactly as it came,
before you trust it. [XipFax's webhooks guide](https://developer.xipfax.com/docs/guides/webhooks/)
shows how. Its body is then a `FaxEvent`, which `JsonConvert.DeserializeObject<FaxEvent>` reads.

## Versions

Each request names the version of the API the SDK was generated from, `ApiVersion.Value`, in its
`Xip-Version` header, so each answer keeps the shape the SDK's types describe. Within that version,
an answer may gain a member, and a set of values a new value: the SDK reads both, and keeps a member
it doesn't know in the model's `AdditionalProperties`.

## Where the SDK comes from

`openapi.json` is the API's document, as [api.xipfax.com](https://api.xipfax.com/openapi.json) gives
it, and `scripts/generate.sh` makes the SDK from it with
[openapi-generator](https://openapi-generator.tech) 7.17.0's C# generator, its `httpclient` library,
and `openapi-generator.yaml`. It first writes the document the SDK is generated from, by
`scripts/sdk-document.jq`, which sets aside what the SDK sends itself or doesn't offer: the
document's security, since `XipFaxConfiguration` sends your key on every request, and an app's token
is not the SDK's; the per-request `Xip-Version`, since `XipFaxConfiguration` names the version on
every request; and the webhooks, which are XipFax's requests to your server, their bodies typed as
`FaxEvent`. Nothing the generator writes is edited by hand: `XipFaxConfiguration.cs` is the
hand-written part, and `templates/` adds the repository's own settings to the project the generator
writes. To update the SDK, copy the new document over `openapi.json`, run `scripts/generate.sh`, and
commit what changes. `scripts/gates.sh` checks that the SDK is what the document generates, then
builds it with its specs and the quickstart, and packs it; `scripts/test.sh` runs the specs, which
walk the quickstart against a stand-in of the API. Both run in the .NET SDK's container, with no
network, after `scripts/fetch.sh` has fetched the packages.

## Help

Write to [help@xipfax.com](mailto:help@xipfax.com).

## License

[MIT](https://github.com/XipHubHQ/xipfax-dotnet/blob/main/LICENSE).
