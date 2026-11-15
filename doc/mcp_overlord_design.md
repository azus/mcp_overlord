# mcp_overlord — As-built specification

This document describes the `mcp_overlord` Windows application as it is built: `src/mcp_overlord` (WPF, .NET 9) plus the desktop operations in `src/automation_util`. It is the contract for the product. A new implementation that follows this document, without the existing source, produces the same server, the same tools, the same configuration rules, and the same window.

The application hosts an MCP server so an LLM client (Cursor, a Grok bot, or any other client that speaks MCP over HTTP) can see a desktop application and drive it. Typical targets are games. The server captures the target window, returns the image to the model, and applies the mouse and keyboard actions the model asks for.

## 1. Overview and goals

### Purpose

`mcp_overlord` is a Windows desktop process that does these things at once:

1. It shows a control window: configuration editing, server start and stop, a live log with a detail view, stats, help, and a connection check.
2. It hosts an MCP server on HTTP and, when configured, HTTPS.
3. It performs the window, screenshot, mouse, and keyboard operations that the MCP tools request, using `automation_util` and a small native PNG encoder.
4. It remembers window placement, panel sizes, and log layout across runs.
5. It installs as a per-machine product with a Start menu shortcut, a `.overlord` file association, and a jump list.

The model decides what to do. The server executes the requested action on a configured application and reports what happened.

### Goals

- Speak MCP over HTTP, and over HTTPS when HTTPS is enabled. A missing certificate file is created and reused.
- Accept connections from Cursor and from a Grok bot (local or remote).
- Take a screenshot of a configured application's window and return it as an MCP image. The default full-window PNG uses Windows Graphics Capture and, when the native encoder is present, an uncompressed PNG.
- Apply mouse move, click, button down, button up, and scroll.
- Apply key press, key down, key up, and text entry.
- Locate a window from the configured window title and the process name derived from the entry.
- Launch a configured executable, with optional arguments, when the client asks.
- Answer `list_applications` with every configured application. `name` is mandatory in the file.
- Answer a description request with the configured description when one is present.
- Load the configuration from `--config` or a positional path and auto-start the server, or create, load, and save it from the UI.
- Validate every configuration file against a versioned JSON Schema shipped as an embedded resource.
- Show a dark window, including a dark title bar, and a dark blue title bar while the process is elevated.
- Offer Restart as Administrator, a connection check, and a log detail view of each HTTP exchange.
- Persist the window, the splitters, the log columns, and the dialog positions.
- Ship an installer that registers the file type and the Start menu jump list.

### Out of scope

- Deciding which button to press. Image search (`AutomationUtil.find_matches` and related helpers) stays in the library and is a future tool, described in section 19.
- Accounts, roles, or more than one operator. One process serves every connected client against the same desktop session.
- Platforms other than Windows.
- Driving a window that is not listed in the configuration. Launch paths come only from the configuration file.
- Recording and replaying macros.
- A file log. The log lives in memory.
- An unlock tool. The operator unlocks the screen at the console.
- Listing monitors or moving a window between them.

### Users

| User | How they reach the server |
| --- | --- |
| A person at the machine | Starts the WPF app, loads or edits a config, presses Start, watches the log and the stats. |
| Cursor on the same machine | An `mcp.json` entry whose `url` is the local MCP endpoint. |
| A Grok bot, or any remote MCP client | HTTPS on a reachable address, with an API key. A tunnel to the loopback port is the other supported path. |

## 2. Context and architecture

The WPF process owns the UI thread. Starting the server builds a generic host and Kestrel. Kestrel serves the MCP endpoint. Tool handlers call services. Those services call `automation_util` and Win32. Every tool call publishes a log event and updates counters. The UI reads those on the dispatcher. There is no separate event-bus type: `LogBuffer` raises `EntriesAdded`, and the view model pulls a snapshot.

```mermaid
flowchart LR
  subgraph clients [Clients]
    cursor[Cursor]
    grok[GrokBot]
  end

  subgraph process [mcp_overlord process]
    kestrel[Kestrel]
    mcp["MCP endpoint /mcp"]
    tools[ToolLayer]
    locator[WindowLocator]
    launcher[AppLauncher]
    capture[CaptureService]
    inputSvc[InputService]
    gate[InputGate]
    log[LogBuffer]
    ui[WpfWindow]
  end

  util["automation_util"]
  snap["snapenc.dll"]

  cursor --> kestrel
  grok --> kestrel
  kestrel --> mcp --> tools
  tools --> locator
  tools --> launcher
  tools --> capture
  tools --> inputSvc
  inputSvc --> gate
  locator --> util
  launcher --> util
  capture --> util
  capture --> snap
  gate --> util
  tools --> log --> ui
```

A typical turn from the model's point of view:

```mermaid
sequenceDiagram
  participant Model
  participant Mcp as McpEndpoint
  participant Capture as CaptureService
  participant Input as InputService
  participant Win as TargetWindow

  Model->>Mcp: tools/call take_screenshot
  Mcp->>Capture: capture window rectangle
  Capture->>Win: Windows Graphics Capture, else window DC, else PrintWindow, else screen blit
  Capture-->>Mcp: png plus scale
  Mcp-->>Model: image content and metadata
  Model->>Mcp: tools/call mouse_click x y
  Mcp->>Input: map image coords to window then client coords
  Input->>Win: focus, move, button down, button up
  Input-->>Mcp: applied point
  Mcp-->>Model: result text
```

### Technology choices

| Concern | Choice |
| --- | --- |
| UI | WPF. Project SDK `Microsoft.NET.Sdk`, `OutputType` `WinExe`, `UseWPF` true, `Nullable` enable, `ImplicitUsings` enable, `AllowUnsafeBlocks` true. |
| Target framework | `net9.0-windows10.0.22621.0`. Assembly version `1.0.0`. Root namespace `mcp_overlord`. |
| Manifest | `app.manifest`. `dpiAware` true and `dpiAwareness` `PerMonitorV2`. No `requestedExecutionLevel`. |
| Icon | `Resources/Icons/app.ico` is the application icon. |
| HTTP server | Kestrel, hosted with `Microsoft.Extensions.Hosting` inside the WPF process. The project adds `<FrameworkReference Include="Microsoft.AspNetCore.App" />`. |
| MCP | `ModelContextProtocol.AspNetCore` 2.2.0. Tools are registered with the SDK. The app does not implement the JSON-RPC framing itself. |
| Schema | `JsonSchema.Net` 9.4.0, draft 2020-12. |
| Icons | `SharpVectors.Wpf` 1.8.6. Each toolbar icon is an SVG drawn by `SvgViewbox`, sized to the parent `FontSize`. |
| Native encoder | `snapenc.dll`, built from `Native/snap.cpp` and `Native/fpng.cpp` when the Visual C++ x64 toolset is installed. Section 15. |
| Desktop operations | `automation_util`: `WindowSearch`, `ScreenshotGenerator`, `ClickHelper` and its two implementations, `KeyboardUtil`, `KeyCode`, `MouseInputEncoder`, `KeyboardInputEncoder`, `NativeInput`. |
| Tests | `Microsoft.Testing.Platform` (`global.json`). Internals are visible to `mcp_overlord.tests`. |

### MCP host

`McpHost` implements `IServerHost` (`IsRunning`, `StartAsync`, `StopAsync`) and `IAsyncDisposable`.

Registration, in this order:

1. `WebApplication.CreateBuilder` with an empty argument list and `ContentRootPath` set to `AppContext.BaseDirectory`. Logging providers are cleared. `AddHttpContextAccessor` is called.
2. `UseUrls` receives the bind URLs from `EndpointFormatter`. When a certificate was loaded, Kestrel's HTTPS defaults use that certificate.
3. Shared services are registered as singletons: `ConfigStore`, `WindowLocator`, `AppLauncher`, `CaptureService`, `ScreenshotScaleStore`, `InputService`, `IWorkstationLock`, `LogBuffer`, `StatsCounter`, `InFlightCounter`, and a `ListenerSecurity` snapshot (API key and allowed origins captured at start).
4. MCP:

```text
AddMcpServer(options =>
{
    options.ServerInstructions = ServerInstructions.Text;
    options.ServerInfo = new Implementation { Name = "mcp_overlord", Version = "1.0.0" };
})
.WithHttpTransport(options =>
{
    options.EnableLegacySse = true;
    options.Stateless = false;
})
.WithToolsFromAssembly(typeof(ApplicationTools).Assembly, jsonOptions)
.WithRequestFilters(filters => filters.AddCallToolFilter(...));
```

`jsonOptions` is `JsonSerializerDefaults.Web` plus `DefaultJsonTypeInfoResolver`, so tool arguments bind with camelCase web defaults.

5. Middleware, outermost first: `TrafficCaptureMiddleware`, `ApiKeyMiddleware`, `OriginCheckMiddleware`, `InFlightMiddleware`, then `MapMcp("/mcp")`.

The transport is stateful. The legacy HTTP+SSE endpoints stay enabled so an older client can connect. Streamable HTTP on `POST`, `GET`, and `DELETE` `/mcp` is the transport documented for new clients. The protocol version the connection check sends is `2025-11-25`. Negotiated versions otherwise follow the SDK.

`HostOptions.ShutdownTimeout` is 3 seconds. `McpHost.CloseWaitTimeout` is 5 seconds; closing the window waits that long for stop.

The server instruction string, exactly:

> You control Windows applications through this server. Call list_applications to learn the names. Call take_screenshot before clicking. The image is the whole window, including the border and the title bar. A window that overlaps the target is not part of the image, and what is behind the window is not part of the image. Click coordinates are pixels of that image, origin at the top left of the window. coordinateSpace image (the default) uses those pixels, including scale and crop. coordinateSpace window_rect uses physical pixels of the window rectangle and ignores scale. A point outside the window is rejected and no mouse input is sent. The server focuses the target window before input. Launch only works for entries that have an executable. Read get_application_description when the list entry says a description exists. Call is_locked to see whether the workstation screen is locked. lock locks the screen. There is no unlock tool. While the screen is locked, mouse and keyboard tools fail and say why.

### Process model

- The WPF `Application` starts first. `ShutdownMode` is `OnMainWindowClose`.
- Starting the server builds the generic host and calls `StartAsync`. Stopping it calls `StopAsync` and disposes the host so the port is released. Stop runs on a thread-pool thread with the synchronization context suppressed, so a blocked UI thread can wait without deadlocking.
- Kestrel request threads run tool handlers. Handlers may block for the configured press delay (tens of milliseconds, up to the clamp in section 6). They must not touch WPF objects.
- UI updates hop to the dispatcher. The log buffer and the stats counters are safe to update from several threads (section 10).
- An `InputGate` holds one lock per target window. Mouse and keyboard actions for the same window run one at a time, so a click's down/up pair cannot interleave with another client's keys. Different windows may be driven in parallel. Lock objects are created on demand and kept for the life of the process.
- Tool handlers capture an immutable snapshot of the loaded configuration at the start of the call (`ConfigStore.Capture`). A config reload does not change a call that has already started.
- Every tool call is raced against a 15 second deadline inside `ToolCallLog`. On timeout the client receives an error and the call keeps running in the background (section 10).

### Composition

`AppComposition.Create(Dispatcher)` wires the object graph by hand. There is no dependency-injection container for the WPF side. The ASP.NET host receives the same instances as singletons when the server starts.

Platform implementations created at composition time:

- `Win32WindowEnumerator`, `Win32ProcessQuery`, `Win32ProcessStarter`
- `Win32WindowApi`, `Win32WindowCapture`, `Win32KeyboardSender`
- `Win32FocusApi`, `Win32IntegrityApi`, `ThreadDelay`
- `MouseEventClickHelper`, `SendInputClickHelper`, `Win32WorkstationLock`
- `WpfUserInteraction`, `WpfClipboard`, `DispatcherUiInvoker`, `ShellRecentFileRegistry`

The graph then builds `LogBuffer`, `StatsCounter`, `InFlightCounter`, `ConfigLoader`, `ConfigStore`, `WindowLocator`, `AppLauncher`, `CaptureService`, `ScreenshotScaleStore`, `ClickBackends`, `FocusHelper`, `IntegrityChecker`, `InputGate`, `InputService`, `ServerStateMachine`, `McpHost`, `ConfigEditorSession`, `ElevationService`, `MainViewModel`, and `UiSettingsStore`.

`ConfigStore.Changed` clears the `WindowLocator` cache, including on bind and unbind.

### Types

| Type | Responsibility |
| --- | --- |
| `ConfigLoader` | Read a path, check `schemaVersion`, validate, enforce the extra rules in section 3, return a `ConfigDocument` or a list of errors. |
| `ConfigStore` | Hold the active document, the listener snapshot, and the pending-listener flag. `Capture` returns the snapshot a tool call keeps. |
| `ConfigDraft`, `ConfigEditorSession`, `ConfigDraftSerializer` | Editable document, dirty tracking, and the JSON the Save button writes. |
| `McpHost` | Build, start, and stop the generic host. Apply bind address, ports, HTTPS certificate, and the API key middleware. |
| `EndpointFormatter` | Public URLs, bind URLs, allowed origins, and secret scrubbing. |
| `ApplicationTools` | `list_applications`, `get_application_description`, `launch_application`. |
| `WindowTools` | `find_window`, `focus_window`. |
| `CaptureTools` | `take_screenshot`. |
| `InputTools` | Mouse and keyboard tools. |
| `SessionTools` | `is_locked`, `lock`. |
| `WindowLocator` | Resolve a configured app to an `HWND`, with the cache in section 5. |
| `AppLauncher` | `Process.Start` for a configured executable, then optional polling. |
| `CaptureService` | Grab the window rectangle, optional crop, optional downscale, encode PNG or JPEG, detect a black client area. |
| `FastWindowSnapshot`, `WindowVisualCapture`, `NativeStoredPng` | Windows Graphics Capture, banded copy, and the native PNG encoder. |
| `InputService` | Translate image coordinates, focus the window, check the lock and integrity, and send keyboard through the gate. Mouse gestures go through the `ClickHelper` selected by `clickBackend`. |
| `CoordinateMapper`, `ScreenshotScaleStore` | Last screenshot scale and region, per application name. |
| `FocusHelper`, `IntegrityChecker`, `InputGate`, `KeyNameMap`, `ClickBackends` | Foreground, UIPI, per-window lock, key names, backend selection. |
| `LogBuffer`, `StatsCounter`, `ToolCallLog`, `TrafficCaptureMiddleware` | Log, counters, tool timing, HTTP capture. |
| `ConnectionDiagnostics` | The connection check in section 13. |
| `MainViewModel` and the dialog view models | Window behavior. |
| `UiSettingsStore`, `WindowPlacementBinder` | Persist and restore layout (section 14). |

## 3. Configuration file

### Document shape

Version 1 of the file is a single JSON object. Property names are camelCase. The file extension is `.overlord`. The contents stay JSON. `ConfigFile` defines:

| Constant | Value |
| --- | --- |
| `Extension` | `.overlord` |
| `DefaultFileName` | `config.overlord` |
| `DialogFilter` | `Overlord configuration (*.overlord)\|*.overlord\|All files (*.*)\|*.*` |

A complete file, written in the order the serializer uses:

```json
{
  "schemaVersion": 1,
  "server": {
    "bindAddress": "127.0.0.1",
    "enableHttps": true,
    "httpsPort": 5001,
    "httpPort": 5000,
    "apiKey": "replace-with-a-long-random-string",
    "logRetention": 1000,
    "certificate": {
      "path": "C:\\certs\\game-mcp.pfx",
      "password": "change-me",
      "hostNames": ["play.example.com"]
    }
  },
  "applications": [
    {
      "name": "Magicraid Starter",
      "executable": "C:\\Program Files (x86)\\InfiniteMagicraid\\Launcher.exe",
      "windowTitle": "InfiniteMagicRaid",
      "description": "Use this to launch the Infinite Magicraid game, press start button"
    },
    {
      "name": "Infinite Magicraid",
      "executable": "C:\\Program Files (x86)\\InfiniteMagicraid\\Games\\InfiniteMagicraid\\InfiniteMagicraid.exe",
      "parameters": "-logFile \"C:/Program Files (x86)/InfiniteMagicraid/output_log.txt\" gclid=none screenWidth=2560 screenHeight=1440 appName=InfiniteMagicraid",
      "workingDirectory": "C:\\Program Files (x86)\\InfiniteMagicraid\\Games\\InfiniteMagicraid",
      "windowTitle": "InfiniteMagicRaid",
      "processName": "InfiniteMagicraid",
      "clickBackend": "sendInput",
      "description": "Infinite Magicraid game"
    }
  ]
}
```

The serializer omits a server field whose Save checkbox is clear, and it omits the whole `server` object when every server field is clear. `logRetention` is omitted when it is the default 1000 and the field was not present in the loaded file. `clickBackend` is omitted when it is `mouseEvent` and the field was not present. A hand-written file may omit any optional property.

### 3.1 Field rules

**Root**

| Field | Required | Rule |
| --- | --- | --- |
| `schemaVersion` | yes | Integer. This build accepts only `1`. |
| `server` | no | Omitted means the defaults in the table below. |
| `applications` | yes | Array. May be empty, which yields a running server with no targets. Duplicate `name` values are rejected. |

**`server`**

