# AB Worksets Manager - working context

Revit add-in (2022-2027) for managing worksets in workshared models, by Abdullah Lotfy (GitHub `Al-Qublawi`).
Part of the AB Adv Tools suite; the kit is vendored in `shared/ABAdvTools` (canonical copy: private repo
`Al-Qublawi/AB.AdvTools.Kit` - change it there, then run its `tools/sync-kit.ps1 -All`).

## Source of truth: GitHub

- Repository: **`Al-Qublawi/AB.WorksetManager.Revit`, private** (owner's choice, 2026-10-05).
- Start every session with `git pull`. Commit finished, verified work and push it to `main`.
- Nothing may live only on one PC: knowledge here, helper scripts in `tools/`. No absolute user-profile paths
  in committed files.
- Release checks: `App.GitHubRepository` is set, but GitHub answers anonymous release queries on a private
  repo with 404, so the add-in silently reports no release information until the repo is made public.
  Do not create a GitHub release or make the repo public unless asked.

## The owner's decisions (2026-10-05)

- Ask for confirmation before starting a sizeable change. **Never touch the model he has open** - live tests
  run on background copies. **Never downgrade** a feature in a later version.
- The nine features he asked for, and his answers to the design questions:
  1. Create from a text file or typed list, validate against the model (all or ticked).
  2. Rename from a new-name column.
  3. Grids/levels (selected or all) to *Shared Levels and Grids* or a chosen workset; scope boxes and
     reference planes as unticked options.
  4. Revit links and linked/imported CAD to a workset, or a new workset per link from a pattern -
     default `Z-Link-RVT-{FileName}` / `Z-Link-CAD-{FileName}`, editable per row.
  5. 3D views: combined, or one per workset (`3D - WS - {Workset}`, `3D - WS - Combined`);
     **an existing 3D view with that name is updated**, not duplicated.
  6. Families: **all model categories** (system families too), by category / family / type / source
     workset; grouped elements move with their whole model group.
  7. Visibility on/off for views and view templates, status check (view x workset table).
  8. Delete ticked worksets after moving their elements to a target.
  9. Not workshared -> **just a message**.
- Revit 2022-2027, private repo, English only, code-free MSI (Only me default, Everyone for IT).

## 1.0.1 (owner's requests after his first test, 2026-10-05)

- Multi-row ticking in every tick list (`UI/ManagerWindow.Selection.cs`): Shift+click a box ticks the range
  from the last box clicked (in the shown, filtered order); ticking a box of a row inside a multi-row
  selection ticks all selected rows; Space toggles the selected rows. Tick boxes carry `Tag="Tick"` and are
  not focusable (so Space reaches the grid). Locked rows are never ticked.
- Families search also matches the Workset column.
- Banner line under the title: `AdvToolsBrand.Copyright` + LinkedIn link (`AdvToolsBrand.LinkedInUrl`).
- `DevHarness.MultiTickTest` (briefly shown real window, Delete tab) **passed in Revit 2026.5**: pressing a tick
  box keeps a 3-row selection, its click ticks all 3, Space unticks / ticks them; banner text correct.
  Shift+click reads the real keyboard and is not simulated - **the owner tried it by hand in 1.0.1: works.**

## 1.0.2 - Excel ate the names (owner's report, 2026-10-05)

His real worksets start with `+ Link_...`; the 1.0.0 CSV export opened in Excel as `=+ Link_...` -> `#NAME?`
(and saving it would have destroyed the names). Fix: `Core/Spreadsheet.cs` - `Xlsx` writes inline-string cells
with a Text number format (numFmt 49) on every cell and column, and reads .xlsx (shared strings, inline strings,
first sheet via workbook rels); `ListFile` picks the format by extension; CSV guards values Excel would misread
(`ExcelText.NeedsGuard`: leading = + - @, numbers, dates) as `="..."`, and `NameList.FromCells` unwraps them.
Checked through real Excel by COM on this PC: the .xlsx and the CSV both show `+ Link_DG-...`, `01`, `3-4`,
`=Equals` exactly; `+ Link_NEW-A` typed into the New Name column stays text; Excel's saved file reads back exactly.

## Layout

- `src/AB.WorksetManager.Core` (netstandard2.0, no Revit): `WorksetNames` (valid names), `NameList`
  (text / Excel paste / CSV), `CreatePlan`, `RenamePlan` (+ `Steps`: swaps and capitals-only renames go
  through `ABWM-TEMP-n-xxxx` names), `NameTemplate`, `Csv`, `Xlsx` / `ListFile` / `ExcelText` (Spreadsheet.cs). Tested by `tests/AB.WorksetManager.Core.Tests`.
- `src/AB.WorksetManager.Addin` (net48 / net8.0-windows / net10.0-windows):
  - `Infrastructure/Report.cs` - `ModelChange.Run` (one transaction, rollback on any error; report lines
    written inside are dropped if Revit rolls back), `DocumentGuard`, `Report`.
  - `Infrastructure/Infrastructure.cs` - `Failures` preprocessor: warnings dismissed and listed, **any error
    rolls back; never `ResolveFailure`** (Revit's resolutions can delete elements). `Compat`, `Log`.
  - `Services/ElementMover` - the one path every move goes through: `Plan` (group -> top group, locked
    reasons, ownership `BlockReason`), `Apply` (`CheckoutElements` first, then one transaction that also
    creates missing target worksets by name).
  - `Services/WorksetService` (create, rename, delete, default visibility, active), `ModelItems`
    (datums, links, element catalog), `ViewService` (visibility, templates, 3D views), `Worksets`.
  - `UI/ManagerWindow.xaml` - one modal window, nine tabs, styles inline (UserControls in separate XAML
    would hit StaticResource lookup before they are parented). Code-behind split by tab:
    `.Worksets.cs` (Overview, Create, Rename, Delete), `.Model.cs` (Grids/Levels, Links, Families),
    `.Views.cs` (3D, Visibility). Every handler goes through `Run` / `Safe` - nothing throws into Revit.
  - **Pick in model**: the tab calls `PickThen`, the window `Hide()`s, which ends `ShowDialog`; the command
    loop runs the pick, then `AfterPick` queues the follow-up on the dispatcher and shows the same window
    again (all tab state kept). Verified live (`DevHarness.ModalHideTest`).
  - `Diagnostics/DevHarness` (Debug only): `RunAll(doc)`, `RenderTabs(uiapp, doc, folder)`, `ModalHideTest()`.

## Revit API facts checked live (Revit 2026.5, 2026-10-05)

- Workset names refuse exactly `\ : { } [ ] | ; < > ? ` ~` (tested each of 31 punctuation characters with
  `Workset.Create`). Names compare **case-insensitively**; a capitals-only `RenameWorkset` throws "already in
  use", so it goes through a temporary name. The first temporary prefix was `~ABWM-` - refused by Revit
  (`~`), caught live; rollback left nothing behind.
- `RenameWorkset` on a workset nobody owns works in a local copy and **gives the user ownership** - the
  add-in reminds to Synchronize with Central and relinquish.
- **Group members follow their group's workset** (87/87); a member's `ELEM_PARTITION_PARAM` is read-only.
- `RevitLinkType` has an editable `ELEM_PARTITION_PARAM`; instance and type move together.
- 38 of 45 view templates in the Snowdon sample control `VIS_GRAPHICS_WORKSETS` - template handling matters.
- `WorksetTable.SetActiveWorksetId` works outside a transaction. `CheckoutElements` / `CheckoutWorksets` must
  be called outside one. `DeleteWorkset` needs the workset checked out (`CheckoutWorksets`), a transaction,
  and exists from **2022.1** - `DeleteOne` is `NoInlining` so 2022.0 fails with an explainable
  `MissingMethodException` / `TypeLoadException`.
- A modal WPF window hidden from inside `ShowDialog` returns from it; the same window can `ShowDialog` again.

## Live test (how)

**With AB Revit MCP 1.6.0+ (preferred):** `tools/revit-bridge/README.md`, files in `tools/revit-bridge/ab-mcp` -
`revit_execute_code` with `"transaction": "none"` for the test set, `revit_run_assembly_method` for the
harness, `revit_get_job_result` for long runs. Needs the human to open the server flag and Code exec.
Compiles for 2025-2027; not yet run live this way. The older way:

Revit open with the MCP bridge switched on (`send_code_to_revit` runs C# **inside a transaction on the
ACTIVE document** - never do model work there). Snippets in `tools/revit-bridge/`:

1. `make-test-central.cs` - copies the running Revit's *Snowdon Towers Sample Architectural*, enables
   worksharing, adds test worksets, saves it as a central in `%LOCALAPPDATA%\Temp\ABWorksetDev\central`
   and opens a new **local** copy in the background (links unloaded). ~3 min: the bridge reports a timeout,
   it keeps running; `status.cs` afterwards returns its result.
2. `tools/devbuild.ps1 -N <n>` - Debug build 1.0.0.n into `...\ABWorksetDev\v<n>` (new n per load).
3. `run-all.cs` (set `v`) - `DevHarness.RunAll` on the local copy. Needs a fresh copy each time (it renames
   and deletes). Baseline 2026-10-05, Revit 2026.5: **ALL PASSED** - create (2 of 5 ready), rename (plain,
   swap, capitals-only, invalid skipped), 46 grids+levels, 6 links to 6 new `Z-Link-RVT-*` worksets (type
   with instance; second run 0 changes), 907 catalog rows / 14,346 elements, grouped member -> group,
   3D combined + per workset (re-run updates), visibility skip vs template, delete with move (83 elements,
   active workset handed over).
4. `RenderTabs` -> PNGs of all nine tabs (offscreen, never shown) - how `docs/images` were made.

## Not verified yet

- The owner installed and used 1.0.0 and 1.0.1 (ribbon button, window, tabs, Shift+click ticking) -
  his requests after the first test became 1.0.1. "Pick in model" has not been reported on yet.
- Revit 2022, 2023, 2024, 2025, 2027 live (they build; 2026 tested).
- Elements borrowed by **another user** (only one user on this PC) - the code path is
  `GetCheckoutStatus == OwnedByOtherUser` -> skipped with the owner's name.
- Cloud (ACC) models.
