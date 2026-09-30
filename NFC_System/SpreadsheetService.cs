using ClosedXML.Excel;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Mail;
using System.Text;

namespace NFC_System;

public enum StudentImportKind { Profiles, Roster }
public enum ImportRowStatus { Valid, Duplicate, Unknown, Error }
public enum SpreadsheetValueKind { Text, DateTime, Number }
public sealed record SpreadsheetColumn(string Header, SpreadsheetValueKind Kind = SpreadsheetValueKind.Text);
public sealed record SpreadsheetTable(string Name, IReadOnlyList<SpreadsheetColumn> Columns, IReadOnlyList<object?[]> Rows);
public sealed record SpreadsheetImportRow(int RowNumber, IReadOnlyDictionary<string, string> Values, string Error = "");
public sealed record SpreadsheetImportDocument(StudentImportKind Kind, IReadOnlyList<SpreadsheetImportRow> Rows);
public sealed record StudentImportProfile(string StudentId, string FullName, string Email, string Course, string YearLevel, string Section);
public sealed record StudentImportPreviewRow(int RowNumber, string StudentId, string FullName, ImportRowStatus Status, string Message, StudentImportProfile? Profile = null);

public static class SpreadsheetService
{
    public const int MaxImportRows = 10000;
    public const string PendingEnrollmentStatus = "Pending Enrollment";
    private const long MaxFileBytes = 25 * 1024 * 1024;
    private static readonly string[] ProfileHeaders = { "StudentId", "FullName", "Email", "Course", "YearLevel", "Section" };
    private static readonly string[] RosterHeaders = { "StudentId" };

    public static SpreadsheetImportDocument ReadImport(string path, StudentImportKind kind)
    {
        if (new FileInfo(path).Length > MaxFileBytes) throw new InvalidDataException("The import file must be 25 MB or smaller.");
        using var input = File.OpenRead(path);
        return ReadImport(input, Path.GetExtension(path), kind);
    }