| Field | Default | Rule |
| --- | --- | --- |
| `httpPort` | `5000` | Integer, 1–65535. |
| `httpsPort` | `5001` | Integer, 1–65535. When `enableHttps` is true, must differ from `httpPort`. |
| `enableHttps` | `false` | When `true`, HTTPS is added. `certificate.path` is optional. |
| `certificate.path` | none | Path of a PKCS#12 (`.pfx`) file. A relative path is resolved against the configuration file's directory and stored absolute, even when HTTPS is off. When set and HTTPS is enabled, the file must exist at load time. When omitted and HTTPS is enabled, start creates `%LOCALAPPDATA%\mcp_overlord\server.pfx` and reuses it. |
| `certificate.password` | `""` | Password passed to the certificate loader. The generated file uses an empty password. |
| `certificate.hostNames` | none | DNS names or IP addresses included in a generated certificate. Blank entries are dropped. The first entry is the copied HTTPS URL when `bindAddress` is `0.0.0.0` or `::`. |
| `bindAddress` | `127.0.0.1` | An IP address Kestrel can bind. An empty string uses the default. `127.0.0.1` and `::1` are loopback. `0.0.0.0` and `::` listen on every interface. `localhost` does not parse and is rejected. A value that is not loopback requires `enableHttps` and `apiKey` before start. |
| `apiKey` | absent | Empty or omitted means no key (`null` after load). Any other value has a minimum length of 16. The value is a shared secret, compared as a SHA-256 digest in constant time. Required when `bindAddress` is not loopback. |
| `logRetention` | `1000` | Integer, 100–1,000,000. How many log entries the window keeps. Applied as soon as the document is published. Changing it does not count as a listener change. |

HTTP is always available. HTTPS is added when `enableHttps` is true. Both listeners share the same MCP routes. When HTTPS is enabled and the bind address is not loopback, HTTP listens on `127.0.0.1` only and HTTPS listens on `bindAddress`.

When `enableHttps` is true and `certificate.path` is omitted, start writes a self-signed certificate at `%LOCALAPPDATA%\mcp_overlord\server.pfx` and a public `server.cer` beside it. The next start reuses that file when it still has a private key, expires more than 14 days later, its not-before time is no later than now plus 5 minutes, and its subject alternative names are a superset of the required set. Otherwise it writes a new pair and logs the thumbprint. The certificate is not added to a trust store. A supplied `certificate.path` is loaded instead. Certificate bytes are specified in section 8.

**`applications[]`**

| Field | Required | Rule |
| --- | --- | --- |
| `name` | yes | Non-empty string, unique in the file, compared case-sensitively. This is the id every tool uses. |
| `executable` | yes | Path of an `.exe`. Relative paths are resolved against the configuration file's directory at load time and stored absolute. The path must include a file name. |
| `parameters` | no | A single command-line string. Quotes are preserved. Passed through `ProcessStartInfo.Arguments`. An empty string is treated as not set. |
| `workingDirectory` | no | A non-empty value is resolved against the configuration file's directory. An empty string, or an omitted field, uses the directory that contains `executable`. |
| `windowTitle` | yes | Substring matched against the window title, ordinal case-insensitive. |
| `processName` | no | Compared to the executable file name without extension, ordinal case-insensitive. When omitted or empty, location uses the executable's file name without extension (`EffectiveProcessName`). |
| `description` | no | Free text returned by `get_application_description`. An empty string is treated as not set. |
| `clickBackend` | no | `mouseEvent` (default) or `sendInput`. Anything other than the exact string `sendInput` is `mouseEvent`. Selects the `ClickHelper` implementation used for this application's mouse tools. Section 7 defines both. |

`name`, `executable`, and `windowTitle` are required on every entry. The editor writes an optional field only when its Save checkbox is checked, and it may write that field as an empty string. The loader treats an empty optional string as not set (`null` on the document).

`name` uniqueness is checked in code after the schema succeeds and reported as a validation error on the same path format (`/applications/1/name`).

Port clash (`httpPort` equals `httpsPort` while HTTPS is enabled) is the same kind of extra rule.

`ServerSettings.HasSameListener` compares ports, the HTTPS flag, bind address, API key, certificate path, certificate password, and host names (order-sensitive, case-insensitive). `logRetention` is excluded, so changing it never asks for a restart.

`ApplicationEntry.IsLaunchable` is true when `Executable` is non-empty. A document that passed the schema always has an executable, so a loaded entry is launchable. `NotLaunchable` remains the error `AppLauncher` returns if an empty executable is ever presented.

### 3.2 Schema file

The schema lives at `src/mcp_overlord/Resources/config.schema.json`. The project embeds it as an `EmbeddedResource` with logical name `mcp_overlord.Resources.config.schema.json`. The loader reads it from the assembly. A missing resource throws `SchemaResourceMissingException` with the message `The embedded configuration schema was not found. The application cannot validate configuration.` Any other failure to build the schema is wrapped with `The embedded configuration schema could not be loaded. The application cannot validate configuration.` Startup shows that message in a dialog and exits with code 1. The main window is not created, and nothing is written to the log.

The schema uses draft 2020-12. `schemaVersion` is `const: 1`. The loader still inspects `schemaVersion` before full validation so the error names the supported version.

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "$id": "https://game-automation.local/schemas/game-mcp-control/config-v1.json",
  "title": "mcp_overlord configuration",
  "type": "object",
  "additionalProperties": false,
  "required": ["schemaVersion", "applications"],
  "properties": {
    "schemaVersion": { "const": 1 },
    "server": { "$ref": "#/$defs/server" },
    "applications": {
      "type": "array",
      "items": { "$ref": "#/$defs/application" }
    }
  },
  "$defs": {
    "server": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "httpPort": { "type": "integer", "minimum": 1, "maximum": 65535, "default": 5000 },
        "httpsPort": { "type": "integer", "minimum": 1, "maximum": 65535, "default": 5001 },
        "enableHttps": { "type": "boolean", "default": false },
        "certificate": { "$ref": "#/$defs/certificate" },
        "bindAddress": { "type": "string", "default": "127.0.0.1" },
        "apiKey": {
          "anyOf": [
            { "type": "string", "maxLength": 0 },
            { "type": "string", "minLength": 16 }
          ]
        },
        "logRetention": { "type": "integer", "minimum": 100, "maximum": 1000000, "default": 1000 }
      }
    },
    "certificate": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "path": { "type": "string" },
        "password": { "type": "string" },
        "hostNames": {
          "type": "array",
          "items": { "type": "string", "minLength": 1 }
        }
      }
    },
    "application": {
      "type": "object",
      "additionalProperties": false,
      "required": ["name", "executable", "windowTitle"],
      "properties": {
        "name": { "type": "string", "minLength": 1 },
        "executable": { "type": "string", "minLength": 1 },
        "parameters": { "type": "string" },
        "workingDirectory": { "type": "string" },
        "windowTitle": { "type": "string", "minLength": 1 },
        "processName": { "type": "string" },
        "description": { "type": "string" },
        "clickBackend": { "enum": ["mouseEvent", "sendInput"], "default": "mouseEvent" }
      }
    }
  }
}
```

The schema is built with `JsonSchema.FromText` and a fresh `SchemaRegistry`. Evaluation uses `OutputFormat.List`. Errors are collected recursively through `Details`. Duplicate path-and-message pairs are dropped. An instance location of `#` or empty becomes `/`. The message is the error value, or the keyword name when the value is empty. When the document is invalid and no errors were collected, the single error is `/` with `The configuration does not match the schema.` Schema failures return immediately, so the extra rules below do not run on a schema failure.

`ConfigLoader` then checks, and reports every failure together:

| Path | Message |
| --- | --- |
| `/server/httpsPort` | `httpsPort must differ from httpPort when HTTPS is enabled.` |
| `/server/bindAddress` | `bindAddress must be an IP address.` (`IPAddress.TryParse`) |
| `/server/certificate/path` | `Certificate file was not found: {path}` when HTTPS is enabled and a path is set |
| `/server/logRetention` | `logRetention must be an integer from 100 to 1000000.` |
| `/applications/{i}/name` | `name is required.` or `Application name '{name}' is duplicated.` |
| `/applications/{i}/executable` | `executable is required.` or `executable must include a file name.` |
| `/applications/{i}/windowTitle` | `windowTitle is required.` |

File-level failures, before schema evaluation:

| Situation | Path | Message |
| --- | --- | --- |
| File missing | the full path, not a JSON pointer | `Configuration file was not found: {fullPath}` |
| Read throws | the full path | `The configuration file could not be read: {message}` |
| JSON parse failure | `/` | `The configuration file is not valid JSON: {message}` |

A missing executable file is a warning, not an error: `Executable was not found: {exe}. Launch will fail until the file is present.` It is recorded in the log. Launch fails only when a client calls `launch_application`. That keeps a config usable for window control when the game is already running and the install path has moved.

`ConfigLoadResult` is `{ Document, Errors, Warnings }`. Success means a document exists and `Errors` is empty.

### 3.3 Validation and the UI

Every load, from `--config`, a positional path, or the Load button, uses `ConfigLoader`.

- Success: the document becomes the active config, the path is shown, the applications panel refreshes, the log records the path and the application count, and the file is added to the jump list recent category when it exists.
- Failure: the previous active config stays in place (or none, on first load). The log lists every error at level Error, category `config`. Status becomes Error when this load was an auto-start attempt. A short summary is shown in the status area: `Configuration invalid, N errors — see log` (em dash). A non-validation failure is counted as one error, and the grammar stays `1 errors`.

When `--config` points at an invalid file, auto-start does not run. The window still opens so the errors are visible.

Secrets in the log: the loader never writes `apiKey` or `certificate.password`. A successful load logs Info, category `config`: `Loaded {path} ({count} applications).` and then `Server bind {address}, HTTP {port}, HTTPS {port or disabled}, API key: {set or not set}, log retention {n}.` `EndpointFormatter.Scrub` replaces the API key and the certificate password with `***` using an ordinal comparison, and start-failure logs pass through that scrub.

### 3.4 Version handling

The loader reads the raw JSON, requires an integer `schemaVersion`, and selects a schema. The root must be an object, the property must be a JSON number, and `TryGetInt32` must succeed.

| Situation | Result |
| --- | --- |
| `schemaVersion` missing, or not an integer | Reject. Path `/`. Message: `schemaVersion is required and must be an integer. This build accepts schemaVersion 1.` |
| `schemaVersion` is an integer this build does not know | Reject. Path `/schemaVersion`. Message: `schemaVersion 2 is not supported. This build accepts schemaVersion 1.` |
| `schemaVersion` is 1 and the body fails the schema | Reject. One log line per schema error. |

This build has no migrator. Unknown future versions are rejected rather than guessed, so a newer file is never partially applied. The app rewrites a file only when the user saves, and the save writes the current draft, not a migrated copy of an older version.

### 3.5 Reload while the server is running

Server listener settings and the application list behave differently.

| Change | While Running |
| --- | --- |
| `applications`, `logRetention` | Applied immediately. The panel refreshes. Log retention is pushed to `LogBuffer` on publish. In-flight tool calls keep the snapshot they started with. The window-handle cache is cleared. |
| `httpPort`, `httpsPort`, `enableHttps`, `certificate`, `bindAddress`, `apiKey` | Stored as pending when `HasSameListener` is false. The log and the toolbar say `Listener settings take effect on the next start. The running endpoints still use the previous values.` The running endpoints still use the values captured at start (`ListenerSecurity` and the Kestrel URLs). |

The user can load or edit a file without stopping. They stop and start when they want the new ports, certificate, bind address, or API key. `MarkBound` on a successful start and `MarkUnbound` on stop clear the pending flag.

### 3.6 Editor model and the file the Save button writes

`ConfigDraft` is the editable copy. `SchemaVersion` is 1. `ServerDraft` and each `ApplicationDraft` hold the fields.

`DraftField<T>` is `{ Value, IsIncluded }`. `IsIncluded` is the Save checkbox. The serializer writes the property only when the box is checked, and it writes an empty string when the box is checked and the value is empty.

Server fields and their initial values, all unchecked on a brand-new draft: `HttpPort` 5000, `HttpsPort` 5001, `EnableHttps` false, `BindAddress` `127.0.0.1`, `ApiKey` empty, `CertificatePath` empty, `CertificatePassword` empty, `HostNames` empty (one name per line in the editor), `LogRetention` 1000.

Application fields: `Name`, `Executable`, and `WindowTitle` are plain strings and are always written. `Parameters`, `WorkingDirectory`, `ProcessName`, `Description`, and `ClickBackend` are `DraftField`s.

`ConfigDraft.FromDocument` marks every port, the HTTPS flag, and the bind address as included. It includes the API key, certificate path, password, and host names only when they are non-empty. It includes `LogRetention` only when it is not 1000, and `ClickBackend` only when it is not `MouseEvent`.

`ConfigDraftSerializer.Read` marks a property included whenever it is present in the file. It does not resolve paths. Parse errors throw.

`ConfigDraftSerializer.Write` uses `Utf8JsonWriter` with `Indented = true` (two spaces) and `UnsafeRelaxedJsonEscaping`, then appends a single `\n`. Property order:

- Root: `schemaVersion`, `server` (omitted when nothing is included), `applications`.
- Server: `bindAddress`, `enableHttps`, `httpsPort`, `httpPort`, `apiKey`, `logRetention`, `certificate`.
- Certificate, written when any of path, password, or host names is included: `path`, `password`, `hostNames`. Host names are split on CR and LF, trimmed, and blanks are dropped.
- Application: `name`, `executable`, `parameters`, `workingDirectory`, `windowTitle`, `processName`, `clickBackend`, `description`.

`ConfigEditorSession` tracks dirtiness by comparing `Write(draft)` with a baseline JSON string. Changing a value back to the baseline clears the dirty flag.

- `New` clears the path, resets the baseline, and applies an empty draft to the store. The store's `SourcePath` is empty. Relative paths in a new document are resolved against the process working directory, using `CWD\config.overlord` as the pretend source path during validation.
- `Open` validates with the loader, then reads the draft from the raw file. The baseline becomes the rewritten JSON, so a formatting difference alone is not dirty. On failure the current document stays.
- `TryApply` serializes, validates, clones the draft, and calls `store.Apply` so the running server sees the application list immediately. The baseline stays the last load or save, so the document becomes dirty.
- `Save` with no document returns `There is no configuration to save.` With no path it returns `NeedsPath`, and the UI asks for a path.
- `SaveAs` rejects an empty path with `A configuration path is required.` When the target folder differs from the previous file's folder (ordinal case-insensitive), `ConfigDraftRebase` rewrites relative certificate, executable, and working-directory paths to absolute paths against the old folder. It creates the directory and writes the text. A failure returns `The configuration could not be saved: {message}`. After a successful write it validates again and applies the document to the store, then sets the baseline so the document is clean.

Closing, New, and Load ask `Save changes to {name}?` with Don't save, Cancel, and Save. Cancel leaves the current document. Don't save drops the edits.

## 4. Startup, command line, and installer

### Arguments

Recognized tokens, matched exactly and case-sensitively, left to right:

| Argument | Meaning |
| --- | --- |
| `file.overlord` | Positional path. Same effect as `--config`. |
| `--config <path>` | Load this file after the window is created. On success, start the server as soon as the window is ready to show logs. |
| `--elevate` | Start an elevated instance and exit this process when that start succeeds. |
| `--register-shell` | Publish the Start menu jump list and exit. The installer runs this after install. |
| `--help`, `-h` | Print usage and exit before showing the window. |

Usage text, with CRLF line endings:

```text
mcp_overlord [file.overlord] [--config <path>] [--elevate] [--register-shell] [--help]

file.overlord      Load this file and start the server.
--config <path>    Load this file and start the server.
--elevate          Start an elevated instance and exit when it starts.
--register-shell   Rebuild the Start menu list and exit.
--help, -h         Show this help and exit.
```

Parsing rules:

- `--help` or `-h` returns immediately, so `--help --bad` shows help and `--bad --help` is an error.
- `--config` with nothing after it, or a blank value, is `Missing path after --config.`
- A second path, whether positional or `--config`, is `Only one configuration path can be supplied.`
- Any other token that starts with `-` is `Unknown argument '{arg}'.`
- An error result drops the path and the elevate flag. A valid path beside an unknown argument is not loaded.
- A relative path is resolved with `Path.GetFullPath` against the process working directory. A path that does not exist is a configuration error (auto-start blocked, window opens, log explains the missing file).

### Startup order

`App.OnStartup`:

1. `AppUserModel.SetCurrentProcess` calls `SetCurrentProcessExplicitAppUserModelID` with `game_automation_in_csharp.MCPOverlord`, before any window exists.
2. Base startup, `ShutdownMode = OnMainWindowClose`, and a `DispatcherUnhandledException` handler.
3. Parse arguments.
4. Help: `ParentConsole.WriteLine` calls `AttachConsole(-1)` and then `Console.WriteLine`. It does not call `FreeConsole`. When there is no parent console the write is skipped. Then `Shutdown(0)`.
5. `--register-shell`: publish the jump list, swallow every exception, `Shutdown(0)`.
6. `--elevate`: when the process integrity is already at least `0x3000` (high), continue in this process. Otherwise `ElevationService.RestartElevated` starts `Environment.ProcessPath` with `UseShellExecute` and verb `runas`. The configuration path, when present, is passed as a positional argument. Success leads to `Shutdown(0)`. Win32 error 1223 (the user cancelled the UAC prompt) continues in this process with no dialog. Any other failure shows `Could not start as administrator: {message}` and continues in this process.
7. Build `AppComposition`. `SchemaResourceMissingException` shows a dialog and calls `Shutdown(1)`.
8. Create `MainWindow`. On `Loaded`, publish the jump list. A failure is a Warning: `Could not publish the Start menu list: {message}`. A parse error is logged at Error, category `config`, and a dialog titled `MCP Overlord` appears. When a configuration path was supplied, `Loaded` calls `AutoStartAsync`.

An unhandled exception on the UI thread is logged in full, shown in a dialog, and ends the process with `Shutdown(1)`.

`OnExit`: when the host is running, wait up to `McpHost.CloseWaitTimeout` (5 seconds). On timeout the log records `The listener did not stop within 5 seconds. The process will still exit.`

### States

```mermaid
stateDiagram-v2
  [*] --> Stopped
  Stopped --> Starting: Start
  Starting --> Running: listeners open
  Starting --> Error: "bind, certificate, or host failure"
  Running --> Stopped: Stop
  Error --> Starting: Start
  Error --> Stopped: dismiss
```

