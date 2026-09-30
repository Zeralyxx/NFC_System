using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace NFC_System;

public static class ReportSpreadsheetTables
{
    public static string DeviceName(string? value) => string.IsNullOrWhiteSpace(value) ? "Unknown/Legacy" : value;

    public static object EventTimestamp(string value) => DateTime.TryParseExact(value, "MMM dd, yyyy - hh:mm:ss tt", CultureInfo.CurrentCulture,
        DateTimeStyles.None, out var timestamp) ? timestamp : value;

    public static SpreadsheetTable Traffic(IEnumerable<VerificationLogRecord> records) => new("Campus traffic",
        new SpreadsheetColumn[]
        {
            new("Timestamp", SpreadsheetValueKind.DateTime), new("Student ID"), new("Student Name"), new("Course"), new("Section"),
            new("Action"), new("Status"), new("Auth Speed (ms)", SpreadsheetValueKind.Number), new("DB Query Speed (ms)", SpreadsheetValueKind.Number),
            new("Device ID"), new("Device Name")
        }, records.Select(l => new object?[] { l.RawTimestamp == DateTime.MinValue ? l.Timestamp : l.RawTimestamp, l.StudentId, l.FullName, l.Course,
            l.Section, l.Action, l.Status, l.AuthSpeedMs, l.DbQuerySpeedMs, l.DeviceId, DeviceName(l.DeviceName) }).ToList());
}
