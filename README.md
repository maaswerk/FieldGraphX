# 🚀 FieldGraphX

**Trace and visualize cloud flow dependencies for any Dataverse field — as a tree and as an interactive graph.**

![Dynamics 365](https://img.shields.io/badge/Dynamics%20365-Dataverse-blue)
![XrmToolBox](https://img.shields.io/badge/XrmToolBox-Plugin-green)
![CI](https://github.com/maaswerk/FieldGraphX/actions/workflows/ci.yml/badge.svg)

---

## What it does

Pick a field (e.g. `msdyn_workorder` / `msdyn_name`) and FieldGraphX shows you:

- **⬆ All cloud flows that SET this field** — every Dataverse *Update/Create/Upsert a row*
  action anywhere in a flow (including inside scopes, conditions and loops).
- **⬇ All cloud flows TRIGGERED by this field** — Dataverse triggers with the field in their
  *filtering attributes*, **plus flows without filtering attributes**: those fire on *every*
  change of the entity, so they are always listed and clearly marked as **broad triggers**.
- **The whole dependency chain, recursively** — flow A sets field X → flow B triggers on X and
  sets Y → flow C triggers on Y → … (configurable depth, cycle-safe).

Results are shown as a **tree view** with a detail panel (trigger, change types, written
fields, direct link into Power Automate) and as an **interactive dependency graph**
(vis-network in a WebView2, fully offline). The graph can also be copied as a
**Mermaid** definition for wikis.

## Why

Before deleting or repurposing a field, you want to know which automations write it and which
automations react to it — including the invisible ones: flows that trigger on *every* row
change. FieldGraphX makes that whole web of dependencies visible in seconds.

## Installation

1. Install [XrmToolBox](https://www.xrmtoolbox.com/).
2. Download the `FieldGraphX-plugin` artifact from the latest CI run (or build it yourself,
   see below) and copy its contents into
   `%APPDATA%\MscrmTools\XrmToolBox\Plugins`:
   - `FieldGraphX.dll`
   - `FieldGraphX.Core.dll`
   - `Microsoft.Web.WebView2.Core.dll`, `Microsoft.Web.WebView2.WinForms.dll`, `WebView2Loader.dll`
3. Start XrmToolBox, connect to your environment, open **FieldGraphX**.

The graph tab needs the [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)
(preinstalled on current Windows 10/11); the tree view works without it.

## Usage

1. Pick the **entity** and the **field** (searchable dropdowns).
2. Click **Analyze**. All cloud flows are fetched once (cached per session — use
   **Refresh flows** after changing flows) and parsed in memory.
3. Explore:
   - Tree: `⬇ Flows triggered by this field` / `⬆ Flows that set this field`,
     `⚠` marks broad triggers, `[Draft]`/`[Off]` mark unpublished/disabled flows.
   - Graph tab: hierarchical or force layout, click a node to see details,
     double-click a flow to open it in Power Automate.
   - Options: analysis depth, include drafts / turned-off flows, show/recurse broad triggers.

## Architecture

```
src/FieldGraphX.Core       netstandard2.0 — parser, analyzer, graph model, exporters.
                           No SDK/WinForms dependencies → unit-testable anywhere.
src/FieldGraphX            net48 WinForms XrmToolBox plugin (UI, Dataverse access, WebView2).
tests/FieldGraphX.Core.Tests  xUnit on net8.0 with realistic clientdata fixtures.
```

How the analysis works:

1. All `workflow` rows with `category = 5` (cloud flows) are fetched with paging.
2. Each flow's `clientdata` JSON is parsed: the trigger
   (`subscriptionRequest/entityname`, `message` → create/update/delete,
   `filteringattributes`; empty = broad trigger) and every Dataverse write action
   (deep JSON walk, `item/*` parameters, OData lookup suffixes normalized).
3. Entity set names (`msdyn_workorders`) are resolved to logical names
   (`msdyn_workorder`) via metadata.
4. A breadth-first search builds one global graph (fields and flows as nodes,
   *triggers*/*sets* as edges). Cycles simply close a loop; change types are matched
   (an update action never fires a create-only trigger).

Known limitations: writes done via HTTP/custom connectors or bound actions ("Perform …")
are not detected; only the first trigger of a flow is evaluated (cloud flows have one).

## Building

Everything builds with the .NET SDK — on Windows *and* Linux (the plugin targets net48 via
reference assemblies):

```bash
dotnet build FieldGraphX.sln
dotnet test tests/FieldGraphX.Core.Tests
```

Optional: set the `XTB_PLUGINS_DIR` environment variable (Windows) and the plugin dll is
copied to your XrmToolBox plugins folder after each build.

## Manual validation checklist (Windows / XrmToolBox)

- [ ] Plugin loads, entity/field dropdowns fill after connecting.
- [ ] Analysis on a known field lists the expected setter/trigger flows.
- [ ] A flow without filtering attributes appears, marked as broad trigger.
- [ ] "Open in Power Automate" opens the right flow (URL uses `workflowidunique`
      — verify once per org).
- [ ] Graph tab renders; clicking nodes syncs with the tree; double-click opens the flow.
- [ ] Without WebView2 runtime, the graph tab shows the download hint instead of crashing.

## License

MIT.