| State | What the user sees |
| --- | --- |
| Stopped | Start is enabled once a valid config is loaded, including a document with an empty application list. Endpoints show the addresses that would be used, labeled `not live`. |
| Starting | Start and Stop are disabled. The log records `Starting`. |
| Running | The button reads Stop. Endpoint URLs are the live ones, with copy buttons. The note beside them is empty. |
| Error | The button reads Start. The status text is `Error: {message}` and the log carries the exception. |

`ServerRunState` is `Stopped` (initial), `Starting`, `Running`, or `Error`. `BeginStart` moves to Starting and clears the error and `RunningSince`. `MarkRunning` sets `RunningSince` to `DateTimeOffset.Now`. `MarkStopped` moves to Stopped. `MarkError` moves to Error. `DismissError` moves Error to Stopped and does nothing in the other states. A failed auto-start load also calls `MarkError`.

There is a UI status kind `Config`, colored like Error, used for the invalid-configuration summary while the server state itself may be Stopped.

### Auto-start

1. Parse arguments before the window exists. Fatal argument errors still create the window so there is a place to read them, except `--help` and `--register-shell`, which exit.
2. On `Window.Loaded`, if a configuration path was supplied, run `ConfigLoader` through `LoadConfig(path, autoStart: true)`.
3. If the document is valid, call the same start path the Start button uses.
4. If it is invalid, remain Stopped (status text describes the failure) and do not open a port.

### Start sequence

1. Return immediately when there is no document, or when the state is already Starting or Running.
2. If the bind address is not loopback, require `enableHttps` and a non-empty `apiKey`. Otherwise log Error, category `server`, `A network bind requires HTTPS and an API key.` and return without changing state. An Error state stays Error.
3. `BeginStart`, then log `Starting`.
4. `McpHost.StartAsync`. When HTTPS is enabled and `certificate.path` is empty, `SelfSignedCertificateStore.Ensure` creates or reuses the certificate. The log line is Info, category `server`: `Created certificate {pfx} ({thumbprint}). Public certificate {cer}.` or `Reused certificate ...` with the same shape. The settings used for the listener are then rewritten so the certificate path is the generated pfx and the password is empty.
5. Load the PKCS#12 file with `X509KeyStorageFlags.UserKeySet | Exportable`. Ephemeral keys do not reach Schannel, and the TLS handshake would close immediately.
6. Build the host as in section 2 and call `StartAsync`. On success store the application and the certificate, `MarkBound`, `MarkRunning`, and log one Info line per public endpoint URL. On any exception dispose the application and the certificate, log `exception.ToString()` after scrubbing the API key and the password, and `MarkError` with the scrubbed `exception.Message`. No partial listener is left open.

### Stop sequence

`ReleaseHeldButtons`, then `StopAsync` on a thread-pool thread, then in a `finally`: `SetActiveSessions(0)`, `MarkUnbound`, `MarkStopped`, and a log line `Stopped`. Cumulative counters stay until the process exits. A stop exception is logged at Error. `ReleaseHeldButtons` sends no button-up when the screen is already locked.

### Installed Start menu

The installer creates a non-advertised Start menu shortcut named `MCP Overlord`, description `MCP server that controls Windows applications`, working directory the install folder. The shortcut's `System.AppUserModel.ID` is `game_automation_in_csharp.MCPOverlord`, the same id the process sets before it creates a window. The `.overlord` ProgId carries that id in `HKLM\SOFTWARE\Classes\MCPOverlord.Configuration\AppUserModelID`, so recent files belong to this shortcut.

The jump list (`JumpListBuilder`, published through `WpfJumpListPublisher`):

- `ShowFrequentCategory` false, `ShowRecentCategory` true.
- Task `Run as Administrator`, description `Start MCP Overlord elevated`: the executable with `--elevate`, icon from the executable, index 0.
- Task `Uninstall`, description `Uninstall MCP Overlord`, only when a product code exists. It runs `%SystemRoot%\System32\msiexec.exe` with `/x {GUID}`.

The product code is read from HKLM, 64-bit registry view, key `SOFTWARE\MCP Overlord`, value `ProductCode`. `Guid.TryParse` normalizes it to uppercase `{B}` form. An invalid or missing value means no Uninstall task. A build that is not installed has no product code and no Uninstall task.

A successful load or save calls `JumpList.AddToRecentCategory` when the file exists. Shell errors are swallowed.

### Installer

`installer/mcp_overlord` is a WiX 5 project (`WixToolset.Sdk` 5.0.2, `WixToolset.UI.wixext` 5.0.2), platform x64. The MSI name is `mcp_overlord-$(ProductVersion)-win-x64`. The default version is 1.0.0. The publish input defaults to `artifacts/publish/mcp_overlord/`. Visual Studio can still build the `.wixproj`. `dev/build-installer.ps1` builds `Package.wxs` with the WiX v5.0.2 command line instead.

| Setting | Value |
| --- | --- |
| Package name | `MCP Overlord` |
| Manufacturer | `game_automation_in_csharp` |
| UpgradeCode | `EA1684D4-1935-4FF8-8193-BB8098476FA6` (stable across builds) |
| ProductCode | Generated per build. Written to `HKLM\SOFTWARE\MCP Overlord\ProductCode`. |
| Scope | perMachine |
| Upgrade | `MajorUpgrade`. Downgrade message: `A newer version of [ProductName] is already installed.` |
| Media | Embedded cabinet, high compression. |
| Install folder | `ProgramFiles64Folder\MCP Overlord` |
| UI | `WixUI_InstallDir`. The license page is skipped: Welcome goes straight to the directory page. |
| ARP icon | `AppIcon.exe` |

The main executable component defines ProgId `MCPOverlord.Configuration`, description `MCP Overlord configuration`, icon the executable at index 0. Extension `overlord`, verb `open`, command `Open`, argument `--config "%1"`.

The Start menu shortcut is not advertised. Its key path is HKCU `SOFTWARE\MCP Overlord\StartMenuShortcut` = 1, which satisfies ICE43. The rest of the publish folder is harvested with `Files Include`, excluding the main executable so it is not listed twice.

Custom action `RegisterShellJumpList` runs the installed executable with `--register-shell` after `InstallFinalize`, impersonating the installing user, `Return=ignore`, and only when the action is not a full uninstall (`NOT (REMOVE~="ALL")`).

`dev/build-installer.ps1` installs the build requirements, runs `kill-debug.ps1`, publishes win-x64 self-contained without debug symbols, requires `snapenc.dll` in the publish output, and builds the MSI into `artifacts/installer`. `install_requirements.ps1` installs the signed WiX v5.0.2 command line from `wix-cli-x64.msi` with `msiexec /qn`. The Windows SDK and Visual C++ installers also run with no window, and their logs are printed as steps. The WiX CLI MSI also installs `WixToolset.UI.wixext` 5.0.2 under `Program Files\Common Files\WixToolset\extensions`, and the build uses that copy. `wix extension add -g WixToolset.UI.wixext/5.0.2` runs only when that copy is missing. The script then runs `wix build`. Restore of the application and the WiX extension are separate from the build. Between them the script removes the Windows download mark from the NuGet cache and from `%USERPROFILE%\.wix\extensions`. Windows Sandbox also enables Smart App Control, which blocks those assemblies even after the mark is removed. On the sandbox account, `install_requirements.ps1` sets `VerifiedAndReputablePolicyState` to 0 and refreshes the policy with `CiTool.exe -r`. The sandbox image has no VBScript engine, and Windows Installer validation (ICE) needs that engine, so the sandbox build skips `wix msi validate` and still writes the MSI.

### Manual load

The Load button opens `OpenFileDialog` with `ConfigFile.DialogFilter`. The starting folder is `UiSettings.LastConfigDirectory` when that folder exists. The chosen file goes through `ConfigLoader`. Success swaps the active document as in section 3.5. The path is shown in the top bar. Save offers `config.overlord` for a new document. The last folder used by the config dialogs and the folder picker is written to `ui_settings.json`.

### Restart as Administrator from the window

The shield button is visible only when the process is not elevated. It asks to save when the document is dirty, stops the listener, and starts an elevated instance with the open file as a positional argument so that instance loads the file and starts the server. Cancelling the UAC prompt leaves this instance open and starts the listener again when it was running. While elevated, the window title is prefixed with `Administrator: ` and the caption color is `#0B2A4A`.

## 5. Window location

`WindowLocator` is the only code that turns an application `name` into an `HWND`. Tools never search the desktop themselves.

### Match rules

Given one application entry:

1. **Process filter.** `EffectiveProcessName` is `processName` when it is non-empty, otherwise the executable file name without extension. The filter is an exact, ordinal, case-insensitive match against the candidate's process name. An empty filter is skipped.
2. **Title filter.** `windowTitle` is an ordinal, case-insensitive substring of the window title. An empty title fails when a title filter is set. An empty filter is skipped.
3. Both filters apply when both exist. A window must satisfy every filter that is configured. This is how two entries can share a title (the Magicraid launcher and the game both expose a title containing `InfiniteMagicRaid`) and still resolve to different processes.

Process names come from `QueryFullProcessImageName` (file name without extension), cached per process id for the life of the process (`ProcessNameLookup`). Titles come from `InternalGetWindowText` into a 512-character buffer. That call does not send a window message, so a hung window cannot block the search.

Candidates come from two passes. The second pass runs only when the first pass matches nothing:

1. `EnumWindows` over every top-level window, including hidden ones. Each candidate records `IsVisible`, `IsIconic`, `IsZoomed`, whether it is the foreground window, and a z-order equal to its enumeration index (lower index is closer to the front).
2. `Process.MainWindowHandle` / `MainWindowTitle` for each process. In this pass `IsVisible` is recorded as "not minimized", and z-order follows process order.

`Window.FindByTitle` in `automation_util` still walks `MainWindowTitle` only and stays case-sensitive, so existing callers keep their first-match behavior. The locator sits on `WindowSearch` instead.

### Several matches

`WindowCandidateSelector.Choose`:

1. When any candidate is visible and not minimized, drop the rest.
2. Prefer the foreground window if it is in that pool.
3. Otherwise take the lowest z-order.

Before that, a cached handle that is still among the matches is kept. The cache wins over the tie-break, so a newly foreground window does not displace a cached one.

The tool result includes `matchCount`. When `matchCount` is greater than 1, the result text is `The configuration matches {n} windows. Chose '{title}'.` The log records that at Warning only when the caller asked to report multiple matches (`find_window`, `take_screenshot`, and the input tools). `list_applications` does not. The call still succeeds so the model can act.

### No window yet

`find_window` with nothing chosen returns `found: false`.

- When the process filter matches a live process: `The process is running and has no window yet (process id {id}).` A launchable entry appends ` Call launch_application with waitForWindowSeconds, or retry find_window.` The JSON includes `processId`.
- Otherwise: `No matching window was found.` A launchable entry appends ` The application is launchable; call launch_application.`

`launch_application` polls the same lookup until `waitForWindowSeconds` elapses.

### Minimized windows

Before a screenshot or any input, if the matched window is iconic, the locator restores it with `ShowWindow(hwnd, SW_RESTORE)` and waits 200 ms so the frame has a size. `find_window` and `list_applications` pass `restore: false` and leave the window as it was. `find_window` reports `state: "minimized"`. `focus_window`, `take_screenshot`, and every input tool restore.

### Cache

`WindowLocator` keeps `name -> { hwnd, processId, processName, matchCount }` for the active config.

A cache hit is tried before any enumeration. It is valid when all of these hold:

- `IsWindow(hwnd)` is true.
- `GetWindowThreadProcessId` still returns the cached process id.
- The cached process name still passes the process filter. The live process name is not read again.
- The current title still passes the title filter.

Any failed check drops the entry and runs a full search. A cache hit reports the stored `matchCount` and treats the window as visible without a fresh visibility check. `ConfigStore.Changed` drops the whole cache.

### Result of a search

`find_window` returns this object as text content. Null properties are omitted. The handle is `0x` plus at least eight uppercase hex digits (`0x{value:X8}`), so a value that needs more than eight digits is longer.

Found:

```json
{
  "found": true,
  "hwnd": "0x000A0123",
  "processId": 1234,
  "processName": "InfiniteMagicraid",
  "title": "InfiniteMagicRaid",
  "matchCount": 1,
  "window": { "width": 2576, "height": 1478 },
  "screen": { "x": 0, "y": 0, "width": 2576, "height": 1478 },
  "state": "normal",
  "message": null
}
```

`message` is present only when `matchCount` is greater than 1. `window` and `screen` use `GetWindowRect`. `state` is `normal`, `minimized`, or `maximized`.

Not found:

```json
{
  "found": false,
  "matchCount": 0,
  "processRunning": true,
  "processId": 1234,
  "message": "The process is running and has no window yet (process id 1234)."
}
```

`processId` is omitted when `processRunning` is false.

| Field | Meaning |
| --- | --- |
| `hwnd` | Hex string. Informational. Tools still take `name`, never an hwnd from the model. |
| `window` | Width and height of the snapshot coordinate space (`GetWindowRect`). |
| `screen` | That same rectangle placed on the screen (`GetWindowRect`). |
| `state` | `normal`, `minimized`, or `maximized`. |

`found: false` uses the tool success channel. See section 11 for the split between a normal "not found" result and a tool error.

`GetFrameGeometry` also records the client origin (`ClientToScreen(0, 0)` minus the window corner) and the `GetClientRect` size. Mouse tools use that offset to turn a window-rectangle point into a client point. The offset can be negative for a title-bar or border pixel, and that point is still sent.

## 6. MCP tools

Seventeen tools are registered. Every tool sets `OpenWorld` false. Results are one text content block of JSON, serialized with camelCase names and nulls omitted, except `take_screenshot`, which adds an image block after the text. Errors use `isError: true` and a single text block. The strings are in section 11.

Window, capture, and input tools take `name`, the configured application name, matched exactly (ordinal). `is_locked` and `lock` take no arguments. An unknown name is a tool error whose message lists the known names. When no document is loaded the message is `No configuration is loaded.`

Every call runs inside `ToolCallLog.Invoke`. After 15 seconds the client receives `The tool did not finish within 15 seconds.` The work keeps running. An unhandled exception becomes `The tool failed ({TypeName}): {Message}`.

### Shared input order

Mouse and keyboard tools check the workstation lock before they validate arguments. When the screen is locked, the tool returns `ScreenLocked` and sends nothing (section 6.10).

Mouse tools then:

1. Check the button name, when the tool has one.
2. Check `coordinateSpace`. Empty or `image` selects image coordinates. `window_rect` selects window-rectangle pixels. Anything else is `UnknownCoordinateSpace`.
3. `TryPrepare`: `Find` with restore and with multiple-match reporting, then `WindowNotAvailable` when nothing was found, then the integrity check.
4. Map coordinates. A point outside the window is `PointOutsideWindow`. Nothing is focused and no mouse input is sent.
5. Clamp `holdMs` or `delta` when the tool has them.
6. Resolve `ClickBackends` from the snapshot's `clickBackend`.
7. Take the per-window `InputGate`, then focus, then send. Focus happens inside the gate.

Keyboard tools: lock, then key-name lookup (or the text-length check for `type_text`), then `TryPrepare`, then the gate, then focus, then send.

`holdMs` defaults to 50 and is clamped to 0–2000. A clamped value is mentioned in `message` and the call proceeds. `focus_window` uses `TryPrepare` as well, so a higher-integrity target returns `HigherIntegrity` and is not focused.

### 6.1 `list_applications`

Read-only and idempotent. Description: `List every configured application. Call this first to learn the names.`

No arguments. Returns:

```json
{
  "applications": [
    {
      "name": "Infinite Magicraid",
      "description": "Infinite Magicraid game",
      "hasDescription": true,
      "launchable": true,
      "running": true,
      "windowFound": true
    }
  ]
}
```

| Field | Meaning |
| --- | --- |
| `description` | The configured string, or `""` when absent. |
| `hasDescription` | True when the description is non-empty. Lets the client skip `get_application_description`. |
| `launchable` | `executable` is non-empty. |
| `running` | `Find` without restore reports the window found, or reports the process running. |
| `windowFound` | A window passes the full locator rule right now. |

This tool does not launch, focus, or restore.

### 6.2 `get_application_description`

Read-only and idempotent. Description: `Return the configured description for an application. Call this when list_applications says hasDescription is true.`

| Argument | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | string | yes | `Configured application name.` |

Returns `{ "name", "description" }`. When no description was configured, `description` is `""` and the call succeeds.

### 6.3 `launch_application`

Description: `Launch a configured executable. The path and arguments come from the configuration, not from this call.`

| Argument | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `name` | string | yes | | `Configured application name.` |
| `waitForWindowSeconds` | number | no | `0` | `Seconds to wait for a window. Clamped to 0-120. Default 0.` |

Behavior:

- An empty executable returns `NotLaunchable`. A schema-valid entry has an executable.
- `ProcessStartInfo` uses `FileName = executable`, `Arguments = parameters` (the raw string, so quotes stay intact), `WorkingDirectory` only when it is non-empty, `UseShellExecute = false`.
- The started process id is the direct child. Many games use a launcher that exits after spawning the real process. The Magicraid sample is that situation: "Magicraid Starter" starts `Launcher.exe`, and the playable window belongs to `InfiniteMagicraid.exe`, which is a separate configured application. `launch_application` reports the child it created. It does not claim that this child still owns the window. Window discovery stays on the locator rules for that same entry.
- When the applied wait is greater than 0, the tool polls `Find` immediately and then every 250 ms (the last slice is shortened) until a window is found or the time runs out. A wait of 0 does not poll, so `windowFound` is false. On timeout the spawn is still a success, with `windowFound: false` and the process id.
- NaN or infinity is treated as 0 and counts as clamped.
- A start failure is `StartFailed` with `exception.Message`.

Returns:

```json
{
  "name": "Magicraid Starter",
  "started": true,
  "processId": 4812,
  "windowFound": false,
  "message": "waitForWindowSeconds was clamped to 120."
}
```

`message` is present only when the wait was clamped: `waitForWindowSeconds was clamped to {value}.`

### 6.4 `find_window`

Read-only and idempotent. Description: `Find the window for a configured application. found false means it is not on screen yet.`

