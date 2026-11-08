using System;
using XipHub.XipFax.Sdk.Client;

namespace XipHub.XipFax.Sdk
{
    /// <summary>
    /// The settings of a client of XipFax's API, made from your API key: each request they make
    /// carries the key as a bearer token and names <see cref="ApiVersion.Value"/> in its Xip-Version
    /// header. Make each API from them, as in <c>new FaxesApi(new XipFaxConfiguration(apiKey))</c>.
    /// An API takes the settings when it is made, so change any before you make one.
    /// </summary>
    public sealed class XipFaxConfiguration : Configuration
    {
        /// <summary>
        /// Makes the settings of a client of XipFax's API at https://api.xipfax.com from your API
        /// key, xipfax_key_live_ and 32 more characters, which you make in the developer console and
        /// keep on your server.
        /// </summary>
        /// <param name="apiKey">Your API key.</param>
        /// <exception cref="ArgumentException">The key is null, empty or only spaces.</exception>
        public XipFaxConfiguration(string apiKey)
            : this(apiKey, null)
        {
        }

        /// <summary>
        /// Makes the settings of a client of XipFax's API from your API key, at the address you name.
        /// </summary>
        /// <param name="apiKey">Your API key.</param>
        /// <param name="basePath">Where the API answers, or null for https://api.xipfax.com.</param>
        /// <exception cref="ArgumentException">
        /// The key is null, empty or only spaces, or the address isn't an http or https one.
        /// </exception>
        public XipFaxConfiguration(string apiKey, string basePath)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                throw new ArgumentException(
                    "no API key: make one in the developer console, https://developer.xipfax.com/console/",
                    nameof(apiKey));
            }
            if (basePath != null)
            {
                if (!Uri.TryCreate(basePath, UriKind.Absolute, out Uri address)
                    || (address.Scheme != Uri.UriSchemeHttps && address.Scheme != Uri.UriSchemeHttp))
                {
                    throw new ArgumentException($"{basePath} is not an http or https address.", nameof(basePath));
                }
                BasePath = basePath.TrimEnd('/');
            }
            UserAgent = "xipfax-dotnet/" + Version;
            DefaultHeaders["Authorization"] = "Bearer " + apiKey;
            DefaultHeaders["Xip-Version"] = ApiVersion.Value;
        }
    }
}
