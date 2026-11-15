using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using mcp_overlord.Configuration;

namespace mcp_overlord.UI
{
    /// <summary>
    /// Edits server settings. Each field is written only when its Save checkbox is on.
    /// </summary>
    public sealed class ServerSettingsViewModel : INotifyPropertyChanged
    {
        private readonly ConfigDraft _draft;
        private readonly Func<ConfigDraft, ConfigEditResult> _apply;
        private string _httpPortText = "5000";
        private bool _includeHttpPort;
        private string _httpsPortText = "5001";
        private bool _includeHttpsPort;
        private bool _enableHttps;
        private bool _includeEnableHttps;
        private string _bindAddress = "127.0.0.1";
        private bool _includeBindAddress;
        private string _apiKey = "";
        private bool _includeApiKey;
        private bool _showApiKey;
        private string _certificatePath = "";
        private bool _includeCertificatePath;
        private string _certificatePassword = "";
        private bool _includeCertificatePassword;
        private string _hostNamesText = "";
        private bool _includeHostNames;
        private string _logRetentionText = ServerSettings.DefaultLogRetention.ToString();
        private bool _includeLogRetention;
        private string _errorText = "";
        private string _warningText = "";

        /// <summary>
        /// Creates the editor from a document clone.
        /// </summary>
        /// <param name="draft">Document whose server block is edited. The editor keeps this instance and applies it on OK.</param>
        /// <param name="apply">Validates and applies the document.</param>
        public ServerSettingsViewModel(ConfigDraft draft, Func<ConfigDraft, ConfigEditResult> apply)
        {
            _draft = draft ?? throw new ArgumentNullException(nameof(draft));
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            ServerDraft server = draft.Server;
            _httpPortText = server.HttpPort.Value.ToString();
            _includeHttpPort = server.HttpPort.IsIncluded;
            _httpsPortText = server.HttpsPort.Value.ToString();
            _includeHttpsPort = server.HttpsPort.IsIncluded;
            _enableHttps = server.EnableHttps.Value;
            _includeEnableHttps = server.EnableHttps.IsIncluded;
            _bindAddress = server.BindAddress.Value;
            _includeBindAddress = server.BindAddress.IsIncluded;
            _apiKey = server.ApiKey.Value;
            _includeApiKey = server.ApiKey.IsIncluded;
            _certificatePath = server.CertificatePath.Value;
            _includeCertificatePath = server.CertificatePath.IsIncluded;
            _certificatePassword = server.CertificatePassword.Value;
            _includeCertificatePassword = server.CertificatePassword.IsIncluded;
            _hostNamesText = server.HostNames.Value;
            _includeHostNames = server.HostNames.IsIncluded;
            _logRetentionText = server.LogRetention.Value.ToString();
            _includeLogRetention = server.LogRetention.IsIncluded;
            BindAddressChoices = BuildBindAddressChoices(_bindAddress);
            RefreshWarning();
        }

        /// <inheritdoc />
        public event PropertyChangedEventHandler? PropertyChanged;

        /// <summary>Addresses offered in the bind combo, including this machine's addresses.</summary>
        public IReadOnlyList<string> BindAddressChoices { get; }

        /// <summary>Shown under the form. Listener changes wait for the next start.</summary>
        public string ListenerNote { get; } = "Listener changes apply the next time the server starts.";

