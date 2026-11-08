using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XipHub.XipFax.Sdk;
using XipHub.XipFax.Sdk.Api;
using XipHub.XipFax.Sdk.Model;

namespace XipHub.XipFax.Examples;

/// <summary>What the quickstart is given, and how it talks to you.</summary>
/// <param name="To">A fax number of yours, where the fax goes. Without it, only the free steps run.</param>
/// <param name="PdfUrl">
/// A public https link to a PDF, the fax's pages. Without it, only the free steps run.
/// </param>
/// <param name="Say">Tells you a line.</param>
/// <param name="Ask">Asks you a question, answering what you typed, or null where nothing was.</param>
/// <param name="Pause">Waits between reads of the fax.</param>
internal sealed record QuickstartInput(
    string? To,
    string? PdfUrl,
    Action<string> Say,
    Func<string, string?> Ask,
    Func<TimeSpan, CancellationToken, Task> Pause);

/// <summary>
/// XipFax's quickstart through the SDK, the steps of
/// https://developer.xipfax.com/docs/guides/quickstart/ in their order. The free steps come first: a
/// quote, and the offices that take a form. Then, only where you name a fax number of yours and a
/// PDF, a real fax, charged, which it says before it sends, and sends only once you type send.
/// Program.cs runs it in your terminal.
/// </summary>
internal static class Quickstart
{
    /// <summary>How long the quickstart waits between reads of the fax.</summary>
    public static readonly TimeSpan ReadEvery = TimeSpan.FromSeconds(10);

    /// <summary>How many reads of the fax it makes, at most.</summary>
    public const int Reads = 30;

    /// <summary>What the step that sends says first, as the docs' quickstart says it.</summary>
    public const string RealAndCharged =
        "This step sends a real fax, and it’s charged. It goes to the number you name, so name one of your own. "
        + "Its pages count on your plan, or where you have no plan its price is held on the card you saved, "
        + "and taken once it’s delivered. A fax that fails costs nothing.";

    /// <summary>
    /// Takes the quickstart's steps, each request sent by http with the settings. The API's refusal
    /// stops it, thrown as the SDK's ApiException, whose ErrorContent is the API's error, which
    /// Program.cs prints.
    /// </summary>
    public static async Task RunAsync(
        HttpClient http, XipFaxConfiguration settings, QuickstartInput input, CancellationToken cancellationToken)
    {
        using var quotes = new QuoteApi(http, settings);
        using var offices = new OfficesApi(http, settings);
        using var draft = new DraftApi(http, settings);
        using var faxes = new FaxesApi(http, settings);

        // 1. What a fax costs. This costs nothing, and it shows your key works.
        var quote = await quotes.GetQuoteAsync(1, cancellationToken);
        input.Say($"A one-page fax costs {quote.PriceShown} without a plan: {quote.Terms}");

        // 2. Where a form goes: the offices that take Form 2848 from Ohio, and their numbers. Also free.
        var found = await offices.ListOfficesAsync(
            limit: 3, search: "Form 2848", place: "OH", cancellationToken: cancellationToken);
        foreach (var office in found.Data)
        {
            input.Say($"{office.Name} takes Form 2848 at {office.FaxNumber}.");
        }

        if (string.IsNullOrEmpty(input.To) || string.IsNullOrEmpty(input.PdfUrl))
        {
            return;
        }

        // 3. Your draft, the fax you prepare. This sends only its own document, so it starts empty.
        var before = await draft.GetDraftAsync(cancellationToken);
        if (before.Documents.Count > 0)
        {
            input.Say("Your draft already holds documents. Send or remove them on xipfax.com first, so that this sends only its own.");
            return;
        }

        // 4. Add the PDF by its link. Each POST names a key of its own, so a retry can't add it twice.
        var added = await draft.AddDocumentAsync(
            NewKey(), new NewDocument(name: NameOf(input.PdfUrl), url: input.PdfUrl), cancellationToken);
        input.Say($"Added {added.Document.Name}{PagesOf(added.Document)}.");

        // 5. Send it, a real fax, charged: said first, and sent only once you type send.
        input.Say(RealAndCharged);
        var answer = input.Ask($"Type send to fax it to {input.To}, or anything else to stop: ");
        if (!string.Equals(answer?.Trim(), "send", StringComparison.OrdinalIgnoreCase))
        {
            await draft.RemoveDocumentAsync(added.Document.Id, cancellationToken);
            input.Say("Nothing was sent, and the document is out of your draft again.");
            return;
        }
        // The send names the documents to send: the one this added, alone. A draft whose documents
        // aren't these is refused, so that nothing you didn't name is sent.
        var sent = await draft.SendDraftAsync(
            NewKey(),
            new SendDraft(faxNumber: input.To, documentIds: new List<string> { added.Document.Id }),
            cancellationToken);
        if (!string.IsNullOrEmpty(sent.ConfirmUrl))
        {
            input.Say("Nothing was sent or held: the fax waits for you on xipfax.com, " + sent.ConfirmUrl);
            return;
        }
        input.Say($"Sent, paid by {sent.PaidBy}: fax {sent.Fax.Id}.");

        // 6. Follow the fax until it's delivered or fails, then read its confirmation.
        var fax = sent.Fax;
        var shown = "";
        for (var read = 0; read < Reads && fax.Status != "delivered" && fax.Status != "failed"; read++)
        {
            await input.Pause(ReadEvery, cancellationToken);
            fax = await faxes.GetFaxAsync(fax.Id, cancellationToken);
            var stands = string.IsNullOrEmpty(fax.Step) ? fax.Status : fax.Status + ": " + fax.Step;
            if (stands != shown)
            {
                input.Say(stands);
            }
            shown = stands;
        }
        switch (fax.Status)
        {
            case "delivered":
            {
                var confirmation = await faxes.GetConfirmationAsync(fax.Id, cancellationToken);
                input.Say("Its confirmation, readable for a few minutes: " + confirmation.Url);
                break;
            }
            case "failed":
                input.Say($"It failed, {fax.Failure}, so it costs nothing.");
                break;
            default:
                input.Say($"It's still {fax.Status}. Read it later: new FaxesApi(http, settings).GetFaxAsync(\"{fax.Id}\").");
                break;
        }
    }

    /// <summary>A key of a request's own for its Idempotency-Key, a random UUID.</summary>
    public static string NewKey() => Guid.NewGuid().ToString();

    /// <summary>
    /// The PDF's name, as its link ends: letter.pdf. Each percent-encoded character of it is decoded,
    /// and an escape that is no character's is kept as it is. It keeps 255 characters at most, as the
    /// API takes a name.
    /// </summary>
    public static string NameOf(string link)
    {
        var name = "document.pdf";
        if (Uri.TryCreate(link, UriKind.Absolute, out var parsed))
        {
            var segments = parsed.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length > 0)
            {
                name = Uri.UnescapeDataString(segments[^1]);
            }
        }
        var characters = name.EnumerateRunes().ToList();
        return characters.Count > 255 ? string.Concat(characters.Take(255)) : name;
    }

    /// <summary>A document's pages, once XipFax has counted them.</summary>
    public static string PagesOf(DraftDocument document) => document.PageCount switch
    {
        null => "",
        1 => ", 1 page",
        int count => $", {count} pages",
    };
}
