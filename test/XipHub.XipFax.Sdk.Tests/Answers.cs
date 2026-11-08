using System;
using System.Collections.Generic;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace XipHub.XipFax.Sdk.Tests;

/// <summary>The API's answers the specs are given, in JSON, each as the document gives it.</summary>
internal static class Answers
{
    /// <summary>The JSON written as it reads, its quotes and its plus signs unescaped.</summary>
    private static readonly JsonSerializerOptions Plain = new JsonSerializerOptions
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>GET /quote's answer for a fax of one page.</summary>
    public const string Quote =
        "{\"pages\":1,\"price\":{\"amount_minor\":100,\"currency\":\"USD\"},\"price_shown\":\"$1.00\","
        + "\"terms\":\"The stand-in’s terms.\",\"price_list\":\"stand-in\"}";

    /// <summary>An office of GET /offices, which takes Form 2848 from Ohio.</summary>
    public const string Office =
        "{\"id\":\"stand-in-office\",\"name\":\"A stand-in office\",\"fax_number\":\"+12025550143\","
        + "\"fax_number_from_abroad\":null,\"applies_to\":[\"Ohio\"],\"takes\":[\"Form 2848\"],"
        + "\"signed_in_ink\":false,\"confidential_cover\":false,"
        + "\"sources\":[{\"title\":\"A stand-in page\",\"url\":\"https://example.com/office\"}],"
        + "\"checked_on\":\"2026-11-01\"}";

    /// <summary>GET /plan's answer for a client with no plan.</summary>
    public const string NoPlan = "{\"plan\":null}";

    /// <summary>A document of the draft, ready, by its id and its name.</summary>
    public static string Document(string id, string name) => ObjectOf(new Dictionary<string, object?>
    {
        ["id"] = id,
        ["name"] = name,
        ["size_bytes"] = 20480,
        ["page_count"] = 2,
        ["status"] = "ready",
        ["url"] = "https://example.com/" + id + ".pdf",
    });

    /// <summary>
    /// fax_1, a fax you sent that was delivered, with each member changes names in place of its own.
    /// </summary>
    public static string Fax(IReadOnlyDictionary<string, object?>? changes = null)
    {
        var fax = new Dictionary<string, object?>
        {
            ["id"] = "fax_1",
            ["direction"] = "sent",
            ["status"] = "delivered",
            ["step"] = null,
            ["office_id"] = null,
            ["to_number"] = "+12025550143",
            ["from_number"] = "+12025550100",
            ["pages"] = 2,
            ["price"] = null,
            ["sent_at"] = "2026-11-07T02:00:00Z",
            ["finished_at"] = "2026-11-07T02:03:00Z",
            ["retry_at"] = null,
            ["failure"] = null,
            ["time_zone"] = "America/New_York",
            ["sender_name"] = null,
            ["cover_confidential"] = false,
        };
        if (changes != null)
        {
            foreach (var change in changes)
            {
                fax[change.Key] = change.Value;
            }
        }
        return ObjectOf(fax);
    }

    /// <summary>The API's refusal, by its code and its sentence.</summary>
    public static string Refusal(string code, string message) => ObjectOf(new Dictionary<string, object?>
    {
        ["error"] = new Dictionary<string, object?>
        {
            ["code"] = code,
            ["message"] = message,
            ["details"] = Array.Empty<object>(),
            ["request_id"] = "req_1",
            ["doc_url"] = null,
        },
    });

    /// <summary>Members as a JSON object, its members in the order of their names.</summary>
    private static string ObjectOf(Dictionary<string, object?> members) =>
        JsonSerializer.Serialize(new SortedDictionary<string, object?>(members, StringComparer.Ordinal), Plain);
}