| Argument | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | string | yes | `Configured application name.` |

Returns the JSON from section 5. `found: false` is a successful tool result.

### 6.5 `take_screenshot`

Read-only. Description: `Capture the whole window, including the border and the title bar. Overlapping windows are not included, and what is behind the window is not included. Call this before clicking. Click coordinates are pixels of this image. Origin (0, 0) is the top left of the window. A click in the outer border can resize the window. On a maximized window, (0, 0) can be off screen.`

| Argument | Type | Required | Default | Description |
| --- | --- | --- | --- | --- |
| `name` | string | yes | | `Configured application name.` |
| `region` | object | no | whole window | `Optional crop in window pixels: x, y, width, height. Origin is the top left of the window.` Fields `x`, `y`, `width`, `height`. |
| `maxWidth` | integer | no | none | `Downscale when the image is wider than this. Clamped to 64-4096.` |
| `format` | string | no | `png` | `png or jpeg. Default png.` Trimmed, case-insensitive. |
| `quality` | integer | no | `80` | `JPEG quality from 1 to 100. Default 80. Ignored for png.` Clamped to 1–100 with no message. |

When `region` is omitted, no crop is applied. When a region object is present, a missing field is 0. The rectangle is intersected with the bitmap. An empty intersection is `RegionMiss`.

The window is restored if it is minimized. A missing window is a tool error here, because the caller asked for pixels. The message is `WindowNotAvailable`.

#### Capture pipeline

A frame whose width or height is 0 or less fails with `Screenshot encode failed: The window has no size.`

**Fast path.** Used only for PNG with no region and no `maxWidth`, and only when `snapenc.dll` loaded (section 15). Under one lock:

1. `GetWindowRect` must still match the geometry size.
2. Reuse a top-down 32-bit DIB section in a memory DC.
3. Start a native encode session.
4. `WindowVisualCapture.TryCopy` copies the frame in bands of 160 rows. A background thread named `snapshot-encode` encodes each band while the next band is copied. A worker wait that exceeds 5 seconds gives up the fast path.
5. When the client area is black, give up the fast path.
6. Otherwise return `captureMethod` `windowVisual`, scale 1, the full rectangle, and no warning.
7. When Windows Graphics Capture is unavailable, the same banded path uses `GetWindowDC` and `BitBlt`, and the method is `windowDc`, with the same black check.

The fast path writes an RGB PNG (alpha dropped) with stored zlib blocks. The file is larger than a compressed PNG.

**Windows Graphics Capture** (`WindowVisualCapture`), used by the fast path and by the slow path's first attempt:

- `D3D11CreateDevice` with the hardware driver, `D3D11_CREATE_DEVICE_BGRA_SUPPORT` (flag `0x20`), and SDK version 7. The device is wrapped with `CreateDirect3D11DeviceFromDXGIDevice`.
- The capture item comes from `IGraphicsCaptureItemInterop.CreateForWindow`.
- The frame pool is `Direct3D11CaptureFramePool.CreateFreeThreaded`, pixel format B8G8R8A8, two buffers, sized to `DWMWA_EXTENDED_FRAME_BOUNDS`.
- The session sets `IsCursorCaptureEnabled` false and `IsBorderRequired` false.
- One session stays open for the same window and the same size. It is rebuilt when either changes, and it is closed when the capture item closes. The session is not disposed when the process exits.
- The first frame is waited on for 300 ms. Later calls reuse the latest frame.
- The frame's content size must equal the capture size. The texture must be BGRA (formats 87, 90, or 91) with one sample. It is copied to a staging texture, mapped, and placed at the offset of the extended bounds within `GetWindowRect`.
- Rows outside the content clamp to the nearest content row. Columns replicate the first or last content pixel. That fills the invisible resize margin inside `GetWindowRect` from the adjacent frame pixel, so the image origin stays the top left of `GetWindowRect`.
- The capture is rejected when the extended bounds are smaller than half the window rectangle.

**Slow path**, used when the caller asked for JPEG, a region, or `maxWidth`, when the fast path is unavailable, or when the fast path saw a black client area. Attempts, in order:

1. `windowVisual` when Windows Graphics Capture copies a bitmap, otherwise `windowDc` from a full `GetWindowDC` / `BitBlt` into a 32-bit ARGB bitmap.
2. `PrintWindow` with flag 0 (`printWindow`).
3. `PrintWindow` with flag `PW_RENDERFULLCONTENT` (2) (`printWindowFull`). Both go through `ScreenshotGenerator.CaptureWindow`: `GetDC(hwnd)`, a compatible bitmap of the `GetWindowRect` size, and `Bitmap.FromHbitmap`. The `PrintWindow` return value is ignored.
4. A screen blit of `GetWindowRect` from the screen DC (`screen`), only when `WindowOverlap.IsCovered` is false. The screen blit would include whatever is painted on top, so it is skipped when the target is covered.

`WindowOverlap.IsCovered` walks `EnumWindows` from the top of the z-order and stops at the target. It skips windows that are invisible, iconic, cloaked (`DWMWA_CLOAKED`, attribute 14), or owned by the target. Any rectangle intersection means covered.

For each attempt:

- A bitmap whose size is not the geometry size is discarded. Its size error is remembered, and the last such error wins.
- The first non-black frame wins.
- A black frame is kept, and a later black frame replaces an earlier one. A black frame from an earlier method is kept even when a later method reports a size mismatch.
- When every frame is black, the tool returns the last image and `warning` set to `The captured frame is black. The game is likely using exclusive fullscreen or a protected surface.` The log records that warning.
- When no frame exists, the size error is returned (`CaptureSizeMismatch`). Otherwise the failure is `Screenshot encode failed: {last exception}` or `Screenshot encode failed: Capture returned no image.`

**Black detection.** Mean Rec.709 luminance, `0.2126 R + 0.7152 G + 0.0722 B`, truncated per sample, over the client rectangle only. The frame is black when the mean is below `2.0`. The sample step is 1 when the area is 20,000 pixels or less, otherwise `sqrt(area / 10000)`. An empty client area counts as black. The fast path exits early once 16 or more samples have been read and the projected mean is already at or above the threshold. The title bar is outside the sample.

**After a bitmap exists:**

- Crop with `DrawImage` into an ARGB bitmap when a region is set.
- Downscale only when the width exceeds the clamped `maxWidth` (64–4096). `HighQualityBicubic`. Height is `max(1, round(height * ratio))` with rounding away from zero.
- `scale = scaledWidth / regionWidth` (1 when nothing was scaled). The same horizontal scale is reused for both axes. It is stored per application name in `ScreenshotScaleStore`, replacing the previous frame. Mouse tools in `image` space use it.
- Encode with GDI+ as PNG, or as JPEG with the `Encoder.Quality` parameter.

A maximized window can extend outside the screen, so the corner of the image can be black and a click at `(0, 0)` can land off screen. A click in the outer border can resize the window, because that pixel is part of the snapshot.

Return content, in order:

1. Text content, JSON metadata. `warning` is omitted when null.

```json
{
  "name": "Infinite Magicraid",
  "windowWidth": 2576,
  "windowHeight": 1478,
  "region": { "x": 0, "y": 0, "width": 2576, "height": 1478 },
  "imageWidth": 1288,
  "imageHeight": 739,
  "scale": 0.5,
  "format": "png",
  "captureMethod": "windowVisual"
}
```

`captureMethod` is `windowVisual`, `windowDc`, `printWindow`, `printWindowFull`, or `screen`.

2. An MCP image content block: base64 payload, `mimeType` `image/png` or `image/jpeg`.

The log line records dimensions and byte size. It does not record the base64 payload. The captured HTTP body still holds the raw response, including the base64 (section 12).

### 6.6 Mouse tools

`coordinateSpace` description, shared by every mouse tool that takes it: `image (default): pixels of the last screenshot, including its scale and crop. window_rect: physical pixels of the window rectangle, with no scale. A point outside the window is rejected and nothing is sent.`

`x` and `y` are doubles. Their description is `X in the selected coordinate space.` and `Y in the selected coordinate space.`

- `image` — x and y are pixels of the last screenshot for this application, including `maxWidth` scaling and excluding any area outside `region`. `windowX = region.x + round(x / scale)`, and the same for y, with `MidpointRounding.AwayFromZero`. A missing, non-positive, NaN, or infinite scale is treated as 1. A NaN coordinate becomes 0. When no screenshot has been taken yet, scale is 1 and the region origin is `(0, 0)`, so image coordinates are `GetWindowRect` pixels.
- `window_rect` — `windowX = round(x)` and `windowY = round(y)`. Scale, region, and any previous screenshot are ignored.

The window size is read again at the time of the click, so a window that moved after the screenshot still lines up. Only the scale and the region come from the snapshot, and only for `image`. A converted point outside `[0, width) × [0, height)` is `PointOutsideWindow`. The click backend receives a client point (window point minus the client offset). `ClientToScreen` turns that into a screen point. That conversion is not part of the tool arguments or the result.

| Tool | Description | Arguments beyond `name`, `x`, `y` |
| --- | --- | --- |
| `mouse_move` | `Move the cursor. No button change. image coordinates are pixels of the last screenshot. window_rect coordinates are physical pixels of the window. A point outside the window is rejected.` | `coordinateSpace?` |
| `mouse_click` | `Move, press, and release a mouse button. image coordinates are pixels of the last screenshot. window_rect coordinates are physical pixels of the window. A point outside the window is rejected. A point on the outer border can resize the window. On a maximized window, (0, 0) can be off screen.` | `button?`, `double?`, `holdMs?`, `coordinateSpace?` |
| `mouse_down` | `Move and press a mouse button. It stays down until mouse_up or a later click. A point outside the window is rejected.` | `button?`, `coordinateSpace?` |
| `mouse_up` | `Move and release a mouse button. A point outside the window is rejected.` | `button?`, `coordinateSpace?` |
| `mouse_scroll` | `Move and scroll the wheel. Positive delta scrolls up, away from the user. Clamped to -30..30. A point outside the window is rejected.` | `delta`, `coordinateSpace?` |

`button` description: `left (default), right, or middle.` Matching is case-insensitive. `double` description: `When true, click a second time after the system double-click delay.` Default false. `holdMs` description: `Milliseconds between press and release. Default 50, clamped to 0-2000.` `delta` description: `Wheel notches. Positive scrolls up.` Clamped to −30…30.

`mouse_click` with `double: true` uses `GetDoubleClickTime` between the two clicks, and `holdMs` inside each click.

`mouse_down` records the button as held for that window, replacing any entry with the same window and button. `mouse_up` removes that entry. `mouse_click` does not clear a held entry. `ReleaseHeldButtons` later sends `ButtonUp` for whatever is still recorded.

Returns text JSON. `button` is present for click, down, and up. `delta` is present for scroll. `message` joins `holdMs was clamped to {v}.` and `delta was clamped to {v}.` with a space. The applied `holdMs` is not its own field.

```json
{
  "name": "Infinite Magicraid",
  "action": "mouse_click",
  "button": "left",
  "windowX": 400,
  "windowY": 300
}
```

`windowX` and `windowY` are pixels of the current `GetWindowRect`.

`InputService` performs the gesture through the `ClickHelper` named by `clickBackend` (section 7). Both implementations convert client coordinates with `ClientToScreen` before moving the cursor or sending a button. A zero window handle skips that conversion.

### 6.7 Keyboard tools

| Tool | Description | Arguments beyond `name` |
| --- | --- | --- |
| `key_press` | `Focus the window, press a key, and release it.` | `key`, `holdMs?` |
| `key_down` | `Focus the window and send a key down. Pair it with key_up.` | `key` |
| `key_up` | `Focus the window and send a key up.` | `key` |
| `type_text` | `Focus the window and type text. Newlines are sent as Return. Length 1 to 500.` | `text` |

`key` on `key_press` is described as `Key name such as A, Enter, Escape, F5, Ctrl, or Left.` On `key_down` and `key_up` it is `Key name.` `holdMs` is `Milliseconds between down and up. Default 50, clamped to 0-2000.` `text` is `Text to type. Length 1 to 500.`

`key` is mapped by `KeyNameMap`, case-insensitive, after trim.

| Names accepted | Virtual key |
| --- | --- |
| `A`–`Z`, `0`–`9` | `VK_A`–`VK_Z`, `VK_0`–`VK_9` |
| `F1`–`F24` | `VK_F1`–`VK_F24` |
| `Enter`, `Return` | `VK_RETURN` |
| `Escape`, `Esc` | `VK_ESCAPE` |
| `Tab` | `VK_TAB` |
| `Space` | `VK_SPACE` |
| `Backspace` | `VK_BACK` |
| `Delete`, `Del` | `VK_DELETE` |
| `Insert`, `Home`, `End`, `PageUp`, `PageDown` | `VK_INSERT`, `VK_HOME`, `VK_END`, `VK_PRIOR`, `VK_NEXT` |
| `Left`, `Right`, `Up`, `Down` | arrow virtual keys |
| `Shift` | `VK_SHIFT` |
| `Ctrl`, `Control` | `VK_CONTROL` |
| `Alt` | `VK_MENU` |
| `Win` | `VK_LWIN` |

An unknown name is `UnknownKey`. The server does not send a best guess. Numpad keys, punctuation, Caps Lock, Print Screen, and left/right modifier variants are not names.

Chords are a sequence the model composes: `key_down` Ctrl, `key_down` A, `key_up` A, `key_up` Ctrl. The per-window gate keeps that sequence intact relative to other clients' input on the same window. Keys held with `key_down` are not tracked. Shutdown and `lock` release mouse buttons only.

`type_text` sends characters, not key names. Length must be 1–500, checked before the window is resolved. Each code unit other than `\n` is delivered with `SendInput` and `KEYEVENTF_UNICODE`, so letters, digits, spaces, and common punctuation work regardless of the current keyboard layout. `\n` is a Return key down plus key up. `\r` is sent as a Unicode character. `type_text` does not hold Shift to produce symbols; the Unicode event carries the glyph.

`key_press`, `key_down`, and `key_up` go through `KeyboardUtil`, which sends one `SendInput` per event after its own `SetForegroundWindow`. The release uses `KEYEVENTF_KEYUP`. Scan code is 0, and `KEYEVENTF_EXTENDEDKEY` is not set, including for the arrows and the navigation keys.

Keyboard result:

```json
{
  "name": "Infinite Magicraid",
  "action": "key_press",
  "key": "Enter",
  "holdMs": 50
}
```

`key` is present for the key tools. `holdMs` is present for `key_press` (the applied value). `length` is present for `type_text`. `message` is `holdMs was clamped to {v}.` when the clamp changed the value.

### 6.8 `focus_window`

Description: `Restore the window if it is minimized and bring it to the foreground.`

| Argument | Type | Required | Description |
| --- | --- | --- | --- |
| `name` | string | yes | `Configured application name.` |

Restores the window if it is minimized, checks integrity, then tries to bring it to the foreground. It does not check the workstation lock.

`SetForegroundWindow` often fails when the caller is a background process. `FocusHelper`:

1. Call `SetForegroundWindow`. On success, `BringWindowToTop` and `SetFocus`, and return true.
2. Otherwise, when the target thread id and the foreground thread id are both non-zero and different, `AttachThreadInput` joins them, `SetForegroundWindow` is tried again, the window is raised, and `AttachThreadInput` detaches in a `finally`. The retry's result is returned.

Returns:

```json
{ "name": "Infinite Magicraid", "focused": true }
```

`focused: false` is a successful result with `message` `The window could not be brought to the foreground. Input may still be delivered at the window's screen position.` Subsequent input tools try the same focus sequence and then send events anyway. `KeyboardUtil` calls `SetForegroundWindow` again inside each keyboard method.

### 6.9 What is not a tool argument

- No tool accepts an executable path, a pid, or an hwnd from the client. The configuration is the allowlist.
- No tool accepts a filesystem path for the screenshot. The image travels inside the MCP result.

### 6.10 Workstation lock

`is_locked` is read-only and idempotent. Description: `Report whether the workstation screen is locked. There is no unlock tool.` Returns `{ "locked": true }` or `{ "locked": false }`.

`lock` description: `Lock the workstation screen. Mouse and keyboard tools then fail until someone unlocks the screen at the console. This server cannot unlock the screen.` It calls `ReleaseHeldButtons`, then `LockWorkStation`. Success returns `{ "locked": true }`. Failure is `LockFailed`, which uses the operating-system message, or `LockWorkStation returned false.` when that message is blank. `lock` does not check whether the screen is already locked. When it is, `ReleaseHeldButtons` sends nothing.

`IsLocked` reads the current session with `WTSQuerySessionInformation` (`WTS_CURRENT_SESSION`, info class `WTSSessionInfoEx` = 25). The info level must be 1 and the buffer must be at least 20 bytes. `SessionFlags` is the 32-bit value at offset 16. A value of 0 (`WTS_SESSIONSTATE_LOCK`) means locked. A failed query returns null and is not treated as locked. `WTS_SESSIONSTATE_UNKNOWN` is not treated as locked. An unread flag would be 0, which is the lock value, and that must not mark a session locked when the query did not return a flag.

It also opens the input desktop with `OpenInputDesktop` and `DESKTOP_READOBJECTS | DESKTOP_SWITCHDESKTOP`. A null handle means the input desktop is not available to this process (a secure desktop such as a UAC prompt, or the Winlogon desktop) and is treated as locked, so input is never sent. When the handle opens, the name is read with `GetUserObjectInformationW` (`UOI_NAME`, 512-character buffer) and the handle is closed. Only the name `Default` (ordinal comparison) counts as an unlocked desktop. An unreadable name counts as locked. The session is locked when either check says so. The decision is the pure function `IsLockedState(sessionFlags, desktopOpened, desktopName)`.

On Windows 10 and 11 the lock screen is `LockApp` on the Default desktop, so the desktop name alone stays `Default` and cannot see that lock. The session flag is what sees it.

