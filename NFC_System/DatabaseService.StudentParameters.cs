using System;
using MySqlConnector;

namespace NFC_System;

public sealed partial class DatabaseService
{
    private static void AddStudentParameters(MySqlCommand command, StudentRecord student, string? salt, string? hash)
    {
        static object Optional(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;
        command.Parameters.AddWithValue("@student_id", student.StudentId);
        command.Parameters.AddWithValue("@full_name", student.FullName);
        command.Parameters.AddWithValue("@email", Optional(student.Email));
        command.Parameters.AddWithValue("@course", Optional(student.Course));
        command.Parameters.AddWithValue("@year_level", int.TryParse(student.YearLevel, out int year) ? year : DBNull.Value);
        command.Parameters.AddWithValue("@section_name", Optional(student.SectionName));
        command.Parameters.AddWithValue("@status", student.Status);
        // Unenrolled profiles must not compete for a unique empty-string NFC UID.
        command.Parameters.AddWithValue("@nfc_uid", Optional(student.NfcUid));
        command.Parameters.AddWithValue("@qr_credential", Optional(student.QrCredential));
        command.Parameters.AddWithValue("@pin_salt", Optional(salt));
        command.Parameters.AddWithValue("@pin_hash", Optional(hash));
        command.Parameters.AddWithValue("@photo_data", student.PhotoData != null ? student.PhotoData : DBNull.Value);
        command.Parameters.AddWithValue("@is_temporary", student.IsTemporary ? 1 : 0);
    }
}
