using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using mcp_overlord.Configuration;
using mcp_overlord.Logging;
using mcp_overlord.Services;
using mcp_overlord.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using LogLevel = mcp_overlord.Logging.LogLevel;
using ModelContextProtocol.Protocol;

namespace mcp_overlord.Hosting
{
    /// <summary>Starts and stops the MCP listener.</summary>
    public interface IServerHost
    {
        /// <summary>True after a successful start and before stop.</summary>
        bool IsRunning { get; }

        /// <summary>
        /// Opens the listener described by <paramref name="settings"/>.
        /// </summary>
        /// <param name="settings">Bind address, ports, certificate, and API key.</param>
        /// <param name="cancellationToken">Cancels the start.</param>
        Task StartAsync(ServerSettings settings, CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes the listener and releases the port.
        /// The stop does not use the caller's synchronization context.
        /// </summary>
        Task StopAsync();
    }

    /// <summary>
    /// Kestrel host for the MCP endpoint. HTTP is always available. HTTPS is added when the settings ask for it.
    /// </summary>
    public sealed class McpHost : IServerHost, IAsyncDisposable
    {
        /// <summary>How long a window close waits for <see cref="StopAsync"/> before the process exits.</summary>
        public static readonly TimeSpan CloseWaitTimeout = TimeSpan.FromSeconds(5);

        private static readonly TimeSpan HostShutdownTimeout = TimeSpan.FromSeconds(3);

        private readonly ConfigStore _configStore;
        private readonly WindowLocator _windowLocator;
        private readonly AppLauncher _appLauncher;
        private readonly CaptureService _captureService;
        private readonly ScreenshotScaleStore _scaleStore;
        private readonly InputService _inputService;
        private readonly IWorkstationLock _workstationLock;
        private readonly LogBuffer _log;
        private readonly StatsCounter _stats;
        private readonly InFlightCounter _inFlight;
        private readonly SelfSignedCertificateStore _certificates;
        private readonly object _gate = new object();
        private WebApplication? _application;
        private X509Certificate2? _certificate;

        /// <summary>
        /// Creates the host. The same service instances are placed in the web application's container on each start.
        /// </summary>
        public McpHost(
            ConfigStore configStore,
            WindowLocator windowLocator,
            AppLauncher appLauncher,
            CaptureService captureService,
            ScreenshotScaleStore scaleStore,
            InputService inputService,
            IWorkstationLock workstationLock,
            LogBuffer log,
            StatsCounter stats,
            InFlightCounter inFlight,
            SelfSignedCertificateStore? certificates = null)
        {
            _configStore = configStore;
            _windowLocator = windowLocator;
            _appLauncher = appLauncher;
            _captureService = captureService;
            _scaleStore = scaleStore;
            _inputService = inputService;
            _workstationLock = workstationLock;
            _log = log;
            _stats = stats;
            _inFlight = inFlight;
            _certificates = certificates ?? new SelfSignedCertificateStore(SelfSignedCertificateStore.DefaultDirectory);
        }

        /// <inheritdoc />
        public bool IsRunning
        {
            get
            {
                lock (_gate)
                {
                    return _application != null;
                }
            }
        }

        /// <inheritdoc />
        public async Task StartAsync(ServerSettings settings, CancellationToken cancellationToken = default)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            lock (_gate)
            {
                if (_application != null)
                {
                    throw new InvalidOperationException("The server is already running.");
                }
            }

            WebApplication? application = null;
            X509Certificate2? certificate = null;
            try
            {
                if (settings.EnableHttps)
                {
                    settings = PrepareCertificate(settings);

                    // Ephemeral keys never reach Schannel on Windows: the TLS handshake closes immediately.
                    certificate = X509CertificateLoader.LoadPkcs12FromFile(
                        settings.Certificate!.Path!,
                        settings.Certificate.Password,
                        X509KeyStorageFlags.UserKeySet | X509KeyStorageFlags.Exportable);
                }

                application = Build(settings, certificate);
                await application.StartAsync(cancellationToken);
                lock (_gate)
                {
                    _application = application;
                    _certificate = certificate;
                }
            }
            catch
            {
                if (application != null)
                {
                    await application.DisposeAsync();
                }

                certificate?.Dispose();
                throw;
            }
        }

        /// <inheritdoc />
        public Task StopAsync()
        {
            using (ExecutionContext.SuppressFlow())
            {
                return Task.Run(StopCoreAsync);
            }
        }

