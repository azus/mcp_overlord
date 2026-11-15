using System.Text.Json;
using System.Text.Json.Serialization;
using mcp_overlord.UI.Placement;

namespace mcp_overlord.UI
{
    /// <summary>
    /// Window layout remembered across runs: bounds, splitters, the panel under the log, log columns, sort, and the level filter.
    /// </summary>
    public sealed class UiSettings
    {
        /// <summary>Window left edge, in device-independent pixels. Kept so older files still load.</summary>
        public double Left { get; set; } = double.NaN;

        /// <summary>Window top edge, in device-independent pixels. Kept so older files still load.</summary>
        public double Top { get; set; } = double.NaN;

        /// <summary>Window width, in device-independent pixels. Kept so older files still load.</summary>
        public double Width { get; set; } = 1100;

        /// <summary>Window height, in device-independent pixels. Kept so older files still load.</summary>
        public double Height { get; set; } = 700;

        /// <summary>
        /// Outer bounds of the restored window, in physical pixels. Null in files written by older builds.
        /// </summary>
        public PixelRect? WindowBoundsPixels { get; set; }

        /// <summary><c>Normal</c> or <c>Maximized</c>.</summary>
        public string WindowState { get; set; } = "Normal";

        /// <summary>Applications column width in pixels.</summary>
        public double ApplicationsPanelWidth { get; set; } = 280;

        /// <summary>Height of the panel under the log, in pixels.</summary>
        public double BottomPanelHeight { get; set; } = 300;

        /// <summary>Selected tab under the log. 0 is detail, 1 is stats, 2 is help.</summary>
        public int BottomPanelTabIndex { get; set; }

        /// <summary>Selected log detail tab. 0 is the request, 1 the response, 2 the image.</summary>
        public int LogDetailTabIndex { get; set; }

        /// <summary><c>Json</c>, <c>Hex</c>, or <c>Ascii</c>.</summary>
        public string LogDetailBodyView { get; set; } = "Json";

        /// <summary>Log column widths keyed by sort member path.</summary>
        public Dictionary<string, double> LogColumnWidths { get; set; } = new Dictionary<string, double>();

        /// <summary>Sort member path of the log grid. Null when unsorted.</summary>
        public string? LogSortColumn { get; set; }

        /// <summary><c>Ascending</c> or <c>Descending</c>.</summary>
        public string LogSortDirection { get; set; } = "Ascending";

        /// <summary>Log level filter: All, Info, Warning, or Error.</summary>
        public string LogLevelFilter { get; set; } = "All";

        /// <summary>Last bounds of the application editor, in physical pixels.</summary>
        public PixelRect? ApplicationEditorBoundsPixels { get; set; }

        /// <summary>Last bounds of the server settings dialog, in physical pixels.</summary>
        public PixelRect? ServerSettingsBoundsPixels { get; set; }

        /// <summary>Last bounds of the connection report, in physical pixels.</summary>
        public PixelRect? ConnectionReportBoundsPixels { get; set; }

        /// <summary>Folder last used by a configuration file dialog.</summary>
        public string? LastConfigDirectory { get; set; }
    }

    /// <summary>
    /// Reads and writes <see cref="UiSettings"/> as JSON under LocalAppData.
    /// </summary>
    public sealed class UiSettingsStore
    {
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
        };

        /// <summary>
        /// Default path: <c>%LOCALAPPDATA%\mcp_overlord\ui_settings.json</c>.
        /// </summary>
        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "mcp_overlord",
                    "ui_settings.json");
            }
        }

        /// <summary>
        /// Loads settings. A missing or unreadable file returns defaults.
        /// </summary>
        /// <param name="path">JSON file.</param>
        /// <returns>The settings.</returns>
        public UiSettings Load(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    return new UiSettings();
                }

                string json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<UiSettings>(json, Options) ?? new UiSettings();
            }
            catch (Exception)
            {
                return new UiSettings();
            }
        }

        /// <summary>
        /// Writes settings, creating the directory when needed.
        /// </summary>
        /// <param name="path">JSON file.</param>
        /// <param name="settings">Settings to write.</param>
        public void Save(string path, UiSettings settings)
        {
            if (settings == null)
            {
                throw new ArgumentNullException(nameof(settings));
            }

            string? directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(path, JsonSerializer.Serialize(settings, Options));
        }
    }
}
