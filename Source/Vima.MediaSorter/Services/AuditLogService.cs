using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Services;

public interface IAuditLogService
{
    string Initialise();
    void WriteHeader(string title);
    void WriteBulletPoints(IEnumerable<string> items, int indentation = 0);
    void WriteLine(string text);
    void WriteError(string context, Exception ex);
}

public class AuditLogService(IOptions<MediaSorterOptions> options) : IAuditLogService
{
    private string? _logPath;

    public string Initialise()
    {
        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _logPath = Path.Combine(options.Value.Directory, $"MediaSorter_{timestamp}.log");

        File.WriteAllText(_logPath, $"=== Vima.MediaSorter Session: {DateTime.Now} ===\n\n");
        return _logPath;
    }

    public void WriteHeader(string title)
    {
        WriteLine($"\n--- [{title.ToUpper()}] ---");
    }

    public void WriteBulletPoints(IEnumerable<string> items, int indentation = 0)
    {
        var prefix = new string(' ', indentation * 2);
        using var sw = GetWriter();
        foreach (var item in items)
        {
            sw.WriteLine($"{prefix}- {item}");
        }
    }

    public void WriteLine(string text)
    {
        using var sw = GetWriter();
        sw.WriteLine(text);
    }

    public void WriteError(string context, Exception ex)
    {
        using var sw = GetWriter();
        sw.WriteLine($"\n[!] ERROR: {context}");
        sw.WriteLine($"    Message: {ex.Message}");
        sw.WriteLine($"    Stack: {ex.StackTrace}");
    }

    private StreamWriter GetWriter()
    {
        var stream = new FileStream(_logPath!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
        return new StreamWriter(stream) { AutoFlush = true };
    }
}