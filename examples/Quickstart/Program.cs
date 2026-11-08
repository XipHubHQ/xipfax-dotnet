using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using XipHub.XipFax.Sdk;
using XipHub.XipFax.Sdk.Client;
using XipHub.XipFax.Sdk.Model;

namespace XipHub.XipFax.Examples;

/// <summary>
/// Runs XipFax's quickstart in your terminal, from a clone of this repository:
/// <c>XIPFAX_API_KEY=xipfax_key_live_… dotnet run --project examples/Quickstart</c>. That takes the
/// free steps alone. To send a fax too, real and charged, name a fax number of yours and a public
/// link to a PDF: <c>XIPFAX_TO='+1 202 555 0143' XIPFAX_PDF_URL=https://example.com/letter.pdf
/// XIPFAX_API_KEY=… dotnet run --project examples/Quickstart</c>.
/// </summary>
internal static class Program
{
    private static async Task<int> Main()
    {
        XipFaxConfiguration settings;
        try
        {
            settings = new XipFaxConfiguration(Environment.GetEnvironmentVariable("XIPFAX_API_KEY"));
        }
        catch (ArgumentException)
        {
            Console.Error.WriteLine(
                "Set XIPFAX_API_KEY to a key you made in the developer console: https://developer.xipfax.com/console/");
            return 1;
        }
        var to = Environment.GetEnvironmentVariable("XIPFAX_TO");
        var pdfUrl = Environment.GetEnvironmentVariable("XIPFAX_PDF_URL");

        using var http = new HttpClient();
        var input = new QuickstartInput(
            to,
            pdfUrl,
            Console.WriteLine,
            question =>
            {
                Console.Write(question);
                // A terminal that closes answers what was typed before it did, which sends only if
                // that was send.
                return Console.ReadLine();
            },
            Task.Delay);
        try
        {
            await Quickstart.RunAsync(http, settings, input, CancellationToken.None);
        }
        catch (ApiException refused)
        {
            var error = RefusalOf(refused)?.Error;
            Console.Error.WriteLine(error != null ? $"XipFax refused it, {error.Code}: {error.Message}" : refused.Message);
            return 1;
        }
        catch (HttpRequestException unreached)
        {
            Console.Error.WriteLine(unreached.Message);
            return 1;
        }
        if (string.IsNullOrEmpty(to) || string.IsNullOrEmpty(pdfUrl))
        {
            Console.WriteLine(
                "Those steps cost nothing. To send a fax too, run it again with XIPFAX_TO, a fax number of yours, and XIPFAX_PDF_URL, a public link to a PDF.");
        }
        return 0;
    }

    /// <summary>The API's error in a refusal's body, or null where the body holds none.</summary>
    private static ApiError? RefusalOf(ApiException refused)
    {
        if (refused.ErrorContent is not string body)
        {
            return null;
        }
        try
        {
            return JsonConvert.DeserializeObject<ApiError>(body);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
