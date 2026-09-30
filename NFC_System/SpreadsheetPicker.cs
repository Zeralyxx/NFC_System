using Microsoft.UI.Xaml;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Provider;

namespace NFC_System;

public static class SpreadsheetPicker
{
    public static async Task<StorageFile?> PickImportAsync(Window owner)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary };
        picker.FileTypeFilter.Add(".csv");
        picker.FileTypeFilter.Add(".xlsx");
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        return await picker.PickSingleFileAsync();
    }

    public static async Task<bool> SaveAsync(Window owner, string suggestedName, SpreadsheetTable table)
    {
        if (table.Columns.Count == 0) throw new InvalidOperationException("Select at least one column to export.");
        var file = await CreateSavePicker(owner, suggestedName).PickSaveFileAsync();
        if (file == null) return false;
        await WriteFileAsync(file, stream => SpreadsheetService.Write(stream, file.FileType, table));
        return true;
    }

    public static async Task<bool> SaveTemplateAsync(Window owner, StudentImportKind kind)
    {
        var file = await CreateSavePicker(owner, kind == StudentImportKind.Profiles ? "Student_Import_Template" : "Event_Roster_Template").PickSaveFileAsync();
        if (file == null) return false;
        await WriteFileAsync(file, stream => SpreadsheetService.WriteTemplate(stream, file.FileType, kind));
        return true;
    }

    public static FileSavePicker CreateSavePicker(Window owner, string suggestedName)
    {
        var picker = new FileSavePicker { SuggestedStartLocation = PickerLocationId.DocumentsLibrary, SuggestedFileName = suggestedName };
        picker.FileTypeChoices.Add("Excel Workbook", new List<string> { ".xlsx" });
        picker.FileTypeChoices.Add("CSV (UTF-8)", new List<string> { ".csv" });
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(owner));
        return picker;
    }

    public static async Task<bool> SaveTablesAsync(Window owner, string suggestedName, IReadOnlyList<SpreadsheetTable> tables)
    {
        var file = await CreateSavePicker(owner, suggestedName).PickSaveFileAsync();
        if (file == null) return false;
        await WriteFileAsync(file, stream =>
        {
            if (file.FileType.Equals(".xlsx", StringComparison.OrdinalIgnoreCase)) SpreadsheetService.WriteWorkbook(stream, tables);
            else
            {
                if (tables.Count == 0) throw new InvalidOperationException("There are no report tables.");
                var columns = new[] { new SpreadsheetColumn("Log Table") }.Concat(tables[0].Columns).ToArray();
                if (tables.Any(t => !t.Columns.SequenceEqual(tables[0].Columns))) throw new InvalidOperationException("The report tables have different columns.");
                var rows = tables.SelectMany(t => t.Rows.Select(r => new object?[] { t.Name }.Concat(r).ToArray())).ToList();
                SpreadsheetService.Write(stream, file.FileType, new("System metrics", columns, rows));
            }
        });
        return true;
    }

    public static async Task WriteFileAsync(StorageFile file, Action<Stream> write)
    {
        // Generate first so a serialization error does not truncate an existing destination.
        using var buffer = new MemoryStream();
        await Task.Run(() => write(buffer));
        CachedFileManager.DeferUpdates(file);
        try
        {
            using var stream = await file.OpenStreamForWriteAsync();
            stream.SetLength(0);
            buffer.Position = 0;
            await buffer.CopyToAsync(stream);
            await stream.FlushAsync();
        }
        catch
        {
            await CachedFileManager.CompleteUpdatesAsync(file);
            throw;
        }
        var status = await CachedFileManager.CompleteUpdatesAsync(file);
        if (status is not (FileUpdateStatus.Complete or FileUpdateStatus.CompleteAndRenamed))
            throw new IOException($"The file provider has not confirmed the save ({status}). Check the destination before retrying.");
    }
}
