using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace XipHub.XipFax.Sdk.Tests;

/// <summary>
/// The API's document, openapi.json, as the repository holds it, which reads what the SDK sends and
/// is answered against it: the operation a request is, and each thing in it the document refuses,
/// from its address, its key and its version to each parameter and its body; and what in an answer or
/// an event's body the document's schema refuses. It reads the schema keywords the document uses,
/// and refuses a schema with one it doesn't read, so that a document that comes to say more than this
/// reads fails the specs rather than passing unread. Every value it reads is JSON as
/// <see cref="Json.Parse(byte[])"/> reads it.
/// </summary>
internal sealed class Contract
{
    private static readonly string[] Methods = { "get", "put", "post", "delete", "options", "head", "patch", "trace" };

    private static readonly Regex Bearer = new Regex(@"\ABearer \S+\z");

    /// <summary>A parameter's place in a path, {fax_id}.</summary>
    private static readonly Regex PathParameter = new Regex(@"\{([^}]+)\}");

    /// <summary>The keywords that describe a value without constraining it.</summary>
    private static readonly HashSet<string> Annotations = new HashSet<string>(StringComparer.Ordinal)
    {
        "description", "title", "default", "examples",
    };

    private readonly Dictionary<string, object?> raw;
    private readonly Uri server;

