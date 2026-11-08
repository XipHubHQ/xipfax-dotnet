using System;
using Xunit;

namespace XipHub.XipFax.Sdk.Tests;

/// <summary>
/// The specs run with no credential in their environment. Both specs change or read the process's
/// environment, so they are one class, which xUnit runs one spec at a time.
/// </summary>
public sealed class CredentialsSpecs
{
    [Fact]
    public void TheEnvironmentHoldsNoCredential()
    {
        Assert.Empty(Credentials.Left());
    }

    [Fact]
    public void StripTakesAKeyATokenAndTheXipFaxVariablesAndKeepsTheRest()
    {
        string[] names = { "XIPFAX_API_KEY", "XIPFAX_TO", "GITHUB_TOKEN", "PLAIN_SETTING" };
        Environment.SetEnvironmentVariable("XIPFAX_API_KEY", "stand-in");
        Environment.SetEnvironmentVariable("XIPFAX_TO", "+1 202 555 0143");
        Environment.SetEnvironmentVariable("GITHUB_TOKEN", "stand-in");
        Environment.SetEnvironmentVariable("PLAIN_SETTING", "kept");
        try
        {
            var removed = Credentials.Strip();

            Assert.Equal(new[] { "GITHUB_TOKEN", "XIPFAX_API_KEY", "XIPFAX_TO" }, removed);
            Assert.Equal("kept", Environment.GetEnvironmentVariable("PLAIN_SETTING"));
        }
        finally
        {
            foreach (var name in names)
            {
                Environment.SetEnvironmentVariable(name, null);
            }
        }
    }
}