        /// <summary>HTTP port text.</summary>
        public string HttpPortText
        {
            get { return _httpPortText; }
            set
            {
                if (Set(ref _httpPortText, value ?? ""))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>True when the HTTP port is written.</summary>
        public bool IncludeHttpPort
        {
            get { return _includeHttpPort; }
            set
            {
                if (Set(ref _includeHttpPort, value))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>HTTPS port text.</summary>
        public string HttpsPortText
        {
            get { return _httpsPortText; }
            set
            {
                if (Set(ref _httpsPortText, value ?? ""))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>True when the HTTPS port is written.</summary>
        public bool IncludeHttpsPort
        {
            get { return _includeHttpsPort; }
            set
            {
                if (Set(ref _includeHttpsPort, value))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>True when HTTPS is enabled.</summary>
        public bool EnableHttps
        {
            get { return _enableHttps; }
            set
            {
                if (Set(ref _enableHttps, value))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>True when enableHttps is written.</summary>
        public bool IncludeEnableHttps
        {
            get { return _includeEnableHttps; }
            set
            {
                if (Set(ref _includeEnableHttps, value))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>Bind address.</summary>
        public string BindAddress
        {
            get { return _bindAddress; }
            set
            {
                if (Set(ref _bindAddress, value ?? ""))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>True when the bind address is written.</summary>
        public bool IncludeBindAddress
        {
            get { return _includeBindAddress; }
            set
            {
                if (Set(ref _includeBindAddress, value))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>API key.</summary>
        public string ApiKey
        {
            get { return _apiKey; }
            set
            {
                if (Set(ref _apiKey, value ?? ""))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>True when the API key is written.</summary>
        public bool IncludeApiKey
        {
            get { return _includeApiKey; }
            set
            {
                if (Set(ref _includeApiKey, value))
                {
                    RefreshWarning();
                }
            }
        }

        /// <summary>True when the API key is shown as plain text.</summary>
        public bool ShowApiKey
        {
            get { return _showApiKey; }
            set
            {
                if (Set(ref _showApiKey, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ApiKeyVisibilityText)));
                }
            }
        }

        /// <summary>Show or Hide.</summary>
        public string ApiKeyVisibilityText
        {
            get { return ShowApiKey ? "Hide" : "Show"; }
        }

        /// <summary>Certificate file.</summary>
        public string CertificatePath
        {
            get { return _certificatePath; }
            set { Set(ref _certificatePath, value ?? ""); }
        }

        /// <summary>True when the certificate path is written.</summary>
        public bool IncludeCertificatePath
        {
            get { return _includeCertificatePath; }
            set { Set(ref _includeCertificatePath, value); }
        }

        /// <summary>Certificate password.</summary>
        public string CertificatePassword
        {
            get { return _certificatePassword; }
            set { Set(ref _certificatePassword, value ?? ""); }
        }

        /// <summary>True when the certificate password is written.</summary>
        public bool IncludeCertificatePassword
        {
            get { return _includeCertificatePassword; }
            set { Set(ref _includeCertificatePassword, value); }
        }

        /// <summary>Host names, one per line.</summary>
        public string HostNamesText
        {
            get { return _hostNamesText; }
            set { Set(ref _hostNamesText, value ?? ""); }
        }

        /// <summary>True when host names are written.</summary>
        public bool IncludeHostNames
        {
            get { return _includeHostNames; }
            set { Set(ref _includeHostNames, value); }
        }

        /// <summary>How many log lines to keep.</summary>
        public string LogRetentionText
        {
            get { return _logRetentionText; }
            set { Set(ref _logRetentionText, value ?? ""); }
        }

        /// <summary>True when log retention is written.</summary>
        public bool IncludeLogRetention
        {
            get { return _includeLogRetention; }
            set { Set(ref _includeLogRetention, value); }
        }

        /// <summary>Explains that log retention does not wait for a restart.</summary>
        public string LogRetentionNote { get; } = "Log retention applies when you press OK. It does not restart the listener.";

        /// <summary>Validation message. Empty when the form is valid.</summary>
        public string ErrorText
        {
            get { return _errorText; }
            private set { Set(ref _errorText, value); }
        }

        /// <summary>Warning for a network bind that cannot start yet.</summary>
        public string WarningText
        {
            get { return _warningText; }
            private set { Set(ref _warningText, value); }
        }

        /// <summary>
        /// Builds the bind-address list: loopback, every interface, and this machine's addresses.
        /// </summary>
        /// <param name="current">Address to keep even when it is not in the detected list.</param>
        /// <returns>The choices.</returns>
        public static IReadOnlyList<string> BuildBindAddressChoices(string? current)
        {
            var choices = new List<string> { "127.0.0.1", "0.0.0.0", "::1", "::" };
            try
            {
                foreach (IPAddress address in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (IPAddress.IsLoopback(address))
                    {
                        continue;
                    }

                    if (address.AddressFamily != AddressFamily.InterNetwork
                        && address.AddressFamily != AddressFamily.InterNetworkV6)
                    {
                        continue;
                    }

                    string text = address.ToString();
                    if (!choices.Contains(text))
                    {
                        choices.Add(text);
                    }
                }
            }
            catch (Exception)
            {
            }

            if (!string.IsNullOrWhiteSpace(current) && !choices.Contains(current))
            {
                choices.Add(current);
            }

            return choices;
        }

        /// <summary>
        /// Replaces the API key with 32 random characters and marks the field to be saved.
        /// </summary>
        public void GenerateApiKey()
        {
            ApiKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24));
            IncludeApiKey = true;
            ShowApiKey = true;
        }

        /// <summary>Switches the API key between plain text and a password box.</summary>
        public void ToggleApiKeyVisibility()
        {
            ShowApiKey = !ShowApiKey;
        }

        /// <summary>
        /// Validates the fields and applies the document.
        /// </summary>
        /// <returns>True when the document was applied.</returns>
        public bool TryCommit()
        {
            if (!TryValidate(out string error))
            {
                ErrorText = error;
                return false;
            }

            WriteToDraft();
            ConfigEditResult result = _apply(_draft);
            if (!result.Success)
            {
                ErrorText = Describe(result);
                return false;
            }

            ErrorText = "";
            return true;
        }

        private void WriteToDraft()
        {
            ServerDraft server = _draft.Server;
            server.HttpPort.IsIncluded = IncludeHttpPort;
            if (TryParsePort(HttpPortText, out int httpPort))
            {
                server.HttpPort.Value = httpPort;
            }

            server.HttpsPort.IsIncluded = IncludeHttpsPort;
            if (TryParsePort(HttpsPortText, out int httpsPort))
            {
                server.HttpsPort.Value = httpsPort;
            }

            server.EnableHttps.IsIncluded = IncludeEnableHttps;
            server.EnableHttps.Value = EnableHttps;
            server.BindAddress.IsIncluded = IncludeBindAddress;
            server.BindAddress.Value = BindAddress.Trim();
            server.ApiKey.IsIncluded = IncludeApiKey;
            server.ApiKey.Value = ApiKey;
            server.CertificatePath.IsIncluded = IncludeCertificatePath;
            server.CertificatePath.Value = CertificatePath.Trim();
            server.CertificatePassword.IsIncluded = IncludeCertificatePassword;
            server.CertificatePassword.Value = CertificatePassword;
            server.HostNames.IsIncluded = IncludeHostNames;
            server.HostNames.Value = HostNamesText;
            server.LogRetention.IsIncluded = IncludeLogRetention;
            if (TryParseRetention(LogRetentionText, out int retention))
            {
                server.LogRetention.Value = retention;
            }
        }

        private bool TryValidate(out string error)
        {
            if (IncludeHttpPort && !TryParsePort(HttpPortText, out _))
            {
                error = "HTTP port must be an integer from 1 to 65535.";
                return false;
            }

            if (IncludeHttpsPort && !TryParsePort(HttpsPortText, out _))
            {
                error = "HTTPS port must be an integer from 1 to 65535.";
                return false;
            }

            int httpPort = EffectivePort(IncludeHttpPort, HttpPortText, 5000);
            int httpsPort = EffectivePort(IncludeHttpsPort, HttpsPortText, 5001);
            if (EffectiveHttps() && httpPort == httpsPort)
            {
                error = "The HTTPS port must differ from the HTTP port when HTTPS is enabled.";
                return false;
            }

            if (IncludeBindAddress && !string.IsNullOrWhiteSpace(BindAddress) && !IPAddress.TryParse(BindAddress.Trim(), out _))
            {
                error = "Bind address must be an IP address.";
                return false;
            }

            if (IncludeApiKey && !string.IsNullOrEmpty(ApiKey) && ApiKey.Length < 16)
            {
                error = "API key must be empty or at least 16 characters.";
                return false;
            }

            if (IncludeLogRetention && !TryParseRetention(LogRetentionText, out _))
            {
                error = "Log retention must be an integer from "
                    + ServerSettings.MinimumLogRetention
                    + " to "
                    + ServerSettings.MaximumLogRetention
                    + ".";
                return false;
            }

            error = "";
            return true;
        }

        private void RefreshWarning()
        {
            string address = EffectiveBindAddress();
            bool network = IPAddress.TryParse(address, out IPAddress? parsed) && parsed != null && !IPAddress.IsLoopback(parsed);
            bool missingProtection = !EffectiveHttps() || string.IsNullOrEmpty(EffectiveApiKey());
            WarningText = network && missingProtection
                ? "A bind address that is not loopback requires HTTPS and an API key before the server can start."
                : "";
        }

        private bool EffectiveHttps()
        {
            return IncludeEnableHttps && EnableHttps;
        }

        private string EffectiveBindAddress()
        {
            if (!IncludeBindAddress || string.IsNullOrWhiteSpace(BindAddress))
            {
                return "127.0.0.1";
            }

            return BindAddress.Trim();
        }

        private string EffectiveApiKey()
        {
            return IncludeApiKey ? ApiKey : "";
        }

        private static int EffectivePort(bool included, string text, int fallback)
        {
            if (included && TryParsePort(text, out int port))
            {
                return port;
            }

            return fallback;
        }

        private static bool TryParsePort(string text, out int port)
        {
            if (int.TryParse(text, out port) && port >= 1 && port <= 65535)
            {
                return true;
            }

            port = 0;
            return false;
        }

        private static bool TryParseRetention(string text, out int retention)
        {
            if (int.TryParse(text, out retention)
                && retention >= ServerSettings.MinimumLogRetention
                && retention <= ServerSettings.MaximumLogRetention)
            {
                return true;
            }

            retention = 0;
            return false;
        }

        private static string Describe(ConfigEditResult result)
        {
            if (!string.IsNullOrEmpty(result.Error))
            {
                return result.Error;
            }

            if (result.Errors.Count == 0)
            {
                return "The server settings could not be applied.";
            }

            var lines = new List<string>();
            foreach (ConfigValidationError error in result.Errors)
            {
                lines.Add(error.Message);
            }

            return string.Join(" ", lines);
        }

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }
    }
}