While the workstation is locked, `mouse_move`, `mouse_click`, `mouse_down`, `mouse_up`, `mouse_scroll`, `key_press`, `key_down`, `key_up`, and `type_text` return a tool error and send nothing. The message says the workstation is locked, that no input was sent, that unlock happens at the console, and that this server has no unlock tool. The check runs at the start of the gesture, before argument validation, the window search, focus, and the input call.

`focus_window`, `take_screenshot`, `find_window`, and the application tools still run.

## 7. automation_util

`src/automation_util` is a `net9.0-windows7.0` WPF class library. Packages: Emgu.CV 4.8.1.5350 (with the bitmap and runtime packages), SharpDX.DirectInput and SharpDX.XInput 4.2.0, System.Drawing.Common 8.0.0. The MCP layer uses the input, search, and capture types below. Image search stays in the library for a later tool.

### Keyboard

`KeyboardInputEncoder`:

- `BuildPressAndRelease` emits every key down, then the key ups in reverse order with `KEYEVENTF_KEYUP`.
- `KeyDown` and `KeyUp` build a single event.
- `TypeText` turns `\n` into Return down and up. Every other UTF-16 unit is a `KEYEVENTF_UNICODE` down and a matching up. `\r` is a Unicode unit.

`NativeInput.SendKeyboard` and `SendMouse` send one batch. Fewer events sent than requested throws `Win32Exception`. On x64 the `INPUT` size is 40 bytes.

`KeyboardUtil` (namespace `game_automation_in_csharp.util`):

- `PressAndRelease` uses `keybd_event` through the encoder, so the up half carries `KEYEVENTF_KEYUP`, and sleeps 50 ms after each event. Existing callers keep this method.
- `KeyDown`, `KeyUp`, `Press(holdMs)`, and `TypeUnicode` use `SendInput`, one call per event, each after `SetForegroundWindow`.

### Mouse

`MouseButton` is `Left`, `Right`, `Middle`.

```csharp
public interface ClickHelper
{
    void MoveTo(IntPtr hwnd, int clientX, int clientY);
    void ButtonDown(IntPtr hwnd, int clientX, int clientY, MouseButton button);
    void ButtonUp(IntPtr hwnd, int clientX, int clientY, MouseButton button);
    void Click(IntPtr hwnd, int clientX, int clientY, MouseButton button, int holdMs, bool doubleClick);
    void Scroll(IntPtr hwnd, int clientX, int clientY, int notches);
    void ClickOnPoint(IntPtr hwnd, int x, int y, int clickDelayInMilliseconds = 100);
}
```

`ClickHelperBase` implements `Click`, `ClickOnPoint`, and the double-click timing once, in terms of `MoveTo`, `ButtonDown`, and `ButtonUp`. `Click` is move, down, delay, up. A double click repeats after `GetDoubleClickTime`. `Scroll` is move, then wheel. `ClickOnPoint` is an explicit interface implementation: left click, `doubleClick` false, default delay 100 ms. The base owns `ClientToScreen`. Each concrete class delivers the raw move, button, and wheel at a screen point.

| Class | What it sends |
| --- | --- |
| `MouseEventClickHelper` | `SetCursorPos`, then `mouse_event`. Flags: `MOUSEEVENTF_LEFTDOWN` / `LEFTUP`, `RIGHTDOWN` / `RIGHTUP`, `MIDDLEDOWN` / `MIDDLEUP`, and `MOUSEEVENTF_WHEEL` with `notches * 120`. A static `ClickOnPoint` forwards to a shared instance. |
| `SendInputClickHelper` | One `SendInput` batch. The move uses `MOUSEEVENTF_MOVE \| MOUSEEVENTF_ABSOLUTE \| MOUSEEVENTF_VIRTUALDESK`, normalized to the virtual screen (`GetSystemMetrics` 76–79, range 0–65535). A button or wheel event is an absolute move plus the button or wheel in the same batch. |

`MouseInputEncoder` holds the flag constants, `WHEEL_DELTA` 120, and `ABSOLUTE_RANGE` 65535. `Normalize` is `(pixel - origin) * 65535 / (extent - 1)`, rounded and clamped. An extent of 1 or less yields `(0, 0)`.

`clickBackend` chooses the instance:

| Value | Implementation |
| --- | --- |
| `mouseEvent` | `MouseEventClickHelper`. Default when the field is omitted. |
| `sendInput` | `SendInputClickHelper`. |

`InputService` holds one shared instance of each class (`ClickBackends`) and picks by the snapshot's `clickBackend` at the start of the gesture. A reload that changes `clickBackend` affects the next gesture.

Existing callers of a left-button click (`AutomationUtil`, `Bluestack`) use `MouseEventClickHelper.ClickOnPoint`. They do not go through the MCP `clickBackend` setting. The delay default stays 100 ms.

### Window search

`WindowSearch` (used by the locator):

- `WindowCandidate` with lazy `ProcessName`.
- `WindowSearchResult(Found, Chosen, MatchCount, Matches)`.
- `WindowCandidateSelector.Filter`, `Choose`, `PassesProcessFilter`, `PassesTitleFilter`.
- `Enumerate` and `Search(processName, title)`. `Search` adds missing main windows only when the enumeration matched nothing.

`NativeWindowText.Read` is the hung-window-safe title read. `ProcessNameLookup.Read` caches the image name per process id for the life of the process.

`Window` still wraps a `BitmapFinder` and a handle: `FindBitmap`, `FindAllMatches`, `rect`, `setRect` (`SetWindowPos`), `FindByTitle`, `Save`, `Capture`, `Load`, `Close` (`WM_CLOSE`). `setRect` is not an MCP tool.

### Capture helpers

`ScreenshotGenerator` exposes `CaptureWindow(hwnd)` (flag 0), `CaptureWindow(hwnd, flags)`, `CaptureWindowFromScreen`, `CaptureClientFromScreen`, and `PW_RENDERFULLCONTENT = 2`. A zero or unreadable rectangle throws. `BlockableScreenshotGenerator` overrides `CaptureWindow(IntPtr)` only.

### Image search and older helpers

These are not MCP tools. They stay available to other programs in the solution.

- `BitmapFinder` uses Emgu `MatchTemplate` with `CcoeffNormed`. `FindBitmap` returns the best match at or above the ratio. `FindAllMatches` returns every match above the ratio, sorted, with overlaps removed.
- `AutomationUtil` exposes `detect_menu`, `activate_menu`, `find_all_matches`, `find_matches`, and `click_in_the_middle_of_rectangle`.
- `Image` loads a WPF pack resource. `ImageUtil.load_images` loads by regex.
- `Bluestack` knows a hard-coded HD-Player path. `Close` sends `WM_CLOSE`, waits, then clicks a fixed point.
- `DirectInputClick` is a SharpDX stub. It is not a `clickBackend` value.
- `HotKey` registers a hotkey on a WPF window.
- Small types: `AtomicBool`, `DeltaTimeChecker`, `ElapsedTime`, `MousePositionUtility`, `Pair`, `ImageMatch`, `KeyCode` (modifier and virtual-key constants).

### DPI

The WPF app manifest enables per-monitor v2 DPI awareness (`dpiAwareness` = `PerMonitorV2`). Under that mode, `GetClientRect`, `GetWindowRect`, `ClientToScreen`, and `SetCursorPos` share physical pixels, and a screenshot pixel lines up with a click pixel. System-DPI awareness would make a click land in the wrong place on a scaled monitor. The manifest is part of the feature.

## 8. Security

The server can click and type into other programs, and it can start executables listed in the config. The defaults assume the clients are on the same machine.

### Bind address

Default `bindAddress` is `127.0.0.1`. Loopback is reachable by Cursor on the same PC and is not reachable from another machine.

A Grok bot running elsewhere needs a reachable endpoint. Two supported setups:

1. `bindAddress` set to a non-loopback address, `enableHttps` true, `apiKey` set.
2. Leave the bind on loopback and put a tunnel (or a reverse proxy that terminates TLS) in front of the HTTP port. The API key is still recommended, because the tunnel URL is a bearer capability.

`EndpointFormatter` builds the URLs.

| Settings | HTTP URL | HTTPS URL |
| --- | --- | --- |
| `127.0.0.1`, HTTPS on | `http://127.0.0.1:5000/mcp` | `https://127.0.0.1:5001/mcp` |
| `::1` | `http://[::1]:5000/mcp` | `https://[::1]:5001/mcp` |
| `10.0.0.8`, HTTPS on | `http://127.0.0.1:5000/mcp` | `https://10.0.0.8:5001/mcp` |
| `0.0.0.0`, HTTPS on, `hostNames` `["play.example.com"]` | `http://127.0.0.1:5000/mcp` | `https://play.example.com:5001/mcp`, bound on `https://0.0.0.0:5001` |
| `0.0.0.0`, HTTPS off | `http://0.0.0.0:5000/mcp` is what the stopped window would show | Start refuses this case |

A host that contains `:` is wrapped in brackets. The HTTP bind host is `127.0.0.1` when HTTPS is enabled and the bind address is not loopback. Otherwise it is the bind address. The HTTPS bind host is the raw bind address. The HTTPS public host is the bind address when it is a specific IP, otherwise the first non-blank `hostNames` entry, otherwise `Environment.MachineName`.

### API key

When `apiKey` is set, every MCP request must carry one of:

- `X-API-Key: <apiKey>` when that header is non-empty. It wins over `Authorization`.
- `Authorization: Bearer <apiKey>`. The prefix match is case-insensitive. The remainder is trimmed.

Comparison hashes both UTF-8 strings with SHA-256 and uses `CryptographicOperations.FixedTimeEquals` on the digests. A missing or wrong key produces HTTP 401 with an empty body and no `WWW-Authenticate` header, plus a Warning, category `server`: `Rejected a request with a missing or wrong API key.` The provided secret is not logged. The MCP session is not created. The failure increments the error counter.

When `apiKey` is absent and the bind address is loopback, requests are accepted without a key so local Cursor setup stays a URL and nothing else.

When `apiKey` is absent, or HTTPS is disabled, and the bind address is not loopback, Start logs `A network bind requires HTTPS and an API key.` and leaves the state unchanged. The server does not open an unauthenticated network listener.

The key check runs before the origin check. A request that fails both receives 401.

### HTTPS

`enableHttps` loads the PKCS#12 file from `certificate.path` with `certificate.password` and adds an HTTPS endpoint on `httpsPort`. When `certificate.path` is omitted, start creates or reuses `%LOCALAPPDATA%\mcp_overlord\server.pfx` (empty password) and writes `server.cer` beside it. The log records the path and thumbprint. A bad password or an unreadable supplied file fails the start and lands in the Error state with the exception message. The password is not logged.

The generated certificate:

| Property | Value |
| --- | --- |
| Key | RSA 2048, SHA-256, PKCS#1 |
| Subject | `CN=localhost` |
| Validity | Not before now minus 1 day, not after now plus 730 days |
| Basic constraints | CA false, not critical |
| Key usage | Digital signature and key encipherment, critical |
| Extended key usage | Server authentication (`1.3.6.1.5.5.7.3.1`), not critical |

Subject alternative names, de-duplicated:

- DNS `localhost`
- IP `127.0.0.1`
- IP `::1`
- DNS `Environment.MachineName`
- The bind address, when it parses as an IP and is neither `0.0.0.0` nor `::`
- Each non-blank `certificate.hostNames` entry. An entry that parses as an IP is an IP SAN. Any other entry is a DNS SAN.

Reuse (`TryReuse`) loads the existing pfx with the empty password. It is reused when it has a private key, `NotAfter` is more than 14 days away, `NotBefore` is no later than now plus 5 minutes, and its SANs are a superset of the required list (DNS compared case-insensitively, IPs by equality). On reuse, `server.cer` is exported again. A `CryptographicException` (a corrupt file, for example) writes a new pair. Other IO errors fail the start. `server.cer` is DER and has no private key. The certificate is not installed into a trust store.

```json
"server": {
  "bindAddress": "0.0.0.0",
  "enableHttps": true,
  "httpsPort": 5001,
  "apiKey": "replace-with-a-long-random-string",
  "certificate": {
    "hostNames": ["play.example.com"]
  }
}
```

HTTP stays available on `httpPort` so a local client can use it. When the bind address is not loopback, that HTTP listener is `127.0.0.1` only, and HTTPS listens on the bind address. Remote clients use the HTTPS URL. The copied HTTPS URL uses the first `certificate.hostNames` entry when the bind address is `0.0.0.0` or `::`, and the machine name when that list is empty. The operator gives `server.cer` to the remote client. Until that client trusts that exact certificate, TLS does not protect the API key from interception. The UI lists both URLs when HTTPS is on. Firewall rules and port forwarding stay outside the app. The Help tab explains a signed certificate, Let's Encrypt with win-acme, a commercial or private authority, and intermediate certificates.

### Origin header

Browsers can be pointed at a loopback MCP server via DNS rebinding. `OriginCheckMiddleware` checks `Origin`:

- No `Origin` header, or an empty one: allowed. Cursor and other native clients omit it.
- `Origin` present and equal, ordinal case-insensitive, to one of `EndpointFormatter.AllowedOrigins`: allowed. There is no normalization of a trailing slash or a default port.
- Any other `Origin`: HTTP 403, Warning `Rejected origin {origin}.`, and an error-counter increment.

The allow-list contains:

- Every bind URL (scheme and host, no path).
- `http://127.0.0.1:{http}`, `http://localhost:{http}`, and `http://[::1]:{http}`.
- When HTTPS is enabled, the same three hosts on the HTTPS port, `https://{public host}:{https}`, and `https://{each hostName}:{https}`.

### Launch allowlist

`launch_application` starts the `executable` stored on the named entry and only that path. Tool arguments cannot substitute a path. `parameters` come from the file, not from the client. A model cannot turn this server into a general process runner.

### Integrity

If the target window's process runs at a higher integrity level than `mcp_overlord` (a game launched as administrator, this app launched as a normal user), UIPI blocks both `mouse_event` and `SendInput`. `IntegrityChecker` compares the relative identifiers before sending. It blocks only when the target RID is strictly greater than this process's RID. When either RID cannot be read, the value is −1 and input is allowed. On a mismatch the tool returns `HigherIntegrity`. It does not attempt a bypass. Switching `clickBackend` does not cross an integrity boundary.

The read opens the process with `PROCESS_QUERY_LIMITED_INFORMATION`, opens the token, and reads `TokenIntegrityLevel` (25). The RID is the last sub-authority of the integrity SID.

The application editor is the other direction. Explorer's OLE drag-and-drop cannot enter an elevated process. While this process is at high integrity or above (`0x3000`), the editor turns off its OLE drop target (`AllowDrop` false, `RevokeDragDrop`) and accepts a shell file drop (`WM_DROPFILES`) after `ChangeWindowMessageFilterEx` allows `WM_DROPFILES` (`0x0233`), `WM_COPYDATA` (`0x004A`), and `WM_COPYGLOBALDATA` (`0x0049`) at process and window scope. The drop only fills editor fields. It does not send input into another process.

### Workstation lock

`lock` calls `LockWorkStation`. The server does not expose an unlock tool, so a remote client cannot unlock the session. While the session lock flag is set, or the input desktop is not `Default`, mouse and keyboard tools refuse to send input and explain why. `focus_window` and `take_screenshot` still run.

### What is redacted

Response headers `Authorization`, `X-API-Key`, and `Proxy-Authorization` are stored as `***`. Tool log messages do not include the API key. Start-failure text is scrubbed for the API key and the certificate password. Request and response bodies are stored as captured, with no size limit and no body scrub. A client that posts the key inside a JSON body will have that body in the detail view.

## 9. User interface

One main window. File dialogs, the save prompt, the application editor, the server settings dialog, and the connection report are the other windows. A fatal error dialog is shown if the embedded schema cannot be loaded. The title is `MCP Overlord - {display name}`. The display name is `New` until the document has a path, including before anything is loaded, and the file name after that. While elevated, the title is prefixed with `Administrator: `.

There is no menu bar and no keyboard shortcut on a toolbar button. Enter confirms and Escape cancels in the application editor, the server settings dialog, and the prompt. Escape closes the connection report and cancels a target drag. The log grid keeps the built-in Ctrl+C row copy.

### Layout

Default size 1100×700. `MinWidth` 800, `MinHeight` 500. At runtime the minimum width is raised to the measured width of the two toolbar groups plus margins and window chrome, and it is measured again when the DPI, the window state, the size, the Start/Stop label, the status text, or the Restart button's visibility changes. The path text shrinks before a toolbar button is clipped.

```text
+----------------------------------------------------------------------------------+
| MCP Overlord - magicraid.overlord                                                |
| [New] [Load] [Save] [Save as] [Settings] [Check] [Restart]  path   ( Start ) Running  Help |
| http://127.0.0.1:5000/mcp [Copy]   https://127.0.0.1:5001/mcp [Copy]     not live |
+---------------------------+------------------------------------------------------+
| Applications        [+][e][d][x] | Log                          [All v] [Clear] |
| Infinite Magicraid        [e] | 11:02:01.120  Info   request take_screenshot  |
|   running                    |                                                  |
|   Infinite Magicraid game    +--------------------------------------------------+
| Magicraid Starter         [e] | Detail | Stats | Help                            |
|   stopped                    | time, level, category, message, copy buttons     |
|   Use this to launch...      | [JSON] [Hex] [ASCII]                             |
|                              | Request | Response | Image                        |
+---------------------------+------------------------------------------------------+
```

The root is a `DockPanel`. The top is a stack of three rows: the toolbar, the endpoints, and the pending-listener line. Below that, a three-column grid: the applications column (default width 280), a 6 px splitter, and the right side. The right side is a three-row grid: the log (`*`), a 6 px splitter, and the bottom panel (default height 300). The bottom panel is a `TabControl` with the tab strip at the bottom: Detail, Stats, Help. There is no docking framework. A restored splitter width is kept only when it is at least 120, and a restored bottom height only when it is at least 80.

**Toolbar**

A three-column grid: file commands, the path (`*`), server commands. Each file button shows an SVG icon and a label with an 8 px gap.

