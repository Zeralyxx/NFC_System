# Spreadsheet integration contracts

- `new StudentImportWindow()` opens the profile import preview; `StudentsImported` fires after a successful transaction for directory refresh.
- `SpreadsheetPicker.PickImportAsync(Window)` offers `.csv` / `.xlsx`.
- `SpreadsheetService.ReadImport(path, StudentImportKind.Profiles|Roster)` parses off the UI thread.
- `SpreadsheetService.PreviewRoster(document, existingStudentIds, alreadyIncludedIds)` returns all rows with `Valid`, `Duplicate`, `Unknown`, or `Error` status. Consumers must display every row and explicitly confirm any exclusion, or block save until all rows are valid.
- `SpreadsheetPicker.SaveTemplateAsync(Window, StudentImportKind)` offers templates in both formats. Profile columns: `StudentId,FullName,Email,Course,YearLevel,Section`; roster: `StudentId` only.
- `SpreadsheetColumn(Header, Kind = SpreadsheetValueKind.Text)` / `SpreadsheetTable(Name, Columns, Rows)` describe exports. Rows are `object?[]`; date columns accept `DateTime`, numeric columns accept numeric values. Text is never emitted as formulas.
- `SpreadsheetPicker.SaveAsync(Window, suggestedName, table)` returns false for picker cancellation, true only after write/provider completion; throws on errors.
- `SpreadsheetService.Write(Stream, extension, table)` is UI-independent and shared by tests and exports.
- Imported profile status is exactly `Pending Enrollment`; NFC/PIN/QR columns are SQL NULL, never imported or generated. Parent must wire authorized enrollment through existing edit flow and enforce pending status in verification/graduation paths.

The service rejects formulas, unexpected credential columns, oversized/multi-sheet imports, and numeric Excel IDs whose original leading zeros cannot be known. Use text-formatted template cells. CSV has no cell type; use XLSX to preserve text IDs when opening in Excel.