    private Contract(string path, Dictionary<string, object?> raw)
    {
        this.raw = raw;
        Version = Json.Get(Json.Get(raw, "info") as Dictionary<string, object?>, "version") as string ?? "";

        if (Json.Get(raw, "servers") is not List<object?> servers || servers.Count == 0)
        {
            throw new InvalidDataException($"{path} names no server");
        }
        var address = Json.Get(servers[0] as Dictionary<string, object?>, "url") as string;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var parsed))
        {
            throw new InvalidDataException($"{path}'s server, {address}, is no address");
        }
        server = parsed;

        var operations = new List<Operation>();
        var paths = Json.Get(raw, "paths") as Dictionary<string, object?> ?? new Dictionary<string, object?>();
        foreach (var route in Sorted(paths.Keys))
        {
            var item = paths[route] as Dictionary<string, object?> ?? new Dictionary<string, object?>();
            foreach (var method in Methods)
            {
                if (Json.Get(item, method) is Dictionary<string, object?> entry)
                {
                    operations.Add(OperationOf(route, method, item, entry));
                }
            }
        }
        Operations = operations;

        var webhooks = new Dictionary<string, Dictionary<string, object?>>(StringComparer.Ordinal);
        var posted = Json.Get(raw, "webhooks") as Dictionary<string, object?> ?? new Dictionary<string, object?>();
        foreach (var name in Sorted(posted.Keys))
        {
            var post = Json.Get(posted[name] as Dictionary<string, object?>, "post") as Dictionary<string, object?>;
            Dictionary<string, object?>? schema;
            try
            {
                schema = BodySchema(post);
            }
            catch (InvalidDataException e)
            {
                throw new InvalidDataException($"the webhook {name} posts no JSON body the document gives: {e.Message}", e);
            }
            webhooks[name] = schema ?? throw new InvalidDataException($"the webhook {name} posts no JSON body the document gives");
        }
        Webhooks = webhooks;
    }

    /// <summary>The document's info.version, the version of the API it is.</summary>
    public string Version { get; }

    /// <summary>The document's operations, by their paths in order and the method within each.</summary>
    public IReadOnlyList<Operation> Operations { get; }

    /// <summary>The schema of the body each event posts, by the event's name.</summary>
    public IReadOnlyDictionary<string, Dictionary<string, object?>> Webhooks { get; }

    /// <summary>The document as the specs' program holds it, beside the program.</summary>
    public static Contract Load() => Load(Path.Combine(AppContext.BaseDirectory, "openapi.json"));

    /// <summary>The document at path.</summary>
    public static Contract Load(string path)
    {
        object? value;
        try
        {
            value = Json.Parse(File.ReadAllBytes(path));
        }
        catch (JsonException e)
        {
            throw new InvalidDataException($"{path}: {e.Message}", e);
        }
        if (value is not Dictionary<string, object?> raw)
        {
            throw new InvalidDataException($"{path} is no JSON object");
        }
        return new Contract(path, raw);
    }

    private Operation OperationOf(string route, string method, Dictionary<string, object?> item, Dictionary<string, object?> entry)
    {
        var id = Json.Get(entry, "operationId") as string ?? "";

        // The operation's parameters, the path item's and then its own, one of its own taking the
        // place of the item's of the same name and place.
        var declared = new Dictionary<string, Parameter>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var owner in new[] { item, entry })
        {
            if (Json.Get(owner, "parameters") is not List<object?> list)
            {
                continue;
            }
            foreach (var value in list)
            {
                Dictionary<string, object?>? found;
                try
                {
                    found = Resolve(value);
                }
                catch (InvalidDataException e)
                {
                    throw new InvalidDataException($"{id}: {e.Message}", e);
                }
                var parameter = new Parameter(
                    Json.Get(found, "name") as string ?? "",
                    Json.Get(found, "in") as string ?? "",
                    Json.Get(found, "required") is true,
                    Json.Get(found, "schema") as Dictionary<string, object?>);
                var key = parameter.In + ":" + parameter.Name.ToLowerInvariant();
                if (!declared.ContainsKey(key))
                {
                    order.Add(key);
                }
                declared[key] = parameter;
            }
        }

        Dictionary<string, object?>? body;
        try
        {
            body = BodySchema(entry);
        }
        catch (InvalidDataException e)
        {
            throw new InvalidDataException($"{id}: {e.Message}", e);
        }

        var responses = Json.Get(entry, "responses") as Dictionary<string, object?> ?? new Dictionary<string, object?>();
        var success = 0;
        foreach (var status in Sorted(responses.Keys))
        {
            if (int.TryParse(status, NumberStyles.None, CultureInfo.InvariantCulture, out var code) && code >= 200 && code < 300)
            {
                success = code;
                break;
            }
        }

        // The path on the document's server, each {name} in it a segment of its own.
        var full = server.AbsolutePath.TrimEnd('/') + route;
        var names = new List<string>();
        var pattern = new StringBuilder(@"\A");
        var at = 0;
        foreach (Match match in PathParameter.Matches(full))
        {
            pattern.Append(Regex.Escape(full.Substring(at, match.Index - at))).Append("([^/]+)");
            names.Add(match.Groups[1].Value);
            at = match.Index + match.Length;
        }
        pattern.Append(Regex.Escape(full.Substring(at))).Append(@"\z");

        return new Operation(
            id,
            method.ToUpperInvariant(),
            route,
            success,
            order.Select(key => declared[key]).ToList(),
            body,
            new Regex(pattern.ToString()),
            names,
            responses);
    }

    /// <summary>The schema of an operation's JSON body, or null where it takes none.</summary>
    private Dictionary<string, object?>? BodySchema(Dictionary<string, object?>? entry)
    {
        if (entry == null || !entry.TryGetValue("requestBody", out var value))
        {
            return null;
        }
        var body = Resolve(value);
        var content = Json.Get(body, "content") as Dictionary<string, object?>;
        if (Json.Get(content, "application/json") is not Dictionary<string, object?> media)
        {
            throw new InvalidDataException("its body is not JSON");
        }
        return Json.Get(media, "schema") as Dictionary<string, object?>;
    }

    /// <summary>The object a value names, following each $ref it holds into the document.</summary>
    private Dictionary<string, object?>? Resolve(object? value)
    {
        var current = value as Dictionary<string, object?>;
        while (Json.Get(current, "$ref") is string reference)
        {
            current = Pointer(reference);
        }
        return current;
    }

    /// <summary>The object a local reference, #/components/schemas/Fax, names.</summary>
    private Dictionary<string, object?> Pointer(string reference)
    {
        if (!reference.StartsWith("#/", StringComparison.Ordinal))
        {
            throw new InvalidDataException($"{reference} is no reference into the document");
        }
        object? value = raw;
        foreach (var token in reference.Substring(2).Split('/'))
        {
            if (value is not Dictionary<string, object?> current)
            {
                throw new InvalidDataException($"{reference} names nothing in the document");
            }
            value = Json.Get(current, token.Replace("~1", "/").Replace("~0", "~"));
        }
        return value as Dictionary<string, object?>
            ?? throw new InvalidDataException($"{reference} names nothing in the document");
    }

    /// <summary>
    /// The operation a request is, by its method and its path on the document's server, with the
    /// values of its path's parameters; null where it is none.
    /// </summary>
    private (Operation? Operation, Dictionary<string, string> Values) Find(SentRequest request)
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (request.Uri.Scheme != server.Scheme || request.Uri.Authority != server.Authority)
        {
            return (null, values);
        }
        foreach (var operation in Operations)
        {
            if (operation.Method != request.Method)
            {
                continue;
            }
            var match = operation.Pattern.Match(request.Uri.AbsolutePath);
            if (!match.Success)
            {
                continue;
            }
            for (var index = 0; index < operation.Names.Count; index++)
            {
                values[operation.Names[index]] = Uri.UnescapeDataString(match.Groups[index + 1].Value);
            }
            return (operation, values);
        }
        return (null, values);
    }

    /// <summary>
    /// Reads a request against the document: the id of the operation it is, and each thing in it the
    /// document refuses, none where it fits. version is the version its Xip-Version must name.
    /// </summary>
    public (string Id, List<string> Refused) ReadRequest(SentRequest request, string version)
    {
        var (operation, values) = Find(request);
        if (operation == null)
        {
            return ("", new List<string> { $"{request.Method} {request.Uri.AbsoluteUri} is no operation of the document" });
        }

        var refused = new List<string>();
        var keys = request.Values("Authorization");
        if (keys.Count != 1 || !Bearer.IsMatch(keys[0]))
        {
            refused.Add($"Authorization is {Text.QuoteAll(keys)}, not one bearer key");
        }
        var versions = request.Values("Xip-Version");
        if (versions.Count != 1 || versions[0] != version)
        {
            refused.Add($"Xip-Version is {Text.QuoteAll(versions)}, not {version}");
        }

        var query = QueryOf(request.Uri.Query);
        var takesQuery = new HashSet<string>(StringComparer.Ordinal);
        foreach (var parameter in operation.Parameters)
        {
            IReadOnlyList<string> given = Array.Empty<string>();
            switch (parameter.In)
            {
                case "query":
                    takesQuery.Add(parameter.Name);
                    if (query.TryGetValue(parameter.Name, out var sent))
                    {
                        given = sent;
                    }
                    break;
                case "header":
                    given = request.Values(parameter.Name);
                    break;
                case "path":
                    if (values.TryGetValue(parameter.Name, out var segment))
                    {
                        given = new[] { segment };
                    }
                    break;
            }
            var at = parameter.In + " " + parameter.Name;
            if (given.Count == 0)
            {
                if (parameter.Required)
                {
                    refused.Add(at + " is missing");
                }
            }
            else if (given.Count > 1)
            {
                refused.Add($"{at} is sent {given.Count} times");
            }
            else
            {
                refused.AddRange(CheckText(parameter.Schema, given[0], at));
            }
        }
        foreach (var name in Sorted(query.Keys))
        {
            if (!takesQuery.Contains(name))
            {
                refused.Add($"query {name} is no parameter of {operation.Id}");
            }
        }

        if (operation.Body != null)
        {
            var type = request.Values("Content-Type").FirstOrDefault() ?? "";
            if (!type.StartsWith("application/json", StringComparison.Ordinal))
            {
                refused.Add("the body is not sent as JSON");
            }
            object? body = null;
            var read = true;
            try
            {
                body = Json.Parse(request.Body);
            }
            catch (JsonException e)
            {
                refused.Add($"the body is no JSON: {e.Message}");
                read = false;
            }
            if (read)
            {
                refused.AddRange(Check(operation.Body, body, "body"));
            }
        }
        else if (request.Body.Length > 0)
        {
            refused.Add("a body the operation takes none of");
        }
        return (operation.Id, refused);
    }

    /// <summary>
    /// Reads an answer to a request against the document: what in it the operation's answer for its
    /// status refuses, none where it fits. An empty body is an answer that carries none.
    /// </summary>
    public List<string> ReadAnswer(SentRequest request, int status, string body)
    {
        var (operation, _) = Find(request);
        if (operation == null)
        {
            return new List<string> { $"{request.Method} {request.Uri.AbsoluteUri} is no operation of the document" };
        }
        if (!operation.Responses.TryGetValue(status.ToString(CultureInfo.InvariantCulture), out var answer))
        {
            return new List<string> { $"{operation.Id} gives no {status}" };
        }
        Dictionary<string, object?>? response;
        try
        {
            response = Resolve(answer);
        }
        catch (InvalidDataException e)
        {
            return new List<string> { e.Message };
        }
        if (Json.Get(response, "content") is not Dictionary<string, object?> content)
        {
            return body.Length > 0
                ? new List<string> { $"{operation.Id}'s {status} carries no body" }
                : new List<string>();
        }
        var schema = Json.Get(Json.Get(content, "application/json") as Dictionary<string, object?>, "schema") as Dictionary<string, object?>;
        object? value;
        try
        {
            value = Json.Parse(Encoding.UTF8.GetBytes(body));
        }
        catch (JsonException e)
        {
            return new List<string> { $"{operation.Id}'s {status} is no JSON: {e.Message}" };
        }
        return Check(schema, value, "answer");
    }

    /// <summary>
    /// What the operation's Xip-Version parameter refuses in version, none where it takes it.
    /// </summary>
    public List<string> VersionRefused(Operation operation, string version)
    {
        foreach (var parameter in operation.Parameters)
        {
            if (parameter.In == "header" && parameter.Name == "Xip-Version")
            {
                return CheckText(parameter.Schema, version, "header Xip-Version");
            }
        }
        return new List<string> { operation.Id + " takes no Xip-Version" };
    }

    /// <summary>A parameter's text, read as its schema's type, checked against the schema.</summary>
    private List<string> CheckText(Dictionary<string, object?>? schema, string text, string at)
    {
        Dictionary<string, object?>? target;
        try
        {
            target = Resolve(schema);
        }
        catch (InvalidDataException e)
        {
            return new List<string> { $"{at}: {e.Message}" };
        }
        if (Json.Get(target, "type") is "integer")
        {
            if (!long.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            {
                return new List<string> { $"{at} is {Text.Quote(text)}, no integer" };
            }
            return Check(schema, (double)number, at);
        }
        return Check(schema, text, at);
    }

    /// <summary>
    /// What the schema refuses in value, each at its place from at; none where it fits.
    /// </summary>
    public List<string> Check(Dictionary<string, object?>? schema, object? value, string at)
    {
        var refused = new List<string>();
        if (schema == null)
        {
            return refused;
        }
        foreach (var keyword in Sorted(schema.Keys))
        {
            var rule = schema[keyword];
            switch (keyword)
            {
                case "$ref":
                {
                    Dictionary<string, object?> target;
                    try
                    {
                        target = Pointer(Text.Of(rule));
                    }
                    catch (InvalidDataException e)
                    {
                        refused.Add($"{at}: {e.Message}");
                        break;
                    }
                    refused.AddRange(Check(target, value, at));
                    break;
                }
                case "type":
                {
                    if (!HasType(rule, value))
                    {
                        refused.Add($"{at} is {TypeOf(value)}, not {Text.Of(rule)}");
                    }
                    break;
                }
                case "enum":
                {
                    if (!(rule is List<object?> allowed && allowed.Any(item => Json.Same(item, value))))
                    {
                        refused.Add($"{at} is {Text.Of(value)}, none of {Text.Of(rule)}");
                    }
                    break;
                }
                case "const":
                {
                    if (!Json.Same(rule, value))
                    {
                        refused.Add($"{at} is {Text.Of(value)}, not {Text.Of(rule)}");
                    }
                    break;
                }
                case "minLength":
                case "maxLength":
                {
                    if (value is string text && rule is double limit)
                    {
                        double length = text.EnumerateRunes().Count();
                        if ((keyword == "minLength" && length < limit) || (keyword == "maxLength" && length > limit))
                        {
                            refused.Add($"{at} has {Text.Of(length)} characters, past its {keyword} of {Text.Of(limit)}");
                        }
                    }
                    break;
                }
                case "pattern":
                {
                    if (value is not string text)
                    {
                        break;
                    }
                    Regex expression;
                    try
                    {
                        expression = new Regex(EndAnchored(Text.Of(rule)));
                    }
                    catch (ArgumentException e)
                    {
                        refused.Add($"{at}: its pattern {Text.Of(rule)} doesn't compile: {e.Message}");
                        break;
                    }
                    if (!expression.IsMatch(text))
                    {
                        refused.Add($"{at} is {Text.Quote(text)}, not of the pattern {Text.Of(rule)}");
                    }
                    break;
                }
                case "minimum":
                case "maximum":
                {
                    if (value is double number && rule is double limit)
                    {
                        if ((keyword == "minimum" && number < limit) || (keyword == "maximum" && number > limit))
                        {
                            refused.Add($"{at} is {Text.Of(number)}, past its {keyword} of {Text.Of(limit)}");
                        }
                    }
                    break;
                }
                case "minItems":
                case "maxItems":
                {
                    if (value is List<object?> list && rule is double limit)
                    {
                        double count = list.Count;
                        if ((keyword == "minItems" && count < limit) || (keyword == "maxItems" && count > limit))
                        {
                            refused.Add($"{at} has {Text.Of(count)} items, past its {keyword} of {Text.Of(limit)}");
                        }
                    }
                    break;
                }
                case "items":
                {
                    if (value is List<object?> list && rule is Dictionary<string, object?> items)
                    {
                        for (var index = 0; index < list.Count; index++)
                        {
                            refused.AddRange(Check(items, list[index], $"{at}[{index}]"));
                        }
                    }
                    break;
                }
                case "properties":
                {
                    if (value is Dictionary<string, object?> members && rule is Dictionary<string, object?> properties)
                    {
                        foreach (var name in Sorted(properties.Keys))
                        {
                            if (members.TryGetValue(name, out var member) && properties[name] is Dictionary<string, object?> property)
                            {
                                refused.AddRange(Check(property, member, at + "." + name));
                            }
                        }
                    }
                    break;
                }
                case "required":
                {
                    if (value is Dictionary<string, object?> members && rule is List<object?> names)
                    {
                        foreach (var name in names)
                        {
                            if (!members.ContainsKey(Text.Of(name)))
                            {
                                refused.Add($"{at}.{Text.Of(name)} is missing");
                            }
                        }
                    }
                    break;
                }
                case "additionalProperties":
                {
                    if (value is not Dictionary<string, object?> members)
                    {
                        break;
                    }
                    var properties = Json.Get(schema, "properties") as Dictionary<string, object?>;
                    foreach (var name in Sorted(members.Keys))
                    {
                        if (properties != null && properties.ContainsKey(name))
                        {
                            continue;
                        }
                        if (rule is bool allowed)
                        {
                            if (!allowed)
                            {
                                refused.Add($"{at}.{name} is no member it takes");
                            }
                        }
                        else if (rule is Dictionary<string, object?> extra)
                        {
                            refused.AddRange(Check(extra, members[name], at + "." + name));
                        }
                    }
                    break;
                }
                case "propertyNames":
                {
                    if (value is Dictionary<string, object?> members && rule is Dictionary<string, object?> names)
                    {
                        foreach (var name in Sorted(members.Keys))
                        {
                            refused.AddRange(Check(names, name, at + " name " + name));
                        }
                    }
                    break;
                }
                case "anyOf":
                {
                    var fits = rule is List<object?> choices
                        && choices.Any(choice => choice is Dictionary<string, object?> option && Check(option, value, at).Count == 0);
                    if (!fits)
                    {
                        refused.Add($"{at} fits none of its anyOf");
                    }
                    break;
                }
                default:
                {
                    if (!Annotations.Contains(keyword))
                    {
                        refused.Add($"{at}: the contract reads no {keyword}, which its schema gives");
                    }
                    break;
                }
            }
        }
        return refused;
    }

    /// <summary>
    /// A schema's pattern as .NET reads it: each $ outside a set of characters as \z, since in the
    /// ECMA-262 patterns a JSON schema gives, $ matches only at the end of the text, where .NET's also
    /// matches before a last line break.
    /// </summary>
    private static string EndAnchored(string pattern)
    {
        var translated = new StringBuilder(pattern.Length + 4);
        var inSet = false;
        for (var index = 0; index < pattern.Length; index++)
        {
            var character = pattern[index];
            if (character == '\\' && index + 1 < pattern.Length)
            {
                translated.Append(character).Append(pattern[++index]);
            }
            else if (inSet)
            {
                inSet = character != ']';
                translated.Append(character);
            }
            else if (character == '$')
            {
                translated.Append(@"\z");
            }
            else
            {
                inSet = character == '[';
                translated.Append(character);
            }
        }
        return translated.ToString();
    }

    /// <summary>Whether value is of the type, or one of the types, a schema's type names.</summary>
    private static bool HasType(object? rule, object? value) => rule switch
    {
        string name => IsType(name, value),
        List<object?> names => names.Any(name => IsType(Text.Of(name), value)),
        _ => false,
    };

    private static bool IsType(string name, object? value) => name switch
    {
        "null" => value == null,
        "boolean" => value is bool,
        "string" => value is string,
        "number" => value is double,
        "integer" => value is double number && Math.Truncate(number) == number,
        "array" => value is List<object?>,
        "object" => value is Dictionary<string, object?>,
        _ => false,
    };

    private static string TypeOf(object? value) =>
        new[] { "null", "boolean", "integer", "number", "string", "array", "object" }.FirstOrDefault(name => IsType(name, value))
        ?? value?.GetType().Name
        ?? "null";

    /// <summary>A query's parameters, by name, each with its values in their order.</summary>
    private static Dictionary<string, List<string>> QueryOf(string query)
    {
        var parameters = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var pair in (query.StartsWith('?') ? query.Substring(1) : query).Split('&'))
        {
            if (pair.Length == 0)
            {
                continue;
            }
            var equals = pair.IndexOf('=');
            var name = Unescape(equals < 0 ? pair : pair.Substring(0, equals));
            var value = equals < 0 ? "" : Unescape(pair.Substring(equals + 1));
            if (!parameters.TryGetValue(name, out var values))
            {
                values = new List<string>();
                parameters[name] = values;
            }
            values.Add(value);
        }
        return parameters;
    }

    private static string Unescape(string text) => Uri.UnescapeDataString(text.Replace('+', ' '));

    /// <summary>Names in the order of their bytes, as the document's are read.</summary>
    public static List<string> Sorted(IEnumerable<string> names)
    {
        var sorted = names.ToList();
        sorted.Sort(StringComparer.Ordinal);
        return sorted;
    }
}

