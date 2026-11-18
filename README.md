# MCP Overlord

Give your AI hands. Supervise accordingly.

Project site: https://azus.github.io/mcp_overlord

MCP Overlord is a Windows desktop app that hosts an [MCP](https://modelcontextprotocol.io) server. A client such as Cursor, or a Grok bot, can see a window you named in a config file, take a screenshot of it, and send mouse and keyboard input. The usual target is a game. The model decides what to do. This program carries it out, then reports what happened.

```mermaid
flowchart LR
  client[Cursor or other MCP client] --> server[MCP Overlord]
  server --> window[Configured window]
```

## What it does

- Takes a screenshot of a configured window and hands the image back to the model. Later clicks use pixels of that image, with the origin at the top left of the window.
- Moves the mouse, clicks, scrolls, presses keys, and types text into that window. The window is focused first.
- Finds the window from its title, process name, or executable, and can launch the executable listed in the config.
- Shows a small dark window with start and stop, a live log, and counters, so you can watch the session instead of guessing.
- Speaks MCP over HTTP on loopback by default. HTTPS and an API key are there when the client is not on this machine.

The tools are `list_applications`, `get_application_description`, `launch_application`, `find_window`, `focus_window`, `take_screenshot`, `mouse_move`, `mouse_click`, `mouse_down`, `mouse_up`, `mouse_scroll`, `key_press`, `key_down`, `key_up`, `type_text`, `is_locked`, and `lock`.

The behavior contract, including every argument, is [doc/mcp_overlord_design.md](doc/mcp_overlord_design.md).

## What it does not do

It will not play the game for you. Something else might. This program only moves the mouse where it is told.

- It does not choose the next click. The model looks at the screenshot and decides. Recognition, for this version, is whatever that model can see.
- It does not search the screen for a template image. That code sits in the library and is not exposed as a tool yet.
- It does not drive a window that is missing from the configuration, and launch paths come only from that file. A client cannot pass a different executable.
- It does not record or replay macros.
- It runs on 64-bit Windows. Other platforms are out of scope.
- It does not send input into a process running at a higher integrity level. A game started as administrator needs MCP Overlord started the same way. Switching the click backend does not cross that boundary.
- It can lock the workstation. It cannot unlock it. Someone at the keyboard has to do that.

## Built by prompting

This project was vibe coded. Most of the implementation came from prompting an AI coding assistant, working from a written design, with tests beside it and a human reading the result. Read the parts you care about before you point it at a save file you would miss.

## Install

The MSI for 64-bit Windows 10 or 11 is [mcp_overlord-1.0.0-win-x64.msi](https://github.com/azus/mcp_overlord/releases/download/release_1/mcp_overlord-1.0.0-win-x64.msi), attached to the [release_1](https://github.com/azus/mcp_overlord/releases/tag/release_1) release.

The package carries the runtime, so the machine does not need the .NET SDK. The installer adds a Start menu shortcut named MCP Overlord and registers the `.overlord` extension.

Glama badge, once the listing exists: `[![azus/mcp_overlord MCP server](https://glama.ai/mcp/servers/azus/mcp_overlord/badges/score.svg)](https://glama.ai/mcp/servers/azus/mcp_overlord)`

## Requirements

A 64-bit edition of Windows 10 or 11.

Building from source also needs:

- the .NET SDK 9.0 (x64), including the Windows Desktop and ASP.NET Core targeting packs
- the Visual Studio 2022 Visual C++ x64 toolset
- Windows SDK 10.0.22621.0 or newer
- the WiX Toolset v5.0.2 command line

The C++ toolset is there because screenshot encoding lives in a small native library, `snapenc.dll`. WiX builds the installer. `install_requirements.ps1` installs the official signed WiX v5.0.2 MSI into Program Files, so Windows trusts `wix.exe`.

From the repository root:

```powershell
.\dev\install_requirements.ps1
```

The script checks what is already present and installs only what is missing. When an install is required, it asks for administrator permission through User Account Control. The Windows SDK, Visual C++ build tools, and WiX install with no window, and their progress is printed as steps. A step log is written to `%TEMP%\install_requirements.log`.

## Build the installer

You might need this to execute powershell scripts:
```powershell
Set-ExecutionPolicy -ExecutionPolicy RemoteSigned -Scope CurrentUser
```

Start the build:
```powershell
.\dev\build-installer.ps1
```

The script installs any missing build tools, stops a running copy of the app, publishes a self-contained `win-x64` build, and writes an MSI:

```text
artifacts\installer\mcp_overlord-<version>-win-x64.msi
```

The version defaults to the `<Version>` in [src/mcp_overlord/mcp_overlord.csproj](src/mcp_overlord/mcp_overlord.csproj). The MSI carries the runtime, so the machine you install it on does not need the .NET SDK. The installer adds a Start menu shortcut named MCP Overlord and registers the `.overlord` extension.

Optional arguments:

```powershell
.\dev\build-installer.ps1 -Version 1.2.0
.\dev\build-installer.ps1 -Configuration Release
.\dev\build-installer.ps1 -NoPublish
```

`-Version` is `major.minor.patch`. `-Configuration` defaults to `Release`. `-NoPublish` reuses `artifacts\publish\mcp_overlord` instead of publishing again.

## Run a debug build

Pass a config file. The script builds the Debug configuration, starts the app with `--config`, waits until the HTTP port accepts a connection, and prints the MCP URL.

```powershell
.\dev\start-mcp-overlord.ps1 -ConfigPath C:\path\to\your.overlord
```

Skip the build when the binaries are already current:

```powershell
.\dev\start-mcp-overlord.ps1 -ConfigPath C:\path\to\your.overlord -NoBuild
```

`-Configuration` defaults to `Debug`. The script has a built-in sample path, and that sample is not in this repository, so pass `-ConfigPath`.

Ask the window to close, and force it if it is still there after a few seconds:

```powershell
.\dev\stop-mcp-overlord.ps1
```

`.\dev\kill-debug.ps1` stops the process immediately. The start script and the installer build both call it first. If the process is running elevated, run that script from an elevated PowerShell.

From an IDE, open [dev/game_automation_in_csharp.sln](dev/game_automation_in_csharp.sln), set `mcp_overlord` as the startup project, and pass `--config C:\path\to\your.overlord` as the debug arguments.

Run the tests with:

```powershell
dotnet test dev\game_automation_in_csharp.sln
```

The test projects use Microsoft.Testing.Platform. [global.json](global.json) selects that runner for `dotnet test`.

## Configuration

Configuration is JSON, conventionally saved as `something.overlord`. On load it is checked against the schema embedded in the app, [src/mcp_overlord/Resources/config.schema.json](src/mcp_overlord/Resources/config.schema.json). A file that fails that check leaves the server stopped. The window stays open so the log can say why.

A local setup, loopback, no API key:

```json
{
  "schemaVersion": 1,
  "server": {
    "httpPort": 5000,
    "bindAddress": "127.0.0.1"
  },
  "applications": [
    {
      "name": "Example Game",
      "executable": "C:\\Games\\Example\\Example.exe",
      "windowTitle": "Example",
      "processName": "Example",
      "description": "Launch the game, then click Play."
    }
  ]
}
```

Each application needs `name`, `executable`, and `windowTitle`. Optional fields are `processName`, `parameters`, `workingDirectory`, `description`, and `clickBackend` (`mouseEvent` or `sendInput`). The `server` object is optional. Left out, the server listens on `127.0.0.1` port 5000.

When `apiKey` is set it must be at least 16 characters. Clients then send `Authorization: Bearer <apiKey>` or `X-API-Key: <apiKey>`.

| Argument | Meaning |
| --- | --- |
| `--config <path>` | Load this file and start the server once the window can show logs. |
| `file.overlord` | Same as `--config`. Explorer passes the file this way. |
| `--elevate` | Start an elevated copy with the same config. Cancelling the prompt keeps this process. |
| `--help`, `-h` | Print usage and exit before the window opens. |

The window can also load a file after startup. The dialog prefers `*.overlord`.

## Connect a client

Cursor on the same machine, with the config above and no API key:

```json
{
  "mcpServers": {
    "game-control": {
      "url": "http://127.0.0.1:5000/mcp"
    }
  }
}
```

With an API key, add the header. Generate the key yourself.

```json
{
  "mcpServers": {
    "game-control": {
      "url": "http://127.0.0.1:5000/mcp",
      "headers": {
        "Authorization": "Bearer replace-with-a-long-random-string"
      }
    }
  }
}
```

A client on another machine wants HTTPS. Set `enableHttps` and `apiKey`, and a `bindAddress` that machine can reach. Omit `certificate.path` and the app creates or reuses a self-signed pair under `%LOCALAPPDATA%\mcp_overlord`, then logs the path and thumbprint. The remote client has to trust `server.cer`. Until it does, TLS does not protect the API key. The other supported path is to leave the bind on loopback and put your own tunnel in front of the HTTP port. This app does not start a tunnel.

For same-machine Cursor, plain HTTP on loopback is the setup that avoids fighting a self-signed certificate.

A working first session looks like this: `list_applications`, then `get_application_description` when the list says a description exists, `launch_application` when the app is launchable and not running, `take_screenshot`, then `mouse_click` or `key_press` using pixels from that image.

## Security

This program clicks and types into other programs, and it can start the executables you listed. Treat the endpoint as a keyboard that happens to speak HTTP.

- The default bind address is `127.0.0.1`. Other machines cannot reach it.
- A bind address that is not loopback is refused unless HTTPS is enabled and an API key is set. The server will not open an unauthenticated network listener.
- A request that carries an `Origin` header is accepted only when that origin is one of the server's own endpoints. Cursor and other native clients omit the header.
- `launch_application` starts the executable stored on that config entry, with the arguments stored beside it.
- `lock` locks the screen. There is no unlock tool. While the screen is locked, mouse and keyboard tools fail and explain why.
- Input into a higher-integrity process is refused. The tool error asks you to run MCP Overlord at the same integrity level as the target.

Section 8 of the design document is the full version of these rules.

## Repository layout

```text
src/mcp_overlord/          WPF app and MCP server
src/mcp_overlord/Native/   snap.cpp and fpng.cpp, built as snapenc.dll
src/automation_util/       window, screenshot, mouse, and keyboard library
tests/                     unit tests
installer/mcp_overlord/    WiX project for the MSI
dev/                       requirement, installer, and debug scripts
doc/mcp_overlord_design.md behavior contract
doc/publishing.md          text for directory submissions
docs/                      project site published with GitHub Pages
```

The solution is [dev/game_automation_in_csharp.sln](dev/game_automation_in_csharp.sln).

## Known limits

Left for a later version, on purpose:

- A tool that searches a window for a template image. The library can already do the search. The server does not offer it.
- Recorded macros.
- A capture path aimed at exclusive fullscreen. The current fallback covers the common case and reports a black frame when it cannot see the window.
- Games that read input only through DirectInput. Both Win32 backends can be ignored by those titles.

A window on a second monitor already works. Every connected client shares one desktop session: one process, one set of hands.

## License

Released under the [MIT License](LICENSE).
