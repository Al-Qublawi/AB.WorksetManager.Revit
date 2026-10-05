# Live testing through the Revit MCP bridge

C# snippets for `send_code_to_revit` (mcp-server-for-revit). The bridge runs them **inside a transaction on
the ACTIVE document** - never change that document. Everything here works on a background local copy.

| Snippet | What it does |
|---|---|
| `status.cs` | Revit version and open documents (read-only). Also returns the result of a snippet that timed out. |
| `make-test-central.cs` | Fresh workshared test set from the Snowdon Towers Architectural sample: central + background local (~3 min). |
| `run-all.cs` | Loads dev build `v` from bytes and runs `DevHarness.RunAll` on the local copy. |

Build a dev copy first: `.\tools\devbuild.ps1 -N <n>` (new `n` for every build loaded into the same Revit
session - Revit never unloads an assembly). Keep snippets under ~8 KB; put logic in `DevHarness`.
