using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace XipHub.XipFax.Sdk.Tests;

/// <summary>
/// Keeps every credential out of the specs: before any of them runs, anything in the process's
/// environment shaped like a credential, or named for XipFax, goes. The day a real key sits in a
/// developer's shell, a spec that could reach the API still has nothing to reach it with, and the
/// specs keep saying so.
/// </summary>
internal static class Credentials
{
    /// <summary>A variable that is a secret in itself, in any case.</summary>
    private static readonly Regex Secret = new Regex(
        @"TOKEN|SECRET|PASSWORD|CREDENTIAL|AUTH|APIKEY|API_KEY|_KEY\z|_KEYS\z|COOKIE|PRIVATE",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The variables XipFax's own examples read, such as XIPFAX_API_KEY.</summary>
    private static readonly Regex XipFaxVariable = new Regex(
        @"\AXIPFAX_", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Whether a spec's process has no business carrying the variable.</summary>
    public static bool Shaped(string name) => Secret.IsMatch(name) || XipFaxVariable.IsMatch(name);

    /// <summary>The name of each such variable in the process's environment, sorted.</summary>
    public static List<string> Left()
    {
        var left = new List<string>();
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            if (entry.Key is string name && Shaped(name))
            {
                left.Add(name);
            }
        }
        left.Sort(StringComparer.Ordinal);
        return left;
    }

    /// <summary>
    /// Removes each such variable from the process's environment, answering the names it removed,
    /// sorted.
    /// </summary>
    public static List<string> Strip()
    {
        var removed = Left();
        foreach (var name in removed)
        {
            Environment.SetEnvironmentVariable(name, null);
        }
        return removed;
    }
}

/// <summary>
/// Strips the environment as the specs' program loads, before its entry point or any spec runs: a
/// module's initializer runs before any other code of the module.
/// </summary>
internal static class StripBeforeAnySpec
{
    [ModuleInitializer]
    internal static void Strip() => Credentials.Strip();
}