    public static SpreadsheetImportDocument ReadImport(Stream input, string extension, StudentImportKind kind)
    {
        if (input.CanSeek && input.Length > MaxFileBytes) throw new InvalidDataException("The import file must be 25 MB or smaller.");
        var expected = kind == StudentImportKind.Profiles ? ProfileHeaders : RosterHeaders;
        var rows = new List<SpreadsheetImportRow>();
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var parser = new TextFieldParser(input, new UTF8Encoding(false, true), true, true)
            {
                TextFieldType = FieldType.Delimited, HasFieldsEnclosedInQuotes = true, TrimWhiteSpace = false
            };
            parser.SetDelimiters(",");
            string[] headers;
            try { headers = ValidateHeaders(parser.ReadFields() ?? Array.Empty<string>(), expected); }
            catch (MalformedLineException ex) { throw new InvalidDataException("The CSV header is malformed.", ex); }
            while (!parser.EndOfData)
            {
                int line = checked((int)parser.LineNumber);
                try
                {
                    var values = parser.ReadFields() ?? Array.Empty<string>();
                    if (values.All(string.IsNullOrWhiteSpace)) continue;
                    AddRow(rows, line, headers, values, values.Length != headers.Length ? "Column count does not match the header." : "");
                }
                catch (MalformedLineException) { AddRow(rows, line, headers, Array.Empty<string>(), "Malformed CSV quoting. Correct this row before importing."); }
            }
        }
        else if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            if (!input.CanSeek) throw new InvalidDataException("An Excel import requires a seekable stream.");
            long start = input.Position;
            using (var archive = new ZipArchive(input, ZipArchiveMode.Read, true))
            {
                if (archive.Entries.Count > 2000 || archive.Entries.Sum(e => e.Length) > 100 * 1024 * 1024)
                    throw new InvalidDataException("The expanded Excel file is too large.");
            }
            input.Position = start;
            using var workbook = new XLWorkbook(input);
            if (workbook.Worksheets.Count != 1) throw new InvalidDataException("Use a workbook with exactly one worksheet so no rows are omitted.");
            var sheet = workbook.Worksheet(1);
            var used = sheet.RangeUsed(XLCellsUsedOptions.Contents);
            if (used == null) throw new InvalidDataException("The workbook is empty.");
            int lastColumn = used.RangeAddress.LastAddress.ColumnNumber;
            int lastRow = used.RangeAddress.LastAddress.RowNumber;
            if (lastColumn > expected.Length || lastRow > MaxImportRows + 1)
                throw new InvalidDataException($"Use the template columns and at most {MaxImportRows:N0} data rows.");
            if (sheet.Row(1).Cells(1, lastColumn).Any(c => c.HasFormula)) throw new InvalidDataException("Header formulas are not allowed.");
            var headers = ValidateHeaders(Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(1, c).GetString()).ToArray(), expected);
            for (int r = 2; r <= lastRow; r++)
            {
                var cells = Enumerable.Range(1, lastColumn).Select(c => sheet.Cell(r, c)).ToArray();
                if (cells.All(c => c.IsEmpty())) continue;
                string error = cells.Any(c => c.HasFormula) ? "Formulas are not allowed. Replace formulas with literal text values." : "";
                int idColumn = Array.FindIndex(headers, h => h.Equals("StudentId", StringComparison.OrdinalIgnoreCase));
                if (cells[idColumn].DataType != XLDataType.Text && !cells[idColumn].IsEmpty())
                    error = "StudentId must be stored as text. Re-enter the full ID in a text-formatted template cell.";
                if (cells.Any(c => c.DataType == XLDataType.Error)) error = "The row contains an Excel error value.";
                AddRow(rows, r, headers, cells.Select(c => c.HasFormula ? "" : c.GetString()).ToArray(), error);
            }
        }
        else throw new InvalidDataException("Choose a .csv or .xlsx file. Legacy .xls files are not supported.");
        if (rows.Count == 0) throw new InvalidDataException("The file contains no student rows.");
        return new(kind, rows.AsReadOnly());
    }

    private static string[] ValidateHeaders(string[] headers, string[] expected)
    {
        headers = headers.Select(h => h.Trim().TrimStart('\uFEFF')).ToArray();
        if (headers.Length != expected.Length || headers.Distinct(StringComparer.OrdinalIgnoreCase).Count() != headers.Length ||
            expected.Any(h => !headers.Contains(h, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("Use exactly these columns: " + string.Join(", ", expected) + ". Credential and status columns are not accepted.");
        return headers;
    }

    private static void AddRow(List<SpreadsheetImportRow> rows, int number, string[] headers, string[] values, string error)
    {
        if (rows.Count >= MaxImportRows) throw new InvalidDataException($"Import at most {MaxImportRows:N0} rows per file.");
        var fields = headers.Select((h, i) => (h, value: i < values.Length ? values[i].Trim() : ""))
            .ToDictionary(v => v.h, v => v.value, StringComparer.OrdinalIgnoreCase);
        if (fields.Values.Any(v => v.Length > 1000)) error = "A cell exceeds the import field length limit.";
        rows.Add(new(number, fields, error));
    }

    public static IReadOnlyList<StudentImportPreviewRow> PreviewProfiles(SpreadsheetImportDocument document, IEnumerable<string> existingStudentIds, IEnumerable<string> courses)
    {
        if (document.Kind != StudentImportKind.Profiles) throw new ArgumentException("A profile import is required.");
        var existing = existingStudentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownCourses = courses.ToDictionary(c => c, c => c, StringComparer.OrdinalIgnoreCase);
        var duplicateIds = DuplicateIds(document);
        return document.Rows.Select(row =>
        {
            string Get(string field) => row.Values.TryGetValue(field, out var value) ? value : "";
            var profile = new StudentImportProfile(Get("StudentId"), Get("FullName"), Get("Email"), Get("Course"), Get("YearLevel"), Get("Section"));
            string error = row.Error.Length > 0 ? row.Error : ValidateProfile(profile);
            if (error.Length > 0) return new StudentImportPreviewRow(row.RowNumber, profile.StudentId, profile.FullName, ImportRowStatus.Error, error, profile);
            if (duplicateIds.Contains(profile.StudentId) || existing.Contains(profile.StudentId))
                return new(row.RowNumber, profile.StudentId, profile.FullName, ImportRowStatus.Duplicate, existing.Contains(profile.StudentId) ? "ID already exists. Existing profiles will not be overwritten." : "ID appears more than once in this file.", profile);
            if (!knownCourses.TryGetValue(profile.Course, out var canonicalCourse))
                return new(row.RowNumber, profile.StudentId, profile.FullName, ImportRowStatus.Unknown, "Course is not in the directory. Correct it or create the course first.", profile);
            profile = profile with { Course = canonicalCourse };
            return new(row.RowNumber, profile.StudentId, profile.FullName, ImportRowStatus.Valid, PendingEnrollmentStatus, profile);
        }).ToList();
    }

    public static IReadOnlyList<StudentImportPreviewRow> PreviewRoster(SpreadsheetImportDocument document, IEnumerable<string> existingStudentIds, IEnumerable<string>? alreadyIncludedIds = null)
    {
        if (document.Kind != StudentImportKind.Roster) throw new ArgumentException("A roster import is required.");
        var existing = existingStudentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var included = (alreadyIncludedIds ?? Array.Empty<string>()).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var duplicates = DuplicateIds(document);
        return document.Rows.Select(row =>
        {
            string id = row.Values.TryGetValue("StudentId", out var value) ? value : "";
            string error = row.Error.Length > 0 ? row.Error : ValidateId(id);
            if (error.Length > 0) return new StudentImportPreviewRow(row.RowNumber, id, "", ImportRowStatus.Error, error);
            if (duplicates.Contains(id) || included.Contains(id)) return new(row.RowNumber, id, "", ImportRowStatus.Duplicate, included.Contains(id) ? "Already included in this event." : "ID appears more than once in this file.");
            if (!existing.Contains(id)) return new(row.RowNumber, id, "", ImportRowStatus.Unknown, "Student ID was not found in the directory.");
            return new(row.RowNumber, id, "", ImportRowStatus.Valid, "Ready to add to event roster.");
        }).ToList();
    }

    private static HashSet<string> DuplicateIds(SpreadsheetImportDocument document) => document.Rows
        .Select(r => r.Values.TryGetValue("StudentId", out var id) ? id : "")
        .GroupBy(id => id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static string ValidateProfile(StudentImportProfile profile)
    {
        string idError = ValidateId(profile.StudentId);
        if (idError.Length > 0) return idError;
        if (string.IsNullOrWhiteSpace(profile.FullName) || profile.FullName.Length > 100) return "FullName is required and must be at most 100 characters.";
        if (string.IsNullOrWhiteSpace(profile.Course) || profile.Course.Length > 100) return "Course is required and must be at most 100 characters.";
        if (!int.TryParse(profile.YearLevel, NumberStyles.None, CultureInfo.InvariantCulture, out int year) || year < 1 || year > 20) return "YearLevel must be a whole number from 1 to 20.";
        if (profile.Section.Length > 50) return "Section must be at most 50 characters.";
        if (profile.Email.Length > 150 || (profile.Email.Length > 0 && (!MailAddress.TryCreate(profile.Email, out var address) || address.Address != profile.Email))) return "Email must be a valid address of at most 150 characters, or blank.";
        if (new[] { profile.FullName, profile.Email, profile.Course, profile.YearLevel, profile.Section }.Any(v => v.Any(char.IsControl))) return "Profile fields cannot contain line breaks or control characters.";
        return "";
    }

    private static string ValidateId(string id) => StudentProfileRules.ValidateStudentId(id);

    public static void WriteTemplate(Stream output, string extension, StudentImportKind kind)
    {
        var headers = kind == StudentImportKind.Profiles ? ProfileHeaders : RosterHeaders;
        Write(output, extension, new(kind == StudentImportKind.Profiles ? "Students" : "Event roster", headers.Select(h => new SpreadsheetColumn(h)).ToArray(), Array.Empty<object?[]>()), true);
    }

    public static void Write(Stream output, string extension, SpreadsheetTable table, bool template = false)
    {
        if (table.Columns.Count == 0) throw new InvalidOperationException("Select at least one column to export.");
        if (table.Rows.Any(r => r.Length != table.Columns.Count)) throw new ArgumentException("Export row width does not match the selected columns.");
        if (extension.Equals(".csv", StringComparison.OrdinalIgnoreCase))
        {
            using var writer = new StreamWriter(output, new UTF8Encoding(true), 4096, true);
            writer.WriteLine(string.Join(",", table.Columns.Select(c => CsvField(c.Header))));
            foreach (var row in table.Rows) writer.WriteLine(string.Join(",", row.Select(v => CsvField(v))));
        }
        else if (extension.Equals(".xlsx", StringComparison.OrdinalIgnoreCase))
        {
            using var workbook = new XLWorkbook();
            AddSheet(workbook, table, template);
            workbook.SaveAs(output);
        }
        else throw new InvalidDataException("Choose a .csv or .xlsx export.");
    }

    public static void WriteWorkbook(Stream output, IReadOnlyList<SpreadsheetTable> tables)
    {
        if (tables.Count == 0) throw new InvalidOperationException("There are no report tables to export.");
        using var workbook = new XLWorkbook();
        foreach (var table in tables) AddSheet(workbook, table, false);
        workbook.SaveAs(output);
    }

    private static void AddSheet(XLWorkbook workbook, SpreadsheetTable table, bool template)
    {
        if (table.Columns.Count == 0 || table.Columns.Count > 16384 || table.Rows.Count > 1048575 || table.Rows.Any(r => r.Length != table.Columns.Count))
            throw new InvalidDataException("The export exceeds Excel limits or has inconsistent columns.");
        var sheet = workbook.Worksheets.Add(table.Name);
        for (int c = 0; c < table.Columns.Count; c++)
        {
            var column = table.Columns[c];
            sheet.Cell(1, c + 1).Value = column.Header;
            sheet.Column(c + 1).Style.NumberFormat.Format = column.Kind switch { SpreadsheetValueKind.DateTime => "yyyy-mm-dd hh:mm:ss.000", SpreadsheetValueKind.Number => "0.###", _ => "@" };
            sheet.Column(c + 1).Width = column.Kind == SpreadsheetValueKind.DateTime ? 27 : Math.Clamp(column.Header.Length + 8, 18, 42);
        }
        for (int r = 0; r < table.Rows.Count; r++)
            for (int c = 0; c < table.Columns.Count; c++)
            {
                object? value = table.Rows[r][c];
                var cell = sheet.Cell(r + 2, c + 1);
                if (value == null || value == DBNull.Value) continue;
                if (table.Columns[c].Kind == SpreadsheetValueKind.DateTime && value is DateTime date) cell.Value = date;
                else if (table.Columns[c].Kind == SpreadsheetValueKind.Number && value is IConvertible && value is not string) cell.Value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                else cell.Value = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
            }
        var header = sheet.Range(1, 1, 1, table.Columns.Count);
        header.Style.Font.Bold = true;
        header.Style.Font.FontColor = XLColor.White;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#17665B");
        sheet.Row(1).Height = 24;
        sheet.SheetView.FreezeRows(1);
        if (table.Rows.Count > 0) sheet.Range(1, 1, table.Rows.Count + 1, table.Columns.Count).SetAutoFilter();
        if (template) sheet.Range(2, 1, 1001, table.Columns.Count).Style.NumberFormat.Format = "@";
    }

    private static string CsvField(object? value)
    {
        string text = value is DateTime date ? date.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture) : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "";
        // Quoting alone does not prevent spreadsheet formula execution.
        string trimmed = text.TrimStart();
        if (value is string && (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]) || text.Length > 0 && char.IsControl(text[0]))) text = "'" + text;
        return "\"" + text.Replace("\"", "\"\"") + "\"";
    }
}
