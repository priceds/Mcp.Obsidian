# Obsidian.McpServer (Smart Notes for Obsidian via MCP)

> A .NET 10 MCP server that lets agents (Claude / Gemini / Cursor-style) **search, read, create, and update Obsidian notes** using the **Obsidian Local REST API** plugin.

Turn Obsidian into an AI-powered knowledge vault — safely and locally.

---

## ✨ What this enables

- Ask an agent: *“Research X and write a clean note in my vault”*
- Ask: *“Search my notes for Y and summarize what I already know”*
- Ask: *“Append this section under heading ## Decisions in that note”*
- Keep everything **local-first**, versionable (Git), and automatable.

---

## 🧩 How it works (high level)

Agent (Claude / Gemini / etc.)  
→ MCP client loads tools from this MCP server  
→ This MCP server calls **Obsidian Local REST API**  
→ Notes are created/updated inside your vault

No direct file hacks required. Obsidian remains the UI.

---

## ✅ Requirements

- **.NET 10 SDK** (preview/RC depending on release cycle)
- **Obsidian Desktop**
- Community plugin: **Local REST API** installed & enabled
- A vault opened in Obsidian

> This project targets *desktop vault automation* through the Local REST API.
> “Obsidian Sync cloud API” isn’t publicly exposed; this is the stable way.

---

## ⚡ Quick Start

### 1) Install & configure Obsidian Local REST API
1. Obsidian → Settings → Community plugins → Browse
2. Install **Local REST API**
3. Enable it
4. Copy the API key and note the server URL (typically `https://localhost:27123`)

### 2) Configure this MCP server
Create `appsettings.json`:

```json
{
  "Obsidian": {
    "BaseUrl": "https://localhost:27123",
    "ApiKey": "PASTE_YOUR_KEY_HERE",
    "VaultName": "MyVault",
    "VerifySsl": false
  }
}