| Order | Label | Icon | Tooltip | Enabled when |
| --- | --- | --- | --- | --- |
| 1 | New | `new.svg` | `New configuration` | always |
| 2 | Load | `folder.svg` | `Load configuration` | always |
| 3 | Save | `save.svg` | `Save configuration` | a document is open. Accent background and border while dirty |
| 4 | Save as | `save_as.svg` | `Save configuration as` | a document is open |
| 5 | Settings | `gear.svg` | `Server settings` | a document is open |
| 6 | Check connection | `network_check.svg` | `Check HTTP and HTTPS endpoints` | always |
| 7 | Restart as Administrator | `shield.svg` | `Stop, then start this program elevated` | visible only when the process is not elevated |

The path is `ConfigPathDisplay`, trimmed with character ellipsis, full path as the tooltip. The empty state is `No configuration loaded`. A new document shows `New configuration`. A long path is shortened in the middle to 72 characters (`StatsText.EllipsizeMiddle`). While dirty, a warning-colored `●` and the words `Unsaved changes` sit beside the path.

Server commands, on the right:

1. Start/Stop, minimum width 110, `play.svg` or `stop.svg`, label `Stop` while Running and `Start` otherwise. Disabled while Starting and when no valid document is in the store. It uses the accent button style.
2. Status text, tooltip the same string, max width 96 with ellipsis. Colors: Stopped muted, Running success, Starting warning, Error and Config error. Error text is `Error: {message}`. A failed load while stopped shows `Configuration invalid, N errors — see log`.
3. Help (`help.svg` plus `Help`, no tooltip). It selects the Help tab. It does not open a window.

**Endpoints row**

A wrapping list. Each item is the URL in Cascadia Mono (Consolas when Cascadia Mono is absent) and a Copy button (`copy.svg` plus `Copy`). Copy writes the URL to the clipboard and logs Info `Copied {url}`. Docked on the right, in the warning color: `not live` when the URLs come from the loaded document rather than the running listener, and empty while the listener is up. Under the row, shown only while listener settings are pending: `Listener settings take effect on the next start. The running endpoints still use the previous values.`

### Applications panel

Header `Applications`, then icon-only buttons. Tooltips stay visible when the button is disabled.

| Button | Icon | Tooltip | Enabled when |
| --- | --- | --- | --- |
| Add | `add.svg` | `Add application` | a document is open |
| Edit | `pencil.svg` | `Edit application` | a row is selected |
| Duplicate | `duplicate.svg` | `Duplicate application` | a row is selected |
| Remove | `remove.svg` | `Remove application` | a row is selected |

The list is a single-select `ListBox` on a surface-colored border. Each row shows the name in semi-bold, a pencil button docked on the right (`Edit application`), `running` or `stopped` in muted text, and the description wrapped in muted text. Running and stopped are not color-coded. Selection is tracked by name so the one-second refresh does not clear it. Double-clicking the list opens the editor for the selected row. There is no context menu.

A `DispatcherTimer` every 1 second calls `RefreshApplications`, and every document change does the same. The lookup runs on the thread pool. A refresh that arrives while one is running is coalesced into one more run. An application counts as running when `WindowLocator.Find(application, restore: false)` finds the window or reports the process running.

Duplicate names the copy `{name} (2)`, then `(3)`, and opens it in the editor as a new entry. Remove asks `Remove '{name}'?` with title `Remove application` and confirm label `Remove`. OK in the editor applies the draft to the live store immediately and marks the file unsaved.

### Log panel

Header `Log`. On the right, a level combo (width 120: All, Info, Warning, Error) and Clear (`clear.svg` plus `Clear`).

The grid is read-only, full-row selection, columns only:

| Column | Binding | Default width | Sort |
| --- | --- | --- | --- |
| Time | `HH:mm:ss.fff` | 120 | `Timestamp` |
| Level | level name | 90 | `Level` |
| Message | message | `*` | `Message` |

Category is not a column. It appears in the Detail tab. Rows alternate between the surface color and the window background. The font is Cascadia Mono, then Consolas. There is no per-level row color and no search box.

The filter rebuilds the visible rows from a buffer snapshot. The buffer keeps every level. `EntriesAdded` is marshalled with `BeginInvoke` at background priority, and a burst becomes one pull. Clear empties the whole buffer, including lines the filter is hiding. Capacity is `server.logRetention` (default 1,000).

Auto-scroll: a user scroll that is not caused by new content leaves the log stuck to the tail when the viewport is within 4 px of the bottom. After a rebuild, a stuck log scrolls the last row into view. A header click sorts the grid, and the column and direction are saved.

Selecting a row fills the Detail tab.

### Detail tab

With nothing selected, the placeholder is `Select a log line to see the full detail.`

One outer vertical scroller holds:

- `NetworkNote` in the warning color when the line has no captured exchange: `This line has no captured connection.`
- A field list. The label column is 110 px. A non-empty value has a copy icon (`Copy`).
- Always: Time (`yyyy-MM-dd HH:mm:ss.fff`), Level, Category, Message.
- When an exchange exists: Source, Destination (`address:port`, IPv6 in brackets), Scheme, Method, Path, Status, Duration (`N ms`), and Connection when it is non-empty.
- A body-view radio group: JSON (the default), Hex, ASCII. Hidden on the Image tab.
- An inner tab control: Request, Response, Image. Top tabs.

Request and Response share one template: headers as `Name: Value`, a muted note, then the body.

| Situation | Note |
| --- | --- |
| No captured request body | `No HTTP request body was captured. Showing the tool arguments.` |
| GET event stream | `The response body was not stored because this request is a long-lived event stream.` plus ` Showing the tool result.` when a tool result is shown instead |
| Response not captured | `The HTTP response was not captured.` |
| Empty response body | `The HTTP response body is empty.` |

Body views:

- JSON: a read-only, no-wrap text box with its own scrollbars, max height the viewport. A string longer than 1,024 characters whose first 128 characters are in the base64 or base64url alphabet becomes `[base64 N characters — see the Image tab]`. Formatting is cut at 2,000,000 characters with `[Formatted JSON was cut at 2 MB. The Hex view has the full body.]` Text that is not JSON is `This body is not JSON.` plus a blank line and the raw text. An SSE body (`data:` at the start, or a line that is `data:`) formats each event's data as JSON and keeps other lines, such as `event:`, verbatim.
- Hex: a virtualized list, 16 bytes per line. Offset is eight uppercase hex digits, then uppercase `XX` bytes, then an ASCII column (bytes 32–126 as characters, anything else `.`). Short lines are padded.
- ASCII: a virtualized list of UTF-8 text, wrapped at 120 columns, keeping `\r\n`, `\r`, and `\n` breaks and stripping the trailing break from each line.

`ComputedLineList` builds each line when the list asks for that index, so a large body is not materialized up front. The list virtualizes with recycling. A short hex or ASCII list shrinks to its content. A long one stays at the viewport height. A mouse wheel the inner list does not consume is forwarded to the outer scroller.

Image tab: `This line has no image.` when empty. Otherwise each image has a caption `Image N  {mime}  {width} x {height}, {bytes} bytes`, plus `The image could not be decoded.` when the decode fails, and the picture at uniform stretch with max height 280. Save image suggests `image-N.png` or `image-N.jpg` and writes the raw bytes. A write failure is a dialog titled `Save image`.

### Stats tab

Refreshed every 500 ms from `StatsCounter.Snapshot`. Four lines plus a per-tool line:

```text
Uptime 00:12:04    Requests 128    Errors 3
Last 42 ms    Average 80 ms
Screenshots 40    Bytes 18.2 MB    Inputs 70
Sessions 1
key_press 15   mouse_click 55   take_screenshot 40
```

Uptime is `00:00:00` when the server is not running. Hours are total hours and may exceed 24. A negative span is shown as zero. Average latency is rounded to a whole number. Bytes use base 1024: below 1024 is `N B`, below 1 MiB is `0.0 KB`, otherwise `0.0 MB`, invariant culture. Per-tool names are sorted ordinally, each as `name count`, joined by three spaces, muted and wrapping. Section 10 defines what each counter counts. The Help text calls Sessions `MCP requests in flight`.

### Application editor

Title `New application` or `Edit application`. Size 720×680, minimum 560×480, centered on the owner, dark title bar. Its position is persisted.

The top row is a drop zone and a target square. The drop zone has a dashed outline, radius 4, minimum height 48, `drop.svg` sized to the font, and the text `Drop a shortcut (.lnk) or an application (.exe) here`. The target is a square whose side equals the drop zone's actual height: raised background, accent border, radius 4, crosshair cursor, `target.svg` inside a view box. Tooltip: `Drag onto a window to read its title, executable, and process`.

Drop is accepted by the whole window.

- A normal-integrity process uses OLE `AllowDrop` with the Copy effect for `FileDrop`.
- An elevated process uses the shell drop in section 8 and reads the first path from `WM_DROPFILES`.
- Any other file type shows `Drop a shortcut (.lnk) or an application (.exe).`

`.exe` fills name (file stem), executable (full path), working directory (its folder), and process name (file stem). `.lnk` is read on an STA thread through `IShellLinkW` and `IPersistFile`: target path, arguments, working directory, and description (1024-character buffers). Name is the shortcut's file stem. Process name is the target's file stem. Failures: `A shortcut path is required.`, `Shortcut file was not found: {path}`, `The shortcut could not be read: {message}`. Each optional field a drop fills has its Save box checked.

The target drag: mouse down captures the mouse, switches to a crosshair, hides the target icon, and creates `WindowHighlightOverlay`. Mouse move calls `WindowFromPoint` and then `GetAncestor(GA_ROOT)`. A window owned by this process is ignored (handle 0). The overlay is a topmost, click-through, layered tool window that never activates, placed with `SetWindowPos` over the target rectangle: a 3 px `#3A7AFE` border and the same color at alpha 40 as the fill. The preview line is `{title or (no title)} — {process or unknown process}`. Idle text is `Drag the target onto a window.` During a drag with nothing under the cursor it is `No window under the cursor.` Mouse up fills the fields. Escape cancels.

`TryDescribe` returns title, process name, executable (`QueryFullProcessImageName` after `PROCESS_QUERY_INFORMATION | PROCESS_VM_READ`), working directory, and arguments. The working directory and the command line are read from the target's process environment block (`NtQueryInformationProcess`). A 64-bit target uses the environment block at offset `0x20` inside the process parameters, current directory at `0x38`, command line at `0x70`. A 32-bit target under WOW64 uses info class 26, environment block at `0x10`, current directory at `0x24`, command line at `0x40`. Arguments are split with `CommandLineSplitter`: a leading quote returns the text after the closing quote (or empty when the quote never closes); otherwise the text after the first space, left-trimmed. Tabs are not separators. `\??\` prefixes and trailing slashes are stripped from directories.

The target fills window title, executable, process name, working directory, and parameters, and checks Save on each optional field it fills. `DropStatus`, in the success color, reports what was filled, for example `Filled window title, executable, and process name.` or `Nothing was filled.`

Under that, muted: `A checked field is saved, even when it is empty. An empty optional field is treated as not set.`

Fields, in a four-column grid (marker, label at least 130 px, editor, extra button):

| Field | Marker | Editor |
| --- | --- | --- |
| Name | `Required` | text box |
| Executable | `Required` | text box plus a folder button, tooltip `Browse for an executable`, filter `*.exe` and All files |
| Window title | `Required` | text box |
| Parameters | Save | text box |
| Working directory | Save | text box plus a folder button (`OpenFolderDialog`, title `Working directory`). Picking a folder checks Save |
| Process name | Save | text box |
| Description | Save | multi-line, minimum height 56, wrapping |
| Click backend | Save | combo `mouseEvent` / `sendInput` |

An unchecked optional editor is drawn at 0.45 opacity.

Local checks, before the loader runs:

- `Name is required.`
- `An application named '{name}' already exists.`
- `Executable is required.`
- `Executable must include a file name.`
- `Window title is required.`

Loader errors whose path is `/applications/{index}/name`, `executable`, or `windowTitle` are shown on that field. Any other loader error is joined into one form error. Cancel is the cancel button. OK is the default button. The dialog stays open when commit fails.

### Server settings dialog

Title `Server settings`. Size 680×700, minimum 520×460, dark title bar, position persisted.

Messages at the top, in order: muted `Listener changes apply the next time the server starts.`; the same Save hint as the application editor; a warning that tracks the fields, `A bind address that is not loopback requires HTTPS and an API key before the server can start.`; then the error line.

Every row has a Save checkbox. Unchecked editors are dimmed.

| Field | Control |
| --- | --- |
| HTTP port | text, default `5000` |
| HTTPS port | text, default `5001` |
| HTTPS | `Enable HTTPS` checkbox |
| Bind address | editable combo: `127.0.0.1`, `0.0.0.0`, `::1`, `::`, this machine's non-loopback IPv4 and IPv6 addresses, and the current value |
| API key | `PasswordBox` swapped with a `TextBox` by Show/Hide. Generate fills the key, checks Save, and switches to Show |
| Certificate path | text plus browse, filter `*.pfx` and All files. Picking a file checks Save |
| Certificate password | `PasswordBox`, no show toggle |
| Host names | multi-line, minimum height 72, tooltip `One DNS name or IP address per line` |
| Log retention | text. Tooltip and note: `Log retention applies when you press OK. It does not restart the listener.` Default 1,000 |

Generate draws 24 bytes from `RandomNumberGenerator` and encodes them as Base64 with no padding: 32 characters from `A–Z`, `a–z`, `0–9`, `+`, and `/`.

Validation, first failure wins:

1. `HTTP port must be an integer from 1 to 65535.` The HTTPS port uses `HTTPS port must be an integer from 1 to 65535.`
2. `The HTTPS port must differ from the HTTP port when HTTPS is enabled.`
3. `Bind address must be an IP address.`
4. `API key must be empty or at least 16 characters.`
5. `Log retention must be an integer from 100 to 1000000.`
6. Then the loader errors, joined into one line.

Cancel and OK behave as in the application editor. OK applies through the editor session, so application data is unchanged and listener fields become pending when the server is running and `HasSameListener` is false. Log retention applies on OK.

### Prompt dialog

`PromptDialog` is 520 px wide, sized to its content, not resizable, centered on the owner (or on the screen when there is no owner). Buttons, left to right: `Don't save` (save prompts only), `Cancel` (the cancel button), and the confirm button (the default: `OK`, `Save`, or `Remove`). Closing the dialog counts as Cancel. The same dialog shows ordinary messages, the missing-schema error, and the unhandled-exception message. Its title bar stays the system color. Its position is not saved.

### Connection report

Title `Connection check`. Size 760×640, minimum 560×420, dark title bar, position persisted. The check starts on load and is cancelled when the dialog closes. Opened from Check connection with the endpoints currently shown and whether the state is Running.

The status line reads `Ready.`, then `Checking...`, then `Checking {url}`, then `Finished.`, `Cancelled.`, or `The check failed.` In the last case the report text is the exception message.

The step list shows a 48 px `PASS`, `WARN`, or `FAIL` label (success, warning, or error color), the step name, and the detail indented 48 px in muted text. Under that, a 220 px read-only monospace text box holds the full report. Buttons: `Run again` (disabled while running), `Copy` (the report text), `Close` (cancel). Section 13 defines the probes.

### Help

`HelpContent.Text` is plain text in a wrapping block inside a scroller on the Help tab. Headings, in order, after an untitled introduction:

1. The introduction: what the program is, the title-bar file name, `New`, and the `Administrator:` prefix with the dark blue bar.
2. `New, Load, Save, and Save as`
3. `Unsaved changes`
4. `Applications`
5. `Filling an application`
6. `Settings`
7. `Check connection`
8. `Restart as Administrator`
9. `Start and Stop`
10. `Signed certificate`
11. `Let's Encrypt with win-acme`
12. `A commercial or company certificate authority`
13. `A private certificate authority`
14. `Intermediate certificates`
15. `Using the certificate`
16. `Endpoints`
17. `Log`
18. `Stats`
19. `Tools`
20. `clickBackend`
21. `Cursor on this machine`
22. `Start menu`
23. `Layout`
24. `Command line`

The certificate sections tell the operator that a public authority signs a DNS name and does not sign `127.0.0.1`, `localhost`, or a bare IP; how win-acme writes a PFX and renews a 90-day Let's Encrypt certificate around day 60 (the listener must be restarted to pick up the new file); the OpenSSL request `openssl req -new -newkey rsa:2048 -nodes -keyout server.key -out server.csr -subj "/CN=play.example" -addext "subjectAltName=DNS:play.example"` and the matching `certreq` INF (`KeySpec = 1`, `KeyLength = 2048`, `Exportable = TRUE`, `HashAlgorithm = SHA256`, `RequestType = PKCS10`); `openssl pkcs12 -export` to build the PFX, including intermediates with `-certfile`; that the root belongs in the client's Trusted Root store and not in the PFX; and that `hostNames` is copied into a certificate this server creates for itself and leaves a supplied file unchanged. Check connection should then show TLS as `PASS`.

The tools section lists the seventeen tools, the coordinate rules, and the 15 second timeout. The layout section lists everything section 14 saves. The command-line section matches section 4, including `--register-shell`.

### Dark theme

`App.xaml` merges `Resources/Styles.xaml`. There is no separate theme file and no light theme.

| Key | Color |
| --- | --- |
| `WindowBackgroundBrush` | `#1E1E1E` |
| `SurfaceBrush` | `#252526` |
| `RaisedBrush` | `#2D2D30` |
| `BorderBrush` | `#3F3F46` |
| `PrimaryTextBrush` | `#E0E0E0` |
| `MutedTextBrush` | `#9D9D9D` |
| `AccentBrush` | `#3A7AFE` |
| `SuccessBrush` | `#3FB950` |
| `WarningBrush` | `#D29922` |
| `ErrorBrush` | `#F85149` |
| `AdministratorTitleBarColor` | `#FF0B2A4A` |

