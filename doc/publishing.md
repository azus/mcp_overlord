# Publishing MCP Overlord

Text to paste when listing this repo. Commit and push `glama.json`, this file, and the README install section first, so the sites read the release link and the Glama maintainer file from `main`.

The official MCP Registry and Smithery are omitted. The registry accepts an npm, PyPI, NuGet, Cargo, OCI, or `.mcpb` package. Smithery wants a public HTTP endpoint or a stdio server. MCP Overlord is a Windows desktop app that serves MCP over HTTP on loopback (`http://127.0.0.1:5000/mcp` by default). There is no `server.json`, no `.mcpb`, and no public URL.

Checked 18 Nov 2026: the repo has 0 GitHub stars, and the only release is [release_1](https://github.com/azus/mcp_overlord/releases/tag/release_1).

## Shared text

Use these on every form. A site-specific section below only adds fields that differ.

**Name:** MCP Overlord

**Website:** https://azus.github.io/mcp_overlord

**Repository:** https://github.com/azus/mcp_overlord

**Release page:** https://github.com/azus/mcp_overlord/releases/tag/release_1

**MSI:** https://github.com/azus/mcp_overlord/releases/download/release_1/mcp_overlord-1.0.0-win-x64.msi

**Tags:** windows, automation, screenshot, mouse, keyboard, gaming, desktop

**Short description** (94 characters):

```text
Windows desktop MCP server: screenshots plus mouse and keyboard input into configured windows.
```

**Long description:**

```text
MCP Overlord is a Windows desktop app that hosts an MCP server. A client such as Cursor can see a window named in a config file, take a screenshot of it, and send mouse and keyboard input. The usual target is a game. The model decides what to do. This program carries it out, then reports what happened.

It speaks MCP over HTTP on loopback, at http://127.0.0.1:5000/mcp with the default config. It is a desktop app with an installer, not a stdio package and not a public endpoint. Install the MSI, load a .overlord file, start the server from the window, and point the client at that URL.

Tools: list_applications, get_application_description, launch_application, find_window, focus_window, take_screenshot, mouse_move, mouse_click, mouse_down, mouse_up, mouse_scroll, key_press, key_down, key_up, type_text, is_locked, and lock.

64-bit Windows 10 or 11. MIT license.
```

**Cursor config** (same machine, no API key). This is the block in the README under "Connect a client":

```json
{
  "mcpServers": {
    "game-control": {
      "url": "http://127.0.0.1:5000/mcp"
    }
  }
}
```

The README has a second block for when `apiKey` is set. Paste that one only if the listing asks for an authenticated example.

## 1. Glama

Open https://glama.ai/mcp/servers and choose Add server (the page also labels this Add MCP Server).

| Field | Value |
| --- | --- |
| GitHub repository | https://github.com/azus/mcp_overlord |
| Display name | MCP Overlord |
| Short description | the short description above |

`glama.json` at the repo root lists `azus` as a maintainer. After the push is on `main`, run the Claim ownership flow again so Glama reads that file. The repo is under the personal account `azus`, so signing in with GitHub can claim it as well. The file is what to point at if the claim asks for a maintainer list.

Leave the Docker image unset. This repo has no Dockerfile. The server is a Windows desktop app and does not start under a Linux sandbox. A failed sandbox build can leave a profile that stays out of search. Claiming the listing still lets you edit the name and description.

When the listing page exists, replace the placeholder line in the README Install section with:

```markdown
[![azus/mcp_overlord MCP server](https://glama.ai/mcp/servers/azus/mcp_overlord/badges/score.svg)](https://glama.ai/mcp/servers/azus/mcp_overlord)
```

## 2. awesome-mcp-servers

Open https://github.com/punkpeye/awesome-mcp-servers/blob/main/README.md#os-automation

Fork the repo, branch, and open a pull request that adds one line to `README.md`. Their contributing notes ask for one server per line, in alphabetical order inside the category.

Use **OS Automation** (`### 🖥️ OS Automation`). That section is screenshots, window management, and mouse and keyboard input. Gaming is game engines and game data, so this server does not go there.

Insert this line between the `AmrDab/clawdcursor` bullet and the `protocorn/clippy-vision` bullet. Those two are adjacent at the end of the section. Case-insensitive order of the `owner/repo` link text puts `azus/mcp_overlord` between them. The rest of the section is already unsorted. Do not reorder it.

```markdown
- [azus/mcp_overlord](https://github.com/azus/mcp_overlord) #️⃣ 🏠 🪟 - Windows desktop MCP server: screenshots plus mouse and keyboard input into configured windows.
```

`#️⃣` is C#, `🏠` is a local service, `🪟` is Windows. That matches the legend at the top of their README.

Suggested PR title: `Add azus/mcp_overlord to OS Automation`

PR body:

```text
Adds MCP Overlord under OS Automation.

Windows desktop app that hosts an MCP server on loopback. A client screenshots a configured window and sends mouse and keyboard input. C#, local, Windows. MIT.
https://github.com/azus/mcp_overlord
```

## 3. mcp.so

Open https://mcp.so/submit

| Field | Value |
| --- | --- |
| Repository URL | https://github.com/azus/mcp_overlord |
| Name | MCP Overlord |

The form also offers a one-time paid listing. The free fields above are enough to submit. If a description box is present, paste the short description, then the long description if a second box is present. Paste the Cursor config if a setup box is present.

## 4. mcpservers.org

Open https://mcpservers.org/submit

| Field | Value |
| --- | --- |
| Server Name | MCP Overlord |
| Category | Other |
| Short Description | the short description above |
| Repository, Website or Documentation | https://github.com/azus/mcp_overlord |
| Official MCP Registry Name | leave empty |
| This server supports remote connections | leave unchecked |
| Contact Email | your email |

Remote stays off because the server listens on loopback. The registry name stays empty because this app is not in the official registry. A paid review option is on the same form. The free submission is the one this text is for.

## 5. MCP Market

Open https://mcpmarket.com/submit

Paste the repository URL and submit:

```text
https://github.com/azus/mcp_overlord
```

The form asks for that URL and reviews the repo. There is no description field.

## 6. cursor.directory

Open https://cursor.directory/mcp/new

That page is "Submit a Plugin" and can auto-detect from a GitHub repo. Paste:

```text
https://github.com/azus/mcp_overlord
```

If it asks you to fill the listing by hand, use the name, the short description, the tags, and the Cursor config from the shared text. The install command is the MSI link, not an `npx` command.

## 7. LobeHub

Open https://lobehub.com/mcp and use Submit MCP. Paste the repository URL, the name, the short description, and the tags.

You own the repo, so the other path is the market CLI. It needs Node.js 22 or newer, and `login` and `github connect` each open a browser. Do not set `cloudEndpoint`. This server has no public URL. Do not run `plugin init --url` against a host, and do not use `plugin init --stdio`. Both assume a server the CLI can start or reach. Write the manifest yourself:

```json
{
  "author": "azus",
  "authorUrl": "https://github.com/azus",
  "category": "tools",
  "description": "Windows desktop MCP server: screenshots plus mouse and keyboard input into configured windows.",
  "homepage": "https://azus.github.io/mcp_overlord",
  "identifier": "azus-mcp-overlord",
  "name": "MCP Overlord",
  "tags": ["windows", "automation", "screenshot", "mouse", "keyboard", "gaming", "desktop"],
  "version": "1.0.0"
}
```

Save that as `lhm.plugin.json` in a working copy only if you are about to publish. The identifier must stay `azus-mcp-overlord` on later updates. `category` is `tools` because the publish docs list that among the examples. If the CLI rejects it, pick the marketplace category closest to desktop automation and resubmit.

From the repo root, after the browser login:

```powershell
npx -y @lobehub/market-cli auth status --output json
npx -y @lobehub/market-cli login
npx -y @lobehub/market-cli github connect
npx -y @lobehub/market-cli plugin publish https://github.com/azus/mcp_overlord --dir .
```

Publish reads `README.md` from `main` in the background, so push the README before this command. Their publish notes are at https://market.lobehub.com/s/publish-mcp

## 8. PulseMCP

Open https://www.pulsemcp.com/submit (the plan link https://pulsemcp.com/submit lands on the same form).

Prepare the same name, repository URL, short description, long description, tags, and MSI link as the other forms.

Two blocks, as of 18 Nov 2026:

- The form page says submissions and listing changes are paused. The note on the page was last updated 3 Sep 2026. It tells people to publish to the official MCP Registry instead. This app is staying out of that registry, so there is nothing for PulseMCP to pick up automatically while the form is closed.
- When the form is open again, the manual submission asks for at least 100 GitHub stars or a registry entry. This repo has 0 stars. Expect a rejection until the star count is there. The text above is ready for that submission.

Skip this form until both the pause is lifted and the repo has 100 stars.
