# Live testing through a Revit MCP bridge

Everything here works on a background local copy (`ABWM_Arch_local`) of a workshared test set made from the
Snowdon Towers Architectural sample. The model the user has open is never touched.

Build a dev copy first: `.\tools\devbuild.ps1 -N <n>` (new `n` for every build loaded into the same Revit
session - Revit never unloads an assembly).

## With AB Revit MCP 1.6.0+ (preferred) - `ab-mcp\`

The human opens the gates for the test session: the server's `--allow-code-execution` flag and **Code exec** in
Revit (AB Adv Tools > MCP Bridge; off again when Revit closes). Each call needs `"confirm": true` once the
human approved that run. Works on Revit 2022-2027 (2025+ snippets are compiled by the MCP server).

| Step | How |
|---|---|
| Open documents (read-only) | `revit_list_open_documents` - background copies show `openWindows: 0`. |
| Make the test set (~3 min) | `revit_execute_code` with the text of `ab-mcp\make-test-central.cs` as `code`, `"transaction": "none"`, `"waitSeconds": 60`; it answers with a `jobId`. |
| Run the harness | `revit_run_assembly_method` with the `arguments` of `ab-mcp\run-all.json` (replace `<N>`). |
| Long runs | `revit_get_job_result` with the `jobId` and `"waitSeconds": 300`. Never repeat the original call. |

`revit_run_assembly_method` loads the dev build from bytes (with its .pdb), loads `AB.WorksetManager.Core`
beside it on demand, reuses the same bytes, and refuses a rebuild that kept its version. A long report comes
back whole in `returned.fullTextFile` (it replaces the old `run-all.txt`). The ported snippet compiles for
Revit 2025, 2026.5 and 2027; not yet run live through AB MCP.

## With mcp-servers-for-revit (`send_code_to_revit`, older)

C# snippets for `send_code_to_revit`. That bridge runs them **inside a transaction on the ACTIVE document** -
never change that document.

| Snippet | What it does |
|---|---|
| `status.cs` | Revit version and open documents (read-only). Also returns the result of a snippet that timed out. |
| `make-test-central.cs` | Fresh workshared test set from the Snowdon Towers Architectural sample: central + background local (~3 min). |
| `run-all.cs` | Loads dev build `v` from bytes and runs `DevHarness.RunAll` on the local copy. |

Keep snippets under ~8 KB; put logic in `DevHarness`.
