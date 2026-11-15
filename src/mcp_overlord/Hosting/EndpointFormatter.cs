using System.Net;
using mcp_overlord.Configuration;

namespace mcp_overlord.Hosting
{
    /// <summary>A URL the operator can copy.</summary>
    public sealed class EndpointAddress
    {
        /// <summary>Full MCP URL, including <c>/mcp</c>.</summary>
        public string Url { get; init; } = "";

        /// <summary>True for the HTTPS listener.</summary>
        public bool IsHttps { get; init; }
    }

    /// <summary>Builds bind URLs, public MCP URLs, and the origins this process accepts.</summary>
    public static class EndpointFormatter
    {
        /// <summary>
        /// Public MCP URLs for the settings.
        /// </summary>
        /// <param name="settings">Listener settings.</param>
        /// <returns>HTTP, then HTTPS when it is enabled.</returns>
        public static IReadOnlyList<EndpointAddress> PublicEndpoints(ServerSettings settings)
        {
            var endpoints = new List<EndpointAddress>
            {
                new EndpointAddress
                {
                    Url = PublicUrl("http", HttpBindHost(settings), settings.HttpPort),
                    IsHttps = false
                }
            };

            if (settings.EnableHttps)
            {
                endpoints.Add(new EndpointAddress
                {
                    Url = PublicUrl("https", HttpsPublicHost(settings), settings.HttpsPort),
                    IsHttps = true
                });
            }

            return endpoints;
        }

        /// <summary>
        /// Kestrel listen URLs. These do not include the <c>/mcp</c> path.
        /// </summary>
        /// <param name="settings">Listener settings.</param>
        /// <returns>One or two URLs.</returns>
        public static string[] BindUrls(ServerSettings settings)
        {
            var urls = new List<string>
            {
                BindUrl("http", HttpBindHost(settings), settings.HttpPort)
            };

            if (settings.EnableHttps)
            {
                urls.Add(BindUrl("https", settings.BindAddress, settings.HttpsPort));
            }

            return urls.ToArray();
        }

        /// <summary>
        /// Origins that match this listener. Browsers sending any other Origin are rejected.
        /// </summary>
        /// <param name="settings">Listener settings.</param>
        /// <returns>A case-insensitive set of origins.</returns>
        public static IReadOnlySet<string> AllowedOrigins(ServerSettings settings)
        {
            var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string url in BindUrls(settings))
            {
                origins.Add(url);
            }

            AddPort(origins, "127.0.0.1", settings.HttpPort, settings.EnableHttps ? settings.HttpsPort : null);
            AddPort(origins, "localhost", settings.HttpPort, settings.EnableHttps ? settings.HttpsPort : null);
            AddPort(origins, "::1", settings.HttpPort, settings.EnableHttps ? settings.HttpsPort : null);
            if (settings.EnableHttps)
            {
                origins.Add(Origin("https", HttpsPublicHost(settings), settings.HttpsPort));
                if (settings.Certificate != null)
                {
                    foreach (string hostName in settings.Certificate.HostNames)
                    {
                        if (!string.IsNullOrWhiteSpace(hostName))
                        {
                            origins.Add(Origin("https", hostName.Trim(), settings.HttpsPort));
                        }
                    }
                }
            }

            return origins;
        }

        /// <summary>
        /// Removes an API key or certificate password if a message happens to contain one.
        /// </summary>
        /// <param name="text">Text about to be logged.</param>
        /// <param name="settings">Settings whose secrets should be removed.</param>
        /// <returns>The scrubbed text.</returns>
        public static string Scrub(string? text, ServerSettings? settings)
        {
            if (string.IsNullOrEmpty(text))
            {
                return text ?? "";
            }

            if (settings == null)
            {
                return text;
            }

            string scrubbed = text;
            if (!string.IsNullOrEmpty(settings.ApiKey))
            {
                scrubbed = scrubbed.Replace(settings.ApiKey, "***", StringComparison.Ordinal);
            }

            string? password = settings.Certificate?.Password;
            if (!string.IsNullOrEmpty(password))
            {
                scrubbed = scrubbed.Replace(password, "***", StringComparison.Ordinal);
            }

            return scrubbed;
        }

        private static string HttpBindHost(ServerSettings settings)
        {
            if (settings.EnableHttps && !settings.IsLoopback)
            {
                return "127.0.0.1";
            }

            return settings.BindAddress;
        }

        private static string HttpsPublicHost(ServerSettings settings)
        {
            if (!IsWildcard(settings.BindAddress))
            {
                return settings.BindAddress;
            }

            string? configured = FirstHostName(settings);
            if (!string.IsNullOrEmpty(configured))
            {
                return configured;
            }

            return Environment.MachineName;
        }

        private static string? FirstHostName(ServerSettings settings)
        {
            if (settings.Certificate == null)
            {
                return null;
            }

            foreach (string hostName in settings.Certificate.HostNames)
            {
                if (!string.IsNullOrWhiteSpace(hostName))
                {
                    return hostName.Trim();
                }
            }

            return null;
        }

        private static bool IsWildcard(string bindAddress)
        {
            if (!IPAddress.TryParse(bindAddress, out IPAddress? address))
            {
                return false;
            }

            return address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any);
        }

        private static void AddPort(HashSet<string> origins, string host, int httpPort, int? httpsPort)
        {
            origins.Add(Origin("http", host, httpPort));
            if (httpsPort.HasValue)
            {
                origins.Add(Origin("https", host, httpsPort.Value));
            }
        }

        private static string Origin(string scheme, string host, int port)
        {
            return BindUrl(scheme, host, port);
        }

        private static string PublicUrl(string scheme, string host, int port)
        {
            return BindUrl(scheme, host, port) + "/mcp";
        }

        private static string BindUrl(string scheme, string host, int port)
        {
            string formattedHost = host.Contains(':') ? "[" + host + "]" : host;
            return scheme + "://" + formattedHost + ":" + port;
        }
    }
}