An implicit `Window` style sets the background, the foreground, and `FontSize` 14. Implicit window styles do not apply to subclasses, so `MainWindow` and each dialog set their own background and foreground. Other implicit styles cover `TextBlock`, `Button`, `TextBox`, `PasswordBox`, `ListBox`, `ListBoxItem`, `ComboBox` (a full template: raised face, accent border on hover or when open, chevron, raised popup), `ComboBoxItem`, `ScrollViewer`, `ScrollBar`, `DataGrid` and its column header, cell, and row, `GridSplitter`, `TabControl`, `TabItem`, `RadioButton`, `CheckBox`, and `ToolTip`.

Buttons: accent background, white text, padding 12,6, margin 4,0, hand cursor, corner radius 4. Disabled is 0.45 opacity. Hover is 0.9 opacity. Text boxes use the raised background, padding 6,4, and a light caret. The selected list item uses the raised background. Data-grid headers use the raised background and padding 8,6. The selected grid row uses the raised background. Tabs on top use the surface color and an accent border when selected.

Keyed styles: `ToolbarButton` (raised, border color), `IconButton` (toolbar button, padding 6, margin 2, tooltip shown when disabled), `DirtyIndicator`, `FieldLabel` (minimum width 130), `FieldError`, `DropZoneOutline`, `FinderTarget`, `BottomTabItem`, `BottomTabControl` (tab strip at the bottom).

Converters: `BoolToVisibility`, `InverseBoolToVisibility`, `StringToVisibility`, `IncludeOpacity` (1.0 or 0.45), `BodyViewEquals`, `PositiveLength`.

`DarkTitleBar.Apply` sets `DWMWA_USE_IMMERSIVE_DARK_MODE` (attribute 20) on the main window and on the application editor, server settings, and connection report at source-initialized time. On a Windows build that ignores the attribute, the client area is still dark and the title bar stays the system color. When the process is elevated, `ApplyCaptionColor` sets attribute 35 to `#0B2A4A`.

### Icons

SVG files live in `Resources/Icons/` and are WPF resources: `add`, `app`, `clear`, `copy`, `drop`, `duplicate`, `folder`, `gear`, `help`, `network_check`, `new`, `pencil`, `play`, `remove`, `save`, `save_as`, `shield`, `stop`, `target`. They are single-color, with `#E0E0E0` baked into the file. `SvgViewbox` draws them. Width and height bind to the parent button's or stack's `FontSize`, so the icon tracks the text. The target icon instead fills a view box sized to the drop zone. `app.svg` is not referenced at runtime. Windows use `app.ico`. The combo-box chevron is an inline `Path` in `Styles.xaml`, sized to the font.

### Commands

`NewConfig`, `LoadConfig`, `SaveConfig`, `SaveConfigAs`, `EditServerSettings`, `CheckConnection`, `RestartAsAdministrator`, `ToggleServer`, `ShowHelp`, `CopyEndpoint`, `AddApplication`, `EditApplication`, `EditApplicationRow`, `DuplicateApplication`, `RemoveApplication`, `ClearLog`, `Detail.CopyText`, `Detail.SaveImage`. The connection report adds `RunCommand` and `CopyCommand`.

## 10. Logging and stats

### Log entry

```csharp
enum LogLevel { Info, Warning, Error }

record LogEntry(
    DateTimeOffset Timestamp,
    LogLevel Level,
    string Category,
    string Message,
    LogDetail? Detail = null);
```

`Timestamp` is local `DateTimeOffset.Now`, stamped by the buffer on append. `Category` is a short tag. The categories this build writes are `config`, `server`, `mcp`, `capture`, and `ui`. The grid prints timestamp, level, and message. Category is shown in the Detail tab.

`LogDetail` carries the tool name, the full (untruncated) tool-argument JSON, the tool-result JSON, decoded images, and the `HttpExchange` for that request when one was captured.

### Buffer

`LogBuffer` is a linked list under a lock. `DefaultRetention` is 1,000 and `MinimumRetention` is 100. The `Retention` setter clamps to the minimum and drops the oldest entries immediately. The value comes from `server.logRetention` (100–1,000,000, default 1,000) and is applied whenever the document is published. `Append` stamps the entry, appends it, trims, and raises `EntriesAdded` outside the lock. `Clear` drops the contents and raises the same event. `Snapshot` returns an array copy.

The UI subscribes with `BeginInvoke` at background priority, so a burst of tool calls cannot stall Kestrel on the UI thread. A burst coalesces: the UI pulls every pending entry once per dispatcher turn.

### Tool-call log

`ToolCallLog` wraps every tool through the SDK call-tool filter. `ArgumentLimit` is 500. `TOOL_CALL_TIMEOUT` is 15 seconds. A missing tool name is logged as `unknown`. Category is `mcp`. Each line carries a `LogDetail`.

| Event | Level | Message |
| --- | --- | --- |
| Entry | Info | `request {tool} {compact args}` |
| Success | Info | `response {tool} {summary} {elapsed} ms` |
| Tool error (`isError`) | Error | `error {tool} {first text, one line, cut at 500} {elapsed} ms` |
| Timeout | Warning | `timeout {tool} The tool did not finish within 15 seconds. {elapsed} ms` |
| Exception | Error | `error {tool} {Type}: {Message} {elapsed} ms` plus a newline and `exception.ToString()` |

Arguments in the request line are `JsonSerializer.Serialize` of the argument object, cut at 500 characters with `...` appended. Empty arguments produce an empty string. The request line is not scrubbed. The detail keeps the compact JSON without truncation.

The success summary for an image result is `{format} {imageWidth}x{imageHeight} {decoded byte count} bytes`, read from the first text block's JSON (`imageWidth`, `imageHeight`, `format`). Any other success uses the first text block on one line, cut at 500 characters.

On timeout the client receives `ToolErrors.TimedOut` and `RecordToolError` runs. The original call keeps running; its later exception is observed and discarded. The log records a warning, which matches the Help text.

On an exception the client receives `isError` with `The tool failed ({Type}): {Message}`. The stack trace is inside the log message. The host stays up.

`RecordToolSuccess(tool, milliseconds, screenshot, bytes, input)` runs on success. `screenshot` is true when the tool name is `take_screenshot`. `bytes` is the decoded image length. `input` is true when the name starts with `mouse_` or `key_`, or equals `type_text`. `RecordToolError` runs for tool errors, timeouts, and exceptions.

HTTP 401 and 403 are Warning entries from the guard middleware, category `server`, and they call `RecordHttpFailure`. Their log detail holds the `HttpExchange`.

### Stats

`StatsSnapshot` is `Requests`, `Errors`, `Screenshots`, `Bytes`, `Inputs`, `ActiveSessions`, `LastLatencyMilliseconds`, `AverageLatencyMilliseconds`, and `PerTool`.

| Stat | How it moves |
| --- | --- |
| Requests | Incremented when a tool call completes, including errors and timeouts. |
| Errors | Tool errors, timeouts, exceptions, and HTTP 401/403. |
| Last latency | Duration of the most recent completed tool call, including errors. |
| Average latency | Mean of those durations. The latency total and count sit under one lock with the per-tool dictionary. |
| Screenshots | Successful `take_screenshot` calls. |
| Bytes | Sum of decoded image payload sizes on successful image results. |
| Inputs | Successful calls whose name starts with `mouse_` or `key_`, plus `type_text`. The counter moves when the tool returns success, which is after the gesture was sent. |
| Sessions | `InFlightCounter` after `InFlightMiddleware` increments, and again in `finally` when it decrements. Rejected requests never reach this middleware. A long-lived GET event stream counts until it closes. Stop sets the value to 0. The SDK session table is not queried. |
| Per tool | One count per tool name, including errors and timeouts, copied into the snapshot under the same lock. |

There is no reset. Only the session count returns to 0 on stop. Counts accumulate until the process exits. `Snapshot` copies the numbers. The 500 ms UI timer calls it on the dispatcher.

`StatsText.FormatBytes` uses base 1024 and the invariant culture: below 1024 is `N B`, below 1 MiB is `0.0 KB`, otherwise `0.0 MB`. `FormatUptime` is `hh:mm:ss` with total hours, so the hour field can exceed 24. A negative value is zero. `FormatPerTool` sorts names ordinally and joins `name count` with three spaces. `EllipsizeMiddle(value, max)` keeps `(max - 3) / 2` characters on each side of `...`, and leaves the string alone when `max` is below 5. The path display uses 72.

The log stays in memory. `LogEntry` is the extension point for a later file sink. The schema has no file-log field.

## 11. Error handling

Tool failures use the MCP tool-error flag (`isError: true`) and one text content message written for the model. The UI log gets the same message at Error, inside the `error {tool} ...` line.

| Situation | Channel | Message |
| --- | --- | --- |
| No document loaded | Tool error | `No configuration is loaded.` |
| Unknown `name` | Tool error | `Unknown application '{name}'. Configured names: {a, b}.` An empty list uses `(none)`. |
| Empty executable on launch | Tool error | `Application '{name}' is not launchable. It has no executable in the configuration.` |
| `Process.Start` failed | Tool error | `Failed to launch '{name}': {os message}` |
| `find_window` finds nothing | Success, `found: false` | Section 5. Suggests launch when the entry is launchable. |
| `take_screenshot`, focus, or input finds nothing | Tool error | Launchable: `The window for '{name}' was not found. Call launch_application, then retry.` Otherwise: `The window for '{name}' was not found. Call find_window for the current state.` |
| `region` misses the window | Tool error | `The region does not intersect the window ({w}x{h}).` |
| Mouse point outside the window | Tool error | `The point ({x}, {y}) is outside the window ({w}x{h}).` Nothing is focused and no input is sent. |
| Capture size disagrees and no frame survives | Tool error | `The capture is {bw}x{bh} but the window is {w}x{h}. The window may have changed size, or the capture DPI does not match the window. Retry take_screenshot.` |
| Screenshot encode failed | Tool error | `Screenshot encode failed: {message}` |
| Black frame after every capture method | Success plus `warning` | `The captured frame is black. The game is likely using exclusive fullscreen or a protected surface.` |
| Unknown `key` | Tool error | `Key '{key}' was not recognized.` |
| Unknown `button` | Tool error | `Button '{button}' was not recognized. Use left, right, or middle.` |
| Unknown `coordinateSpace` | Tool error | `coordinateSpace '{value}' was not recognized. Use image or window_rect.` |
| Unknown `format` | Tool error | `format '{value}' was not recognized. Use png or jpeg.` |
| `type_text` empty or longer than 500 | Tool error | `text must be between 1 and 500 characters. The value has length {n}.` |
| `holdMs`, `delta`, or `waitForWindowSeconds` outside range | Clamp, mention the applied value in `message` | The call proceeds. `maxWidth` and JPEG `quality` clamp with no message. |
| Target process has higher integrity | Tool error | `The target process runs at a higher integrity level than mcp_overlord. Run mcp_overlord at the same integrity level as the game.` |
| Workstation is locked | Tool error | `The workstation is locked, so mouse and keyboard input was not sent. Unlock the screen at the console. This server has no unlock tool.` |
| `LockWorkStation` failed | Tool error | `Failed to lock the workstation: {os message}` or `LockWorkStation returned false.` |
| Tool still running after 15 seconds | Tool error, work continues | `The tool did not finish within 15 seconds.` Log level Warning. |
| `SetForegroundWindow` failed | Success, `focused: false` | `The window could not be brought to the foreground. Input may still be delivered at the window's screen position.` Input tools continue after the focus attempt. |
| HTTP 401 | HTTP, empty body | Warning `Rejected a request with a missing or wrong API key.` |
| HTTP 403 origin | HTTP | Warning `Rejected origin {origin}.` |
| Listener failed (port in use, certificate) | UI state Error | The scrubbed exception text, including the stack, in the log. No partial listener is left open. |
| Client disconnects mid-call | Drop the response | The input gesture already in progress finishes under the gate so a button is not left down by a torn click. A `mouse_down` that completed is not auto-released; the model owns button state across calls. If the process is stopping, `InputService` releases any button it still considers down, unless the screen is locked. |
| Unhandled exception in a tool | Tool error | `The tool failed ({Type}): {Message}` plus an Error log line that contains the stack trace. |

`holdMs` clamp text is `holdMs was clamped to {v}.` `delta` clamp text is `delta was clamped to {v}.` Both can appear in one mouse `message`, joined by a space. `waitForWindowSeconds` clamp text is `waitForWindowSeconds was clamped to {v}.` NaN and infinity become 0 and count as clamped.

## 12. HTTP traffic capture and the log detail payload

`TrafficCaptureMiddleware` is the first middleware, so it sees every request, including ones the guards reject.

It creates an `HttpExchange` and stores it in `HttpContext.Items["mcp_overlord.HttpExchange"]` and in the async-local `HttpExchangeAmbient.Exchange`. `ToolCallLog` reads the async-local first, then the `IHttpContextAccessor`. The request line and the response line share that same object. Response fields fill in when the request ends, and the detail view reads them later.

Captured fields: connection id, remote IP and port, local IP and port, scheme, method, path plus query, request headers, and the start time. The request body is fully buffered with `EnableBuffering` and then rewound. There is no size cap.

A GET sets `ResponseBodyOmitted` and does not capture the body, because that response is the SSE event stream. Every other method tees the response into a `MemoryStream` through a capturing stream that also replaces `IHttpResponseBodyFeature`. There is no size cap, so a screenshot's base64 is stored in full. A `finally` records the status, the response headers, and the duration, and clears the async-local.

Headers `Authorization`, `X-API-Key`, and `Proxy-Authorization` are stored as `***`. Bodies are not scrubbed.

`LogPayloads.Describe` builds the view the Detail tab binds:

- `HasNetwork` is true when the exchange has a method, a remote address, or a local address. Otherwise the note is `This line has no captured connection.`
- Fields, in order: Time (`yyyy-MM-dd HH:mm:ss.fff`), Level, Category, Message, and, when there is a connection, Source, Destination, Scheme, Method, Path, Status, Duration (`N ms`), and Connection when it is non-empty.
- Request body: the captured body, or the tool arguments when no HTTP body was captured.
- Response body: the captured body, or the tool result. Image blocks inside the stored tool-result JSON become `{ "type": "image", "mimeType", "byteLength", "data": "[see the Image tab]" }`. A failure result is `{"exception": type, "message": message}`.

`JsonFormatter`, `HexDumpFormatter`, and `TextLineList` produce the three body views described in section 9. `ComputedLineList` is a fixed-size read-only list that builds a line when it is indexed, bound to virtualized list boxes (`VirtualizingPanel.IsVirtualizing` true, recycling, `CanContentScroll` true).

## 13. Connection diagnostics

Check connection runs `ConnectionDiagnostics.CheckAsync` against every public endpoint of the settings currently shown, in order. With no settings the report text is `No configuration is loaded, so there is no endpoint to check.` Progress reports `Checking {url}`.

Each step's name in the report is `{url}  {step}`. The steps for one endpoint:

| Step | What it does | When it stops the endpoint |
| --- | --- | --- |
| URL | Parses the URL. Failure detail: `The endpoint URL is not valid.` | Invalid URL |
| Server | Passes when this process is running: `This process is listening for clients.` Fails with `This process is stopped.` | Never. Later steps still run. |
| Port | `IPGlobalProperties.GetActiveTcpListeners` looks for any listener on that port, on any address, from any process. | Never by itself. |
| DNS | An IP literal is itself. Otherwise `Dns.GetHostAddressesAsync`. Failure: `The host name did not resolve.` plus the detail. | DNS failure |
| Address | Always passes. Says whether the address is assigned to this machine (`NetworkInterface` unicast addresses, or loopback). Classifies public versus private: 10/8, 172.16/12, 192.168/16, 169.254/16, IPv6 link-local, fc00::/7, and loopback are private. | Never |
| TCP | `TcpClient.ConnectAsync` with a 3 second timeout. Outcomes: ok, refused, timeout, error. | Anything other than ok |
| TLS | HTTPS only. `SslStream` over TLS 1.2 and 1.3, 3 second timeout, target host the URL host. The validation callback always accepts and records subject, SANs, expiry, chain status, name mismatch, chain errors, self-signed (raw subject equals raw issuer), and thumbprint. | See classification below. The endpoint still continues to MCP when the classification is not Fail. |
| MCP initialize | `HttpClient` POST, 5 second timeout, any certificate accepted. Headers: `Accept: application/json, text/event-stream`, `MCP-Protocol-Version: 2025-11-25`, and, when a key is set, both `Authorization: Bearer` and `X-API-Key`. Body: JSON-RPC `initialize`, id 1, empty capabilities, `clientInfo` `mcp-overlord-check` / `1.0.0`, `protocolVersion` `2025-11-25`. Passes on HTTP 2xx whose JSON-RPC result parses without an error. The detail adds the server name and version and the protocol. For HTTPS it adds that certificate checks were skipped on this call so the status could be read. | A failure is a failed step. The tools call still runs. |
| `notifications/initialized` | Sent with `Mcp-Session-Id`. It does not add a report step. | |
| MCP tools/list | Passes on HTTP 2xx with no JSON-RPC error. Detail includes `{n} tools ({names})`. At most eight names are listed, then `...`. | |

TLS classification:

- Fail when the handshake failed or the certificate name does not match the host.
- Warning when the handshake completed, the name matches, and the certificate is self-signed. Trusting it locally does not change this to Pass. Remote clients still have to trust it themselves.
- Fail when it is not self-signed and the chain is untrusted.
- Pass otherwise.

`Auto-created` means the thumbprint equals `%LOCALAPPDATA%\mcp_overlord\server.cer`.

Insights are added only when the endpoint has a failure or a warning. The hint set covers: server stopped, port closed, DNS failed, connection refused (`The connection was refused. The port is closed, or a firewall rejected it.`), timeout (`The connection timed out. A firewall may be dropping the packets, or the route does not reach the host.`), TLS transport failure, a public address that is not on this machine (NAT loopback or a port-forward), a non-local address, HTTP 401 (`The server rejected the API key.`), HTTP 403 (`The server rejected the Origin header.`), and the certificate hints.