        private async Task StopCoreAsync()
        {
            SynchronizationContext.SetSynchronizationContext(null);

            WebApplication? application;
            X509Certificate2? certificate;
            lock (_gate)
            {
                application = _application;
                certificate = _certificate;
                _application = null;
                _certificate = null;
            }

            if (application == null)
            {
                certificate?.Dispose();
                return;
            }

            try
            {
                await application.StopAsync().ConfigureAwait(false);
            }
            finally
            {
                await application.DisposeAsync().ConfigureAwait(false);
                certificate?.Dispose();
            }
        }

        /// <inheritdoc />
        public async ValueTask DisposeAsync()
        {
            await StopAsync();
        }

        private ServerSettings PrepareCertificate(ServerSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.Certificate?.Path))
            {
                return settings;
            }

            StoredCertificate stored = _certificates.Ensure(settings);
            IReadOnlyList<string> hostNames = settings.Certificate?.HostNames ?? Array.Empty<string>();
            string action = stored.Created ? "Created" : "Reused";
            _log.Append(
                LogLevel.Info,
                "server",
                action + " certificate " + stored.PfxPath + " (" + stored.Thumbprint + "). Public certificate " + stored.CerPath + ".");
            return settings with
            {
                Certificate = new CertificateSettings(stored.PfxPath, stored.Password, hostNames)
            };
        }

        private WebApplication Build(ServerSettings settings, X509Certificate2? certificate)
        {
            string[] urls = EndpointFormatter.BindUrls(settings);
            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                Args = Array.Empty<string>(),
                ContentRootPath = AppContext.BaseDirectory
            });

            builder.Logging.ClearProviders();
            builder.Services.AddHttpContextAccessor();
            builder.Services.Configure<HostOptions>(options =>
            {
                options.ShutdownTimeout = HostShutdownTimeout;
            });
            builder.WebHost.UseUrls(urls);
            if (certificate != null)
            {
                builder.WebHost.ConfigureKestrel(options =>
                {
                    options.ConfigureHttpsDefaults(https =>
                    {
                        https.ServerCertificate = certificate;
                    });
                });
            }

            builder.Services.AddSingleton(_configStore);
            builder.Services.AddSingleton(_windowLocator);
            builder.Services.AddSingleton(_appLauncher);
            builder.Services.AddSingleton(_captureService);
            builder.Services.AddSingleton(_scaleStore);
            builder.Services.AddSingleton(_inputService);
            builder.Services.AddSingleton(_workstationLock);
            builder.Services.AddSingleton(_log);
            builder.Services.AddSingleton(_stats);
            builder.Services.AddSingleton(_inFlight);
            builder.Services.AddSingleton(new ListenerSecurity(settings.ApiKey, EndpointFormatter.AllowedOrigins(settings)));

            LogBuffer log = _log;
            StatsCounter stats = _stats;
            IHttpContextAccessor? contextAccessor = null;
            builder.Services
                .AddMcpServer(options =>
                {
                    options.ServerInstructions = ServerInstructions.Text;
                    options.ServerInfo = new Implementation
                    {
                        Name = "mcp_overlord",
                        Version = "1.0.0"
                    };
                })
                .WithHttpTransport(options =>
                {
#pragma warning disable MCP9004
                    options.EnableLegacySse = true;
#pragma warning restore MCP9004
                    options.Stateless = false;
                })
                .WithToolsFromAssembly(typeof(ApplicationTools).Assembly, ToolSerializerOptions())
                .WithRequestFilters(filters =>
                {
                    filters.AddCallToolFilter(next => async (request, cancellationToken) =>
                    {
                        return await ToolCallLog.Invoke(
                            log,
                            stats,
                            request.Params,
                            () => next(request, cancellationToken),
                            exchange: () => CurrentExchange(contextAccessor));
                    });
                });

            WebApplication application = builder.Build();
            contextAccessor = application.Services.GetRequiredService<IHttpContextAccessor>();
            application.UseMiddleware<TrafficCaptureMiddleware>();
            application.UseMiddleware<ApiKeyMiddleware>();
            application.UseMiddleware<OriginCheckMiddleware>();
            application.UseMiddleware<InFlightMiddleware>();
            application.MapMcp("/mcp");
            return application;
        }

        private static HttpExchange? CurrentExchange(IHttpContextAccessor? accessor)
        {
            HttpExchange? current = HttpExchangeAmbient.Exchange;
            if (current != null)
            {
                return current;
            }

            HttpContext? context = accessor?.HttpContext;
            if (context != null
                && context.Items.TryGetValue(HttpExchange.ItemKey, out object? value)
                && value is HttpExchange exchange)
            {
                return exchange;
            }

            return null;
        }

        private static JsonSerializerOptions ToolSerializerOptions()
        {
            return new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                TypeInfoResolver = new DefaultJsonTypeInfoResolver()
            };
        }
    }
}
