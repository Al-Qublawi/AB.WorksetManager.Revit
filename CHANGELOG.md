# Changelog

## 1.0.2 - 2026-10-05

- **Lists go to Excel as real .xlsx files** (Overview, Rename list, visibility status): every cell is text and
  every column is formatted as Text, so Excel no longer turns names starting with + = - @ into formulas
  (`#NAME?`), "01" into 1 or "3-4" into a date - not even names typed into the sheet later. CSV is still
  offered, with such names written as `="..."` so Excel shows them as they are.
- **Create and Rename load .xlsx** as well as .csv and .txt.
- A file Excel has already damaged (#NAME? in place of names) is reported as such when loaded.

## 1.0.1 - 2026-10-05

Asked for by the owner after his first test:
- **Tick many rows at once in every list:** select rows with Shift+click / Ctrl+click and tick one of their
  boxes (or press Space) - they all follow; Shift+click a box ticks the whole range from the last box clicked.
- **Families tab:** the search also looks in the Workset column.
- **Copyright and LinkedIn link** under the title of the window.

## 1.0.0 - 2026-10-05

First release: Overview, Create, Rename, Grids / Levels, Revit / CAD, 3D Views, Families, Visibility and
Delete tabs, for Revit 2022 - 2027, on the AB Adv Tools tab, with a code-free .msi installer.