/// <summary>One operation of the document.</summary>
/// <param name="Id">Its operationId.</param>
/// <param name="Method">Its method, GET.</param>
/// <param name="Path">Its path, /faxes/{fax_id}.</param>
/// <param name="Success">The first status of 2xx it answers.</param>
internal sealed record Operation(
    string Id,
    string Method,
    string Path,
    int Success,
    IReadOnlyList<Parameter> Parameters,
    Dictionary<string, object?>? Body,
    Regex Pattern,
    IReadOnlyList<string> Names,
    Dictionary<string, object?> Responses);

/// <summary>One parameter of an operation.</summary>
internal sealed record Parameter(string Name, string In, bool Required, Dictionary<string, object?>? Schema);

/// <summary>JSON as the contract reads it, in plain values.</summary>
internal static class Json
{
    /// <summary>
    /// JSON as plain values: an object as a Dictionary of its members, the last of a name kept; an
    /// array as a List; a number as a double; a string, true, false, and null for null.
    /// </summary>
    public static object? Parse(byte[] utf8)
    {
        using var document = JsonDocument.Parse(new ReadOnlyMemory<byte>(utf8));
        return Plain(document.RootElement);
    }

    /// <summary>The value of an object's member, or null where it has none or isn't one.</summary>
    public static object? Get(Dictionary<string, object?>? members, string name) =>
        members != null && members.TryGetValue(name, out var value) ? value : null;

