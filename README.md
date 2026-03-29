## Mcp.Obsidian

![Build Status](https://img.shields.io/badge/build-passing-brightgreen)
![.NET Version](https://img.shields.io/badge/.NET-10.0-purple)
![License](https://img.shields.io/badge/license-MIT-blue)
![MCP Standard](https://img.shields.io/badge/MCP-1.0-orange)

**Mcp.Obsidian** is a .NET 10 Model Context Protocol (MCP) server that bridges AI agents (Claude, Gemini, etc.) with your local Obsidian vault.

It allows LLMs to read, search, and edit your notes safely using the [Obsidian Local REST API](https://github.com/coddingtonbear/obsidian-local-rest-api), keeping your knowledge base local-first while enabling AI automation.

This server is designed to feel much closer to a real Obsidian operator than a thin note CRUD wrapper. It can work with normal vault files, the active note, periodic notes, command execution, vault queries, backlink-aware analysis, and workspace scaffolding.

## What Makes It Different

- It is not limited to file CRUD. It can reason about headings, links, backlinks, periodic notes, and Obsidian commands.
- It exposes higher-level agent workflows like smart append, daily note automation, and workspace scaffolding.
- It stays local-first by talking only to your running Obsidian instance through the Local REST API plugin.
- It is built for any MCP-compatible client that can launch a local process over stdio.

### Why this is useful

- Keep your notes local while still letting MCP-compatible clients work with them.
- Give Claude Desktop, Cursor, Gemini, and other MCP tools structured access to your Obsidian vault.
- Avoid brittle copy-paste workflows by letting agents search, read, create, and update notes directly.
- Build on top of the widely used Obsidian Local REST API instead of inventing a custom plugin.
- Cover higher-level workflows like daily notes, heading-aware appends, command execution, and workspace bootstrapping.

> [!NOTE]
> A demo image is not included in this repository yet, so the placeholder caption has been removed for now.
> Once a real recording exists, add it back as something like `![Demo](docs/demo.gif)`.

---

## 🏗 Architecture

```mermaid
graph LR
    A[User] -->|Prompts| B[AI Client<br>Claude Desktop / Gemini]
    B <-->|MCP Protocol| C[Mcp.Obsidian Server<br>.NET 10]
    C <-->|HTTPS / Localhost| D[Obsidian Plugin<br>Local REST API]
    D -->|Read/Write| E[(Obsidian Vault)]

```

---

## 🚀 Getting Started

### 1. Prerequisites

You will need:

- **Obsidian Desktop**
- **.NET 10 SDK**
- **Obsidian Local REST API** community plugin by `coddingtonbear`

Install and enable the Obsidian plugin:

1. Open Obsidian.
2. Go to `Settings` → `Community plugins`.
3. Install and enable `Local REST API`.
4. Open the plugin settings.
5. Copy the API key.

### 2. Clone and Configure

Clone the repository and create your local config:

```bash
git clone https://github.com/yourusername/Mcp.Obsidian.git
cd Mcp.Obsidian
cp appsettings.json.example appsettings.json
```

Update `appsettings.json` in the repo root with your Obsidian connection details:

```json
{
  "Obsidian": {
    "BaseUrl": "https://127.0.0.1:27124",
    "ApiKey": "YOUR_OBSIDIAN_API_KEY_HERE",
    "VerifySsl": false
  }
}

```

You can also configure the server via environment variables:

```bash
export OBSIDIAN__BASEURL=https://127.0.0.1:27124
export OBSIDIAN__APIKEY=YOUR_OBSIDIAN_API_KEY_HERE
export OBSIDIAN__VERIFYSSL=false
```

### 3. Build

Build the MCP server:

```bash
dotnet build src/Mcp.Obsidian/Mcp.Obsidian.csproj
```

Publish a standalone folder for any MCP-compatible client:

```bash
dotnet publish src/Mcp.Obsidian/Mcp.Obsidian.csproj -c Release -o ./artifacts/obsidian-mcp
```

### 4. Connect It To Your MCP Client

The server uses the MCP stdio transport, so any MCP-compatible tool that can launch a local command can use it.

#### Claude Desktop

Add this to your `claude_desktop_config.json`:

```json
{
  "mcpServers": {
    "obsidian": {
      "command": "dotnet",
      "args": [
        "run",
        "--project",
        "/absolute/path/to/Mcp.Obsidian/src/Mcp.Obsidian/Mcp.Obsidian.csproj",
        "--",
        "--config=/absolute/path/to/Mcp.Obsidian/appsettings.json"
      ]
    }
  }
}

```

#### Generic MCP Client

If your MCP client prefers a built executable, point it to the published binary instead:

```json
{
  "mcpServers": {
    "obsidian": {
      "command": "/absolute/path/to/Mcp.Obsidian/artifacts/obsidian-mcp/Mcp.Obsidian",
      "args": []
    }
  }
}
```

### 5. Quick Start Checklist

1. Start Obsidian and make sure the `Local REST API` plugin is enabled.
2. Confirm your API key is present in `appsettings.json` or environment variables.
3. Run `dotnet build src/Mcp.Obsidian/Mcp.Obsidian.csproj`.
4. Add the MCP server entry to your client config.
5. Restart your MCP client.
6. Ask the client to call `obsidian_list_files` or `obsidian_search` as a smoke test.

### 6. First Prompts To Try

- "List the top-level files and folders in my vault."
- "Create `Inbox/HN Launch.md` with a launch checklist."
- "Append today’s status update to my daily note."
- "Show me which notes already mention or link to `HackerNews Launch`."
- "Create a new client workspace called `Acme` with folders and index notes."

---

## ✨ Feature Set

This MCP server now covers a much larger slice of what a human can do manually in Obsidian:

- Work across three note scopes: vault files, the active note, and periodic notes.
- Read notes as markdown, parsed note JSON, or a document map for structural reasoning.
- Create, replace, append, patch, and delete notes instead of stopping at read-only automation.
- Append under a specific heading path with `obsidian_smart_append`.
- Patch headings, blocks, or frontmatter fields with fine-grained target operations.
- Run simple text search plus richer Dataview DQL and JsonLogic vault queries.
- Inspect outgoing links, backlinks, and plain-text mention opportunities.
- Open notes in the desktop UI and execute Obsidian commands from MCP.
- Scaffold workspace folder structures with index notes for new projects or clients.

## 🛠 Available Tools

| Tool | Description |
| --- | --- |
| `obsidian_list_files` | Lists files in a folder or at the vault root. |
| `obsidian_read_resource` | Reads vault, active, or periodic notes as markdown, note JSON, or document map. |
| `obsidian_write_resource` | Creates or replaces vault, active, or periodic notes. |
| `obsidian_append_resource` | Appends markdown to vault, active, or periodic notes. |
| `obsidian_delete_resource` | Deletes vault, active, or periodic notes. |
| `obsidian_patch_target` | Patches headings, blocks, or frontmatter with append, prepend, or replace. |
| `obsidian_daily_note` | Shorthand helper for current or dated daily note workflows. |
| `obsidian_read_note` | Retrieves the full markdown content of a specific note. |
| `obsidian_create_note` | Creates a new note with specified path and content. |
| `obsidian_append` | Appends text to the end of an existing note. |
| `obsidian_patch_frontmatter` | Updates or adds YAML frontmatter tags and metadata. |
| `obsidian_search` | Backward-compatible alias for simple vault text search. |
| `obsidian_search_simple` | Runs text search through the vault. |
| `obsidian_query_vault` | Runs Dataview DQL or JsonLogic queries through the vault. |
| `obsidian_extract_links` | Extracts wikilinks, embeds, and markdown links from a note. |
| `obsidian_backlink_report` | Reports outgoing links, backlinks, and mention candidates. |
| `obsidian_smart_append` | Appends under a matching heading path or creates it first. |
| `obsidian_open_note` | Opens a note in Obsidian. |
| `obsidian_list_commands` | Lists available Obsidian commands. |
| `obsidian_execute_command` | Executes an Obsidian command by id. |
| `obsidian_scaffold_workspace` | Scaffolds a new folder-based workspace with index notes. |

---

## 💡 Example Uses

- "Find my meeting notes about roadmap planning from last month."
- "Create a note in `Inbox/` with the action items from this conversation."
- "Append this summary to `Projects/MCP.md`."
- "Set the frontmatter `status` field in `Tasks/ship-docs.md` to `done`."
- "List files in my `Daily/` folder before creating today’s note."
- "Append a standup summary to today’s daily note."
- "Append these decisions under `Projects::Launch::Decisions`, and create the heading path if it does not exist."
- "Run a Dataview query for all open tasks tagged `#hn-launch`."
- "Show me notes that already link to `Projects/HackerNews Launch.md` before creating a duplicate."
- "Create a fresh client workspace with Inbox, Projects, Resources, Archive, and Daily sections."
- "Execute the Obsidian command for opening the command palette or templater workflow."

---

## ✅ Quality Checks

- `dotnet build src/Mcp.Obsidian/Mcp.Obsidian.csproj`
- `dotnet test tests/Mcp.Obsidian.Tests/Mcp.Obsidian.Tests.csproj`

The test suite covers the markdown reasoning helpers behind link extraction, heading-path parsing, smart append planning, and backlink analysis.

---

## 🛡 Security & Permissions

* **Local Execution:** This server runs entirely on your machine. No data is sent to a third-party cloud other than the LLM provider you are already using (e.g., Anthropic).
* **HTTPS:** The Local REST API uses self-signed certificates. The `VerifySsl: false` setting is required unless you configure local trust.
* **Scoped Access:** The server can only access the vault where the Local REST API plugin is active.
* **Configuration:** Keep `appsettings.json` local to your machine and never commit your Obsidian API key.

---

## 🔧 Troubleshooting

- If the MCP client cannot connect, make sure Obsidian is running and the Local REST API plugin is enabled.
- If requests fail with authentication errors, re-copy the API key from Obsidian into `appsettings.json`.
- If TLS validation fails, keep `VerifySsl` set to `false` unless you have explicitly trusted the local certificate.
- If your MCP client cannot launch `dotnet run`, publish the app first and point the client at the binary in `artifacts/obsidian-mcp`.
- If a patch against a heading fails, read the note as `document_map` first to inspect the exact heading path the API expects.

---

## 🗺 Roadmap

* [x] Basic CRUD (Create, Read, Search, Append).
* [x] Frontmatter manipulation.
* [x] **Smart Append:** Append contextually under specific H2/H3 headers.
* [x] **Backlink Logic:** AI awareness of existing links before creating new ones.
* [x] **Daily Note Helper:** Shorthand tools for modifying the current daily note.
* [x] **Command Automation:** List and execute Obsidian commands from MCP.
* [x] **Workspace Scaffolding:** Bootstrap project or client workspaces with structured folders and notes.
* [ ] **Templates and Capture Flows:** First-class helpers around templater-style workflows.
* [ ] **Graph-Aware Planning:** Deeper link graph reasoning before large note rewrites.

---

## 🤝 Contributing

Contributions are welcome.

1. Fork the repo.
2. Create your feature branch (`git checkout -b feature/amazing-feature`).
3. Commit your changes.
4. Open a Pull Request.

## 📄 License

Distributed under the MIT License. See `LICENSE` for more information.
