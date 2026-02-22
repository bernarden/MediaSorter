using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Services;

public interface IAuditLogService
{
    string Initialise();
    void LogHeader(string title);
    void LogBulletPoints(IEnumerable<string> items, int indentation = 0);
    void LogLine(string text);
    void LogError(string context, Exception ex);
    void Flush();
}

public class AuditLogService(IOptions<MediaSorterOptions> options) : IAuditLogService
{
    private string? _logPath;
    private readonly StringBuilder _buffer = new();

    public string Initialise()
    {
        var timestamp = DateTime.Now.ToString(MediaSorterConstants.StandardDateFormat);
        string fileName = $"Vima.MediaSorter_{timestamp}.log";
        _logPath = Path.Combine(options.Value.Directory, fileName);

        _buffer.Clear();
        _buffer.AppendLine($"=== Vima.MediaSorter Session: {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        _buffer.AppendLine();
        return fileName;
    }

    public void LogHeader(string title)
    {
        _buffer.AppendLine($"\n--- [{title.ToUpper()}] ---");
    }

    public void LogBulletPoints(IEnumerable<string> items, int indentation = 0)
    {
        var prefix = new string(' ', indentation * 2);
        foreach (var item in items)
        {
            _buffer.AppendLine($"{prefix}- {item}");
        }
    }

    public void LogLine(string text)
    {
        _buffer.AppendLine(text);
    }

    public void LogError(string context, Exception ex)
    {
        _buffer.AppendLine($"\n[!] ERROR: {context}");
        _buffer.AppendLine($"    Message: {ex.Message}");
        _buffer.AppendLine($"    Stack: {ex.StackTrace}");
    }

    public void Flush()
    {
        if (_logPath == null || _buffer.Length == 0) return;
        File.AppendAllText(_logPath, _buffer.ToString());
        _buffer.Clear();
    }
}