    /// <summary>Whether two plain values are the same JSON.</summary>
    public static bool Same(object? first, object? second) => (first, second) switch
    {
        (null, null) => true,
        (string a, string b) => a == b,
        (bool a, bool b) => a == b,
        (double a, double b) => a == b,
        (List<object?> a, List<object?> b) => a.Count == b.Count && a.Zip(b).All(pair => Same(pair.First, pair.Second)),
        (Dictionary<string, object?> a, Dictionary<string, object?> b) =>
            a.Count == b.Count && a.All(member => b.TryGetValue(member.Key, out var other) && Same(member.Value, other)),
        _ => false,
    };

    private static object? Plain(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
            {
                var members = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (var member in element.EnumerateObject())
                {
                    members[member.Name] = Plain(member.Value);
                }
                return members;
            }
            case JsonValueKind.Array:
                return element.EnumerateArray().Select(Plain).ToList();
            case JsonValueKind.String:
                return element.GetString();
            case JsonValueKind.Number:
                return element.GetDouble();
            case JsonValueKind.True:
                return true;
            case JsonValueKind.False:
                return false;
            default:
                return null;
        }
    }
}

/// <summary>Plain values as the contract's refusals give them.</summary>
internal static class Text
{
    /// <summary>
    /// A value as its refusals name it: a string as it is, a number at its shortest, a list as
    /// [a b], an object as map[name:value], null as &lt;nil&gt;.
    /// </summary>
    public static string Of(object? value) => value switch
    {
        null => "<nil>",
        string text => text,
        bool flag => flag ? "true" : "false",
        double number => number.ToString("R", CultureInfo.InvariantCulture),
        List<object?> list => "[" + string.Join(" ", list.Select(Of)) + "]",
        Dictionary<string, object?> members =>
            "map[" + string.Join(" ", Contract.Sorted(members.Keys).Select(name => name + ":" + Of(members[name]))) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    /// <summary>A string in double quotes, its quotes, backslashes and control characters escaped.</summary>
    public static string Quote(string text)
    {
        var quoted = new StringBuilder("\"");
        foreach (var character in text)
        {
            switch (character)
            {
                case '"':
                    quoted.Append("\\\"");
                    break;
                case '\\':
                    quoted.Append("\\\\");
                    break;
                case '\n':
                    quoted.Append("\\n");
                    break;
                case '\r':
                    quoted.Append("\\r");
                    break;
                case '\t':
                    quoted.Append("\\t");
                    break;
                default:
                    if (char.IsControl(character))
                    {
                        quoted.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        quoted.Append(character);
                    }
                    break;
            }
        }
        return quoted.Append('"').ToString();
    }

    /// <summary>Strings each in double quotes, as [&quot;a&quot; &quot;b&quot;].</summary>
    public static string QuoteAll(IEnumerable<string> texts) => "[" + string.Join(" ", texts.Select(Quote)) + "]";
}
