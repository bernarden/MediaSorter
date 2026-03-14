using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Options;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Services;

public interface IOutputService
{
    public string LogFileName { get; }
    public void Initialize();

    string Start(string processorName);
    void Complete(string message = "");
    void Fatal(string context, Exception ex);

    void Header(string title, OutputLevel level);
    void Section(string title, OutputLevel level);
    void Subsection(string title, OutputLevel level);

    void Table(string title, IEnumerable<OutputTableRow> items, OutputLevel level);
    void List(string title, IEnumerable<string> items, OutputLevel level, int? maxItems = null);

    bool Confirm(string question, OutputLevel level, ConsoleKey defaultAnswer = ConsoleKey.N);
    int PromptForInt(string prompt, int defaultValue, int min, int max, OutputLevel level);
    TimeSpan GetVideoUtcOffsetFromUser();

    ConsoleKey ReadKey(bool intercept = true);
    string? ReadLine();
    void Write(string message, OutputLevel level);
    void WriteLine(string message, OutputLevel level);

    void ExecuteWithProgress(string label, Action<IProgress<double>> action);
    T ExecuteWithProgress<T>(string label, Func<IProgress<double>, T> action);
}

public class OutputService(IConsole console, IOptions<MediaSorterOptions> options)
    : IOutputService,
        IDisposable
{
    private readonly IConsole _console = console;
    private readonly MediaSorterOptions _options = options.Value;
    private StreamWriter? _logStreamWriter;
    private FileStream? _logFileStream;
    private string? _fullLogPath;
    private readonly Lock _lock = new();
    private bool _isAtStartOfFileLine = true;

    public string LogFileName { get; set; } = "Vima.MediaSorter.log";

    public void Initialize()
    {
        if (_logFileStream != null)
            return;

        lock (_lock)
        {
            if (_logFileStream != null)
                return;

            try
            {
                var logDir = string.IsNullOrWhiteSpace(_options.Directory)
                    ? Directory.GetCurrentDirectory()
                    : _options.Directory;

                if (!Directory.Exists(logDir))
                    Directory.CreateDirectory(logDir);

                if (string.IsNullOrEmpty(_fullLogPath))
                {
                    var timestamp = DateTime.Now.ToString(MediaSorterConstants.StandardDateFormat);
                    LogFileName = $"Vima.MediaSorter_{timestamp}.log";
                    _fullLogPath = Path.Combine(logDir, LogFileName);
                }

                _logFileStream = new FileStream(
                    _fullLogPath,
                    FileMode.Append,
                    FileAccess.Write,
                    FileShare.ReadWrite,
                    bufferSize: 4096,
                    useAsync: false
                );

                _logStreamWriter = new StreamWriter(_logFileStream, Encoding.UTF8)
                {
                    AutoFlush = true,
                };
            }
            catch (Exception ex)
            {
                _console.WriteLine(
                    $"Warning: Could not initialize log file. {ex.Message}",
                    OutputLevel.Error
                );
                _logFileStream = null;
                _logStreamWriter = null;
            }
        }
    }

    public string Start(string processorName)
    {
        WriteLine(string.Empty, OutputLevel.Info);
        Header($"Executing: {processorName}", OutputLevel.Info);
        return LogFileName;
    }

    public void Complete(string message = "")
    {
        if (!string.IsNullOrWhiteSpace(message))
        {
            WriteLine(message, OutputLevel.Info);
            WriteLine(string.Empty, OutputLevel.Info);
        }

        WriteLine(MediaSorterConstants.Separator, OutputLevel.Info);
        WriteLine(string.Empty, OutputLevel.Info);
    }

    public void Fatal(string context, Exception ex)
    {
        WriteLine(string.Empty, OutputLevel.Error);
        WriteLine(context, OutputLevel.Error);
        WriteLine($"Message: {ex.Message}", OutputLevel.Error);
        WriteLine($"Stack: {ex.StackTrace}", OutputLevel.Error);
        WriteLine($"Details logged to: {LogFileName}", OutputLevel.Error);
        WriteLine(string.Empty, OutputLevel.Error);

        WriteLine(MediaSorterConstants.Separator, OutputLevel.Info);
        WriteLine(string.Empty, OutputLevel.Info);
    }

    public void Header(string title, OutputLevel level)
    {
        WriteLine(MediaSorterConstants.Separator, level);
        WriteLine(title, level);
        WriteLine(MediaSorterConstants.Separator, level);
    }

    public void Section(string title, OutputLevel level)
    {
        WriteLine(MediaSorterConstants.TaskSeparator, level);
        WriteLine(title, level);
        WriteLine(MediaSorterConstants.TaskSeparator, level);
    }

    public void Subsection(string title, OutputLevel level)
    {
        WriteLine(MediaSorterConstants.SubTaskSeparator, level);
        WriteLine($"> {title}", level);
        WriteLine(MediaSorterConstants.SubTaskSeparator, level);
    }

    public void Table(string title, IEnumerable<OutputTableRow> items, OutputLevel level)
    {
        var visibleItems = items.Where(i => i.Condition).ToList();
        if (visibleItems.Count == 0)
            return;

        if (title != null)
        {
            WriteLine(title, level);
        }

        int maxKeyLength = visibleItems.Max(i => i.Key.Length);
        foreach (var item in visibleItems)
        {
            WriteLine($"  {item.Key.PadRight(maxKeyLength)} {item.Value}", level);
        }

        WriteLine(string.Empty, level);
    }

    public void List(
        string title,
        IEnumerable<string> items,
        OutputLevel level,
        int? maxItems = null
    )
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        if (!string.IsNullOrWhiteSpace(title))
        {
            WriteLine(title, level);
        }

        var displayItems = maxItems.HasValue ? itemList.Take(maxItems.Value) : itemList;
        foreach (var item in displayItems)
        {
            WriteLine($"  {item}", level);
        }

        if (maxItems.HasValue && itemList.Count > maxItems.Value)
        {
            int remainingCount = itemList.Count - maxItems.Value;
            _console.WriteLine($"  ... (see logs for {remainingCount} more)", level);

            foreach (var item in itemList.Skip(maxItems.Value))
            {
                LogToFile($"  {item}", level);
            }
        }
    }

    public bool Confirm(string question, OutputLevel level, ConsoleKey defaultAnswer = ConsoleKey.N)
    {
        ConsoleKey response;
        char displayChar;
        string questionWithDefault =
            $"{question} {(defaultAnswer == ConsoleKey.Y ? "[Y/n]" : "[y/N]")} ";

        while (true)
        {
            _console.Write(questionWithDefault, level);
            ConsoleKeyInfo keyInfo = _console.ReadKey(true);

            if (keyInfo.Key is ConsoleKey.Y or ConsoleKey.N or ConsoleKey.Enter)
            {
                response = keyInfo.Key == ConsoleKey.Enter ? defaultAnswer : keyInfo.Key;
                displayChar =
                    keyInfo.Key == ConsoleKey.Enter
                        ? (defaultAnswer == ConsoleKey.Y ? 'y' : 'n')
                        : keyInfo.KeyChar;

                _console.WriteLine(displayChar.ToString(), level);
                break;
            }
            _console.WriteLine(keyInfo.KeyChar.ToString(), level);
        }

        LogToFile($"{questionWithDefault}{displayChar}", level);
        return response == ConsoleKey.Y;
    }

    public int PromptForInt(string prompt, int defaultValue, int min, int max, OutputLevel level)
    {
        int result;
        string formattedPrompt = $"{prompt}: ";

        while (true)
        {
            _console.Write(formattedPrompt, OutputLevel.Info);
            string? input = _console.ReadLine()?.Trim();
            bool isDefault = string.IsNullOrEmpty(input);

            if (isDefault)
            {
                result = defaultValue;
                _console.RewriteLine($"{formattedPrompt}{result}", OutputLevel.Info);
                break;
            }

            if (
                int.TryParse(input, out int parsedValue)
                && parsedValue >= min
                && parsedValue <= max
            )
            {
                result = parsedValue;
                break;
            }
        }

        LogToFile($"{formattedPrompt}{result}", OutputLevel.Info);
        return result;
    }

    public TimeSpan GetVideoUtcOffsetFromUser()
    {
        TimeSpan defaultOffsetTimeSpan = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        string originalSign = defaultOffsetTimeSpan.TotalMinutes >= 0 ? "+" : "-";
        string formattedOffset =
            $"{originalSign}{defaultOffsetTimeSpan.Hours:00}:{defaultOffsetTimeSpan.Minutes:00}";
        StringBuilder offset = new(formattedOffset);

        DisplayOffset();

        while (true)
        {
            ConsoleKeyInfo keyInfo = _console.ReadKey(true);
            if (keyInfo.Key == ConsoleKey.Enter)
            {
                _console.WriteLine(string.Empty, OutputLevel.Info);
                break;
            }

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (offset.Length > 0)
                    offset.Remove(offset.Length - 1, 1);
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                int position = offset.Length;
                if (position < 6 && IsAllowedCharacter(keyInfo.KeyChar, position))
                    offset.Append(keyInfo.KeyChar);
            }

            DisplayOffset();
        }

        var finalOffset = ConvertOffsetToTimeSpan(offset.ToString());
        LogToFile($"UTC offset for media files: {offset}", OutputLevel.Info);
        return finalOffset;

        TimeSpan ConvertOffsetToTimeSpan(string offsetResult)
        {
            if (string.IsNullOrEmpty(offsetResult) || offsetResult.Length != 6)
                throw new ArgumentException("Invalid offset format. Must be in the format ±hh:mm");

            char signResult = offsetResult[0];
            if (signResult != '+' && signResult != '-')
                throw new ArgumentException("Offset must start with a '+' or '-' sign.");

            if (
                !int.TryParse(offsetResult.AsSpan(1, 2), out int hours)
                || !int.TryParse(offsetResult.AsSpan(4, 2), out int minutes)
            )
                throw new ArgumentException("Invalid hours or minutes in offset.");

            TimeSpan timeSpan = new(hours, minutes, 0);
            if (signResult == '-')
                timeSpan = timeSpan.Negate();
            return timeSpan;
        }

        bool IsAllowedCharacter(char ch, int position)
        {
            return position switch
            {
                0 => ch is '+' or '-',
                3 => ch == ':',
                _ => char.IsDigit(ch),
            };
        }

        void DisplayOffset()
        {
            _console.ClearCurrentLine();
            _console.Write($"UTC offset for media files: {offset}", OutputLevel.Info);
        }
    }

    public ConsoleKey ReadKey(bool intercept = true)
    {
        return _console.ReadKey(intercept).Key;
    }

    public string? ReadLine()
    {
        return _console.ReadLine();
    }

    public void Write(string message, OutputLevel level)
    {
        if (level != OutputLevel.Debug)
        {
            _console.Write(message, level);
        }

        LogToFile(message, level, false);
    }

    public void WriteLine(string message, OutputLevel level)
    {
        if (level != OutputLevel.Debug)
        {
            _console.WriteLine(message, level);
        }

        LogToFile(message ?? string.Empty, level);
    }

    public T ExecuteWithProgress<T>(string label, Func<IProgress<double>, T> action)
    {
        _console.Write($"{label}... ", OutputLevel.Info);
        var sw = Stopwatch.StartNew();
        T result;
        using (var progress = new ProgressBar(_console, OutputLevel.Info))
        {
            result = action(new Progress<double>(progress.Report));
        }
        sw.Stop();
        var duration = $"{sw.Elapsed.TotalSeconds:N1}s";
        _console.WriteLine($"Done ({duration}).", OutputLevel.Info);
        LogToFile($"{label}... Done ({duration}).", OutputLevel.Info);
        return result;
    }

    public void ExecuteWithProgress(string label, Action<IProgress<double>> action)
    {
        ExecuteWithProgress<bool>(
            label,
            p =>
            {
                action(p);
                return true;
            }
        );
    }

    private void LogToFile(string message, OutputLevel level, bool addNewLine = true)
    {
        if (_logStreamWriter == null)
            return;

        lock (_lock)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelStr = level.ToString().ToUpper().PadRight(5);
            var prefix = $"[{timestamp}] [{levelStr}] ";

            var lines = message.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                if (_isAtStartOfFileLine)
                {
                    _logStreamWriter.Write(prefix);
                }

                _logStreamWriter.Write(lines[i]);

                var isLastLineOfBatch = i == lines.Length - 1;
                if (!isLastLineOfBatch || addNewLine)
                {
                    _logStreamWriter.WriteLine();
                    _isAtStartOfFileLine = true;
                }
                else
                {
                    _isAtStartOfFileLine = false;
                }
            }
        }
    }

    public void Dispose()
    {
        _logStreamWriter?.Dispose();
        _logFileStream?.Dispose();
        GC.SuppressFinalize(this);
    }
}