Self-signed and auto-created: the insight says the certificate was created because no certificate path is configured, names `%LOCALAPPDATA%\mcp_overlord` and `server.cer`, says no authority vouches for it until it is in the client's Trusted Root Certification Authorities store, says the API key could be intercepted until then, and points at the Help section `Signed certificate`. A self-signed certificate loaded from a configured file gets the same warning with that file named instead. An untrusted certificate signed by someone else, or a name that does not match, stays Fail.

When a network step failed and the address is not loopback, the check pings (TTL 128, 1 second, 32-byte payload) and then traces the route: ping with TTL 1 through 20, 1 second each, stopping on success or unreachable. Each hop is `{ttl}  {reply address or detail}`. The insight lines are `Ping: ...` and `Trace route:`.

Report text:

```text
Checked N endpoints. F failed, W warnings.

https://127.0.0.1:5001/mcp
  PASS  https://127.0.0.1:5001/mcp  Server  This process is listening for clients.
  WARN  https://127.0.0.1:5001/mcp  TLS  ...
  Insights
    ...
```

One endpoint uses `endpoint.` and one warning uses `warning.` The failed and warning counts are counts of endpoints, not of steps.

The probes use managed .NET only: `IPGlobalProperties`, `NetworkInterface`, `Dns`, `TcpClient`, `SslStream`, `HttpClient`, and `Ping`. There is no firewall API and no `netsh`.

`McpReplyParser` tries the whole body when it starts with `{` or `[`, then each SSE `data:` block. The first object with `error` is a JSON-RPC error. The first object with `result` supplies the protocol version, `serverInfo` name and version, and the tool count and names. A body with neither is not parsed.

`ProbeKinds` names the low-level outcomes: ok, refused, timeout, ttlExpired, unreachable, dns, tls, http, error. `CheckStepStatus` is Pass, Warning, or Fail.

## 14. UI persistence

The file is `%LOCALAPPDATA%\mcp_overlord\ui_settings.json`. JSON is indented, camelCase, and allows `NaN` and `Infinity` as named literals. A missing or unreadable file loads defaults. The file is written when the main window closes, after the save prompt has been answered, and after each placed dialog closes.

| JSON name | Default | Meaning |
| --- | --- | --- |
| `left`, `top`, `width`, `height` | NaN, NaN, 1100, 700 | Legacy bounds in device-independent pixels. |
| `windowBoundsPixels` | absent | `{ "x", "y", "width", "height" }` in physical pixels. The origin is the primary monitor, so a monitor to the left or above can be negative. |
| `windowState` | `Normal` | `Normal` or `Maximized`. |
| `applicationsPanelWidth` | 280 | Restored only when it is at least 120. |
| `bottomPanelHeight` | 300 | Restored only when it is at least 80. |
| `bottomPanelTabIndex` | 0 | 0 Detail, 1 Stats, 2 Help. Clamped on restore. |
| `logDetailTabIndex` | 0 | 0 Request, 1 Response, 2 Image. |
| `logDetailBodyView` | `Json` | `Json`, `Hex`, or `Ascii`. |
| `logColumnWidths` | empty | Keyed by sort member path: `Timestamp`, `Level`, `Message`. A width is restored only when it is at least 40. |
| `logSortColumn` | null | Sort member path, or null when unsorted. |
| `logSortDirection` | `Ascending` | `Ascending` or `Descending`. |
| `logLevelFilter` | `All` | `All`, `Info`, `Warning`, or `Error`. |
| `applicationEditorBoundsPixels` | absent | Physical-pixel bounds. Not maximized. |
| `serverSettingsBoundsPixels` | absent | Physical-pixel bounds. |
| `connectionReportBoundsPixels` | absent | Physical-pixel bounds. |
| `lastConfigDirectory` | null | Starting folder for the configuration dialogs and the folder picker. |

`LegacyBoundsConverter.Resolve` uses `windowBoundsPixels` when that rectangle has area. Otherwise it converts the legacy DIP values by trying each monitor at its own DPI. The first choice is the scaled rectangle fitting inside that monitor's bounds within 4 px. The second choice is the monitor that contains the rectangle's center. The fallback scales at 96 DPI.

`WindowBoundsClamp.Clamp` then:

- Picks the monitor whose work area overlaps the rectangle most. A tie, and the case with no overlap, goes to the monitor whose work-area center is nearest.
- Clamps the size between the minimum size (DIP converted with that monitor's DPI, rounded up) and the work-area size.
- Clamps the position so the window lies fully inside the work area.
- With no monitors, keeps the position and applies only the minimum size at 96 DPI.

`Win32MonitorLayout` lists monitors with `EnumDisplayMonitors`. A monitor that is off or unplugged does not appear and is never chosen. A window that was on a disconnected screen moves onto a screen that is still on.

`WindowPlacementBinder`:

- Sets DIP bounds before the native window exists, so the window is created on the right monitor.
- Calls `SetWindowPos` with the exact pixels while the window is still hidden. A `WM_WINDOWPOSCHANGING` hook and a `DpiChanged` handler hold those pixels through the DPI switch.
- Maximizes afterwards when that was the saved state.
- Tracks the normal bounds while the window is Normal. A minimized window that was maximized is saved as maximized.
- When display settings change, re-clamps. A minimized window has its saved bounds clamped. A maximized window is moved back and maximized again. A normal window is moved inside a work area.

Dialogs use the same binder. Their saved bounds are not maximized and do not go through the legacy converter. The prompt dialog's position is not saved.

## 15. Native PNG encoder

`snapenc.dll` writes the fast-path PNG. It is optional at build time and at run time. Without it, the default screenshot still succeeds through the managed slow path and still reports `windowVisual` or `windowDc`. The bytes are then a GDI+ PNG rather than a stored-deflate RGB PNG.

### Build

The project target `BuildSnapEncoder` runs before build, incremental on `Native/snap.cpp`, `Native/fpng.cpp`, `Native/fpng.h`, and `Native/build-snapenc.cmd`. The output is `Native/win-x64/snapenc.dll`.

`build-snapenc.cmd` finds Visual C++ with vswhere. The vswhere path is written to a file and read back, because `Program Files (x86)` breaks a `for /f in (\`...\`)` loop. It then calls `vcvars64.bat` and ignores that batch's exit code, because a successful environment setup can still return 1. The compile runs in a local temp directory. The sandbox maps the repository through a share, and the linker export file written on that share comes back corrupt. The script copies `snapenc.dll` to `Native/win-x64` when `cl` succeeds. Then:

```text
cl /nologo /O2 /LD /EHsc /DNDEBUG /DFPNG_NO_STDIO /std:c++17 snap.cpp fpng.cpp /Fesnapenc.dll
```

When vswhere or the VC tools are missing, the script prints a `snapenc:` line and exits 0. The managed build succeeds with no DLL. When the toolset is present and `cl` fails, the script exits with that error and the managed build fails. `CopySnapEncoder` copies the DLL to the build output after build when the file exists. `CopySnapEncoderPublish` copies it to the publish directory. The installer script requires the DLL to be present.

### Exports

Cdecl exports: `snap_init`, `snap_begin(bgra, width, height, stride, dest, capacity, SnapState*)`, `snap_rows(state, y, count)`, `snap_end(state)`, `snap_stored_into(...)`. `snap_rows` requires rows in order. fpng supplies CRC-32 and Adler-32. `fpng_init` runs once. An SSSE3 shuffle converts BGRA to RGB when the CPU has SSSE3.

### PNG layout

The image is 8-bit RGB (IHDR color type 2). Alpha is dropped. One IDAT holds zlib stored blocks, one block per row, header `78 01`, filter byte 0. A row wider than 65,535 bytes is rejected, which caps the width at 21,844 pixels. The byte count is `57 + 2 + (3 * width + 1) * height + 5 * height + 4`.

### Loading

`NativeStoredPng` calls `NativeLibrary.TryLoad` on `AppContext.BaseDirectory\snapenc.dll` once. A missing file or a missing export disables the fast path with no dialog. Encode buffers come from one pre-warmed array of the PNG length. After each encode, a thread-pool item allocates and page-touches the next buffer so the following capture does not pay for the first touch.

## 16. Project layout and tests

```text
src/mcp_overlord/                 WPF application
  App.xaml, MainWindow.xaml
  AppComposition.cs               hand-wired object graph
  Configuration/                  loader, draft, store, schema models
  Diagnostics/                    connection check
  Hosting/                        Kestrel host, certificate, guards, tool log
  Logging/                        buffer, stats, formatters, HTTP exchange
  Native/                         snap.cpp, fpng, build-snapenc.cmd
  Resources/Styles.xaml           dark theme
  Resources/Icons/                SVG icons and app.ico
  Resources/config.schema.json    embedded schema
  Services/                       locator, capture, input, launch
  Services/Native/                Win32, Windows Graphics Capture, fast PNG
  Shell/                          AppUserModelID, jump list, recent files
  Startup/                        command line
  Tools/                          the seventeen MCP tools
  UI/                             view models, dialogs, help, placement
src/automation_util/              input, search, screenshots, image match
tests/mcp_overlord.tests/         host, config, UI, services
tests/automation_util.tests/      encoders, gestures, search
installer/mcp_overlord/           WiX 5 package
dev/build-installer.ps1           publish plus MSI
doc/mcp_overlord_design.md        this document
```

`global.json` selects `Microsoft.Testing.Platform`. Desktop tests that move the real cursor, capture a real window, or restore a window across monitors run only when the environment variable `GAME_MCP_DESKTOP_TESTS` is `1`. The capture fast path is covered by a pixel round-trip that skips itself when `snapenc.dll` is absent, and by a desktop test that expects `windowVisual` and a warm full-window PNG.

The test projects cover configuration and schema errors, the editor draft and Save As rebasing, the certificate create/reuse rules, HTTP 401 and 403, the tool list, log payload formatting, jump-list tasks, window placement across two DPI scales, the locator tie-break and cache, coordinate mapping, integrity, the lock decision, and the editor dialog on a real desktop.

Before a build or a test run, `kill-debug.ps1` stops a running debug copy of the application so the output files are not locked. A successful build is one that compiles with the warnings resolved as far as the change allows.

## 17. Client setup

### Cursor

Cursor talks to the streamable HTTP endpoint. With the default config, no API key, loopback:

```json
{
  "mcpServers": {
    "game-control": {
      "url": "http://127.0.0.1:5000/mcp"
    }
  }
}
```

With an API key:

```json
{
  "mcpServers": {
    "game-control": {
      "url": "https://127.0.0.1:5001/mcp",
      "headers": {
        "Authorization": "Bearer replace-with-a-long-random-string"
      }
    }
  }
}
```

`X-API-Key` is also accepted. A local HTTPS endpoint that uses a self-signed certificate fails TLS verification in the client until the certificate is trusted on the machine. For same-machine Cursor, plain HTTP on loopback is the straightforward setup. HTTPS matters when the listener is reachable beyond the machine. The generated public certificate is `server.cer` next to `server.pfx` under `%LOCALAPPDATA%\mcp_overlord`.

### Grok bot

The bot needs a URL it can open from its own network.

- Preferred: HTTPS listener, `apiKey` set, bind address on an interface the bot can route to, and `certificate.hostNames` containing the name the bot uses. The bot trusts `server.cer` (or a supplied certificate) and uses that `https://` URL with the same `Authorization` header. A self-signed certificate encrypts the connection only after the bot trusts that exact certificate. Until then, TLS does not protect the API key from interception.
- Alternative: keep `bindAddress` at `127.0.0.1` and run a tunnel or reverse proxy that exposes HTTPS publicly and forwards to `http://127.0.0.1:5000`. Set `apiKey` even in this setup. The bot uses the public HTTPS URL.

The app does not start a tunnel and does not open a firewall port. The log, the copied URL, and the connection check are how the operator confirms what to paste into the bot.

### First session the model should run

The server instructions already say this. A working session looks like:

1. `list_applications`
2. `get_application_description` for the entry the operator cares about, when `hasDescription` is true
3. `launch_application` if `running` is false and `launchable` is true
4. `take_screenshot`
5. `mouse_click` / `key_press` using pixels from that image, or `window_rect` pixels when the caller already knows the window size

`is_locked` tells the model whether input will be refused. `lock` is how the model locks the screen. There is no unlock tool.

## 18. Known limitations

These are behaviors of this build. A recreation that aims to match the binary keeps them. A later change can remove them without renaming a tool.

- Arrow keys and the navigation keys (`Insert`, `Delete`, `Home`, `End`, `PageUp`, `PageDown`, and the arrows) are sent with scan code 0 and without `KEYEVENTF_EXTENDEDKEY`. Some games read those as numpad keys or ignore them.
- `key_down` does not record the key. Stop and `lock` release mouse buttons this server still considers down. They do not send key-ups for keys the model left down.
- `mouse_click` does not clear a held-button record for the same button. A later `ReleaseHeldButtons` sends an extra button-up.
- `InputGate` lock objects are never removed.
- `ProcessNameLookup` caches a process name for a process id forever. A reused process id can report the previous image name until the process exits and the lookup is still warm.
- The Windows Graphics Capture session is not disposed when the process exits. It is rebuilt when the window or the size changes, and closed when the capture item closes.
- The slow-path `windowDc` copy places `BitBlt` pixels in a 32-bit ARGB bitmap. A GDI blit often leaves the alpha byte at 0, so the PNG encoded from that bitmap can be fully transparent. The fast path drops alpha and writes opaque RGB. This alpha result has not been confirmed by a runtime capture in this document's review.
- HTTP bodies are captured with no size limit and no secret scrub. A large screenshot or a body that contains the API key stays in the log detail until the line scrolls out of retention.
- `PromptDialog` does not call `DarkTitleBar.Apply`, so the save prompt, message boxes, and the fatal-schema dialog keep the system title bar.
- The combo-box chevron is an inline geometry in `Styles.xaml` rather than an SVG resource.
- A cache hit reports the match count from the search that filled the cache, and it keeps that window even when a better candidate appears later.
- The "process is running and has no window" message still suggests `launch_application` for a launchable entry whose process is already alive.

## 19. Future work

These are deliberately deferred. They should not change the tool names in section 6. New behavior can arrive as new tools or as a new `schemaVersion` with a migrator.

| Topic | Notes |
| --- | --- |
| Image-match tools | `AutomationUtil.find_matches`, `find_all_matches`, and `activate_menu` already search a window for a template bitmap. A later tool could accept a template name from a folder listed on the application entry and return rectangles in the same coordinate space as `take_screenshot`. This build leaves recognition to the model looking at the screenshot. |
| Macros | A recorded sequence of the existing tools, replayed with delays. The event shape in the log is close to a recording. Playback is a separate feature. |
| Several monitors | Capture and `ClientToScreen` already use the virtual screen, so a window on a secondary monitor works. What is not specified is a tool that lists monitors or moves a window between them. `Window.setRect` exists in the library and is not exposed. |
| Exclusive fullscreen | Windows Graphics Capture is the main path and removes occluders. A protected or exclusive surface can still come back black, and the tool returns that frame with the warning in section 6.5. A further capture path would keep the same result shape. |
| DirectInput-only games | Some games ignore both Win32 backends. `DirectInputClick` in `automation_util` is a stub. A working version becomes another `ClickHelper` implementation and another `clickBackend` value, after a separate security review. This build does not register it. |
| File log and log export | Subscribe a writer to `LogEntry`. Add a schema field only when the feature exists. |
| More than one window per application | This build picks one candidate and reports `matchCount`. A later `find_window` could return the list, and a selector argument could disambiguate. |
| Schema migration | A file written as version N would be migrated forward by pure functions `(document N) -> document N+1`, then validated. Migration would run in memory. The app would rewrite the file only when the user saves. Version 1 has no migrator. |

## Appendix — behavior checklist

The implementation matches this specification when all of the following are true.

- A valid `--config` path, or one positional configuration path, starts the listener without a further click, and an invalid file opens the window, logs the errors, and leaves the listener down.
- `--help` prints the usage text and exits. `--register-shell` publishes the jump list and exits. `--elevate` starts an elevated instance and, when the prompt is cancelled, continues in the same process.
- The Load, New, Save, and Save as buttons follow section 3.6. An application-list change applies without a restart. Listener changes apply on the next start. `logRetention` applies on OK and does not restart the listener.
- `schemaVersion` other than 1 is rejected with the supported version named in the message.
- The embedded schema is the one in section 3.2, including `logRetention`.
- `list_applications`, `get_application_description`, and `launch_application` follow section 6. Launch starts only the configured executable and reports the child process id.
- `take_screenshot` returns an MCP image of the whole window plus scale metadata. The default PNG uses Windows Graphics Capture, and uses `snapenc.dll` when that DLL is next to the executable. A following `mouse_click` in image coordinates lands on that pixel of the snapshot. `window_rect` ignores scale. A point outside the window sends nothing.
- Key up is a key up. `MouseEventClickHelper` and `SendInputClickHelper` both implement move, left/right/middle down and up, and wheel. `clickBackend` selects which one a mouse tool uses, and the default is `mouseEvent`.
- The window is dark, including the title bar on a current Windows 11 build. The layout is the toolbar, the applications column, the log, and the Detail, Stats, and Help tabs in section 9. Placement, splitters, log columns, and dialog bounds restore from `ui_settings.json`.
- A non-loopback bind without HTTPS or an API key does not start. With both, HTTP listens on `127.0.0.1` and HTTPS listens on the bind address.
- With an API key set, a request without the header receives 401. A foreign `Origin` receives 403 when the key check passed.
- `is_locked` reports the workstation lock. `lock` locks the screen and there is no unlock tool. While locked, mouse and keyboard tools return an error and send nothing.
- A tool that runs longer than 15 seconds returns `The tool did not finish within 15 seconds.` and the log records a warning.
- Check connection reports PASS, WARN, and FAIL for each public endpoint, including TLS and an MCP `initialize` at protocol `2025-11-25`.
- The installer registers the `.overlord` extension with `--config "%1"`, the Start menu shortcut, the AppUserModelID, the product-code registry value, and a post-install `--register-shell`.
