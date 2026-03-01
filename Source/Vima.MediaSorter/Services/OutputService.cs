using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.UI;

namespace Vima.MediaSorter.Services;

public interface IOutputService
{
    public void Initialize();
    public string LogFileName { get; }

    string Start(string processorName);
    void Complete();
    void Fatal(string context, Exception ex);

    void Header(string title, OutputLevel level = OutputLevel.Info);
    void Table(string title, IEnumerable<OutputTableRow> items, OutputLevel level = OutputLevel.Info);
    void List(string title, IEnumerable<string> items, OutputLevel level = OutputLevel.Info);

    bool Confirm(string question, ConsoleKey defaultAnswer = ConsoleKey.N);
    T PromptForEnum<T>(string prompt, T defaultValue)
        where T : struct, Enum;
    int PromptForInt(string prompt, int defaultValue, int min, int max);
    TimeSpan GetVideoUtcOffsetFromUser();
    ConsoleKey ReadKey(bool intercept = true);
    string? ReadLine();

    T ExecuteWithProgress<T>(string label, Func<IProgress<double>, T> action);

    void WriteLine(string? message = null, OutputLevel level = OutputLevel.Info);
    void Write(string message, OutputLevel level = OutputLevel.Info);
    void Flush();
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
                _console.WriteLine($"Warning: Could not initialize log file. {ex.Message}");
                _logFileStream = null;
                _logStreamWriter = null;
            }
        }
    }

    public string Start(string processorName)
    {
        WriteLine();
        WriteLine(MediaSorterConstants.Separator);
        WriteLine($"Executing: {processorName}");
        WriteLine(MediaSorterConstants.Separator);
        return LogFileName;
    }

    public void Header(string title, OutputLevel level = OutputLevel.Info)
    {
        if (level == OutputLevel.Debug)
        {
            WriteLine(MediaSorterConstants.SubTaskSeparator, level);
            WriteLine($"> {title}", level);
            WriteLine(MediaSorterConstants.SubTaskSeparator, level);
        }
        else
        {
            WriteLine(MediaSorterConstants.TaskSeparator, level);
            WriteLine(title, level);
            WriteLine(MediaSorterConstants.TaskSeparator, level);
        }
    }

    public void Table(
        string title,
        IEnumerable<OutputTableRow> items,
        OutputLevel level = OutputLevel.Info
    )
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

        WriteLine(level: level);
    }

    public void List(string title, IEnumerable<string> items, OutputLevel level = OutputLevel.Info)
    {
        var itemList = items.ToList();
        if (itemList.Count == 0)
            return;

        if (level == OutputLevel.Debug)
        {
            if (title != null)
                Header(title, level);
            foreach (var item in itemList)
                WriteLine($"  {item}", level);
            return;
        }

        _console.SetColor(level);

        if (title != null)
            WriteLine(title, level);

        const int listLimit = 5;
        foreach (var item in itemList.Take(listLimit))
        {
            WriteLine($"  {item}", level);
        }

        if (itemList.Count > listLimit)
        {
            _console.WriteLine($"  ... (see logs for {itemList.Count - listLimit} more)");
            foreach (var item in itemList.Skip(listLimit))
            {
                LogToFile($"  {item}", level);
            }
        }
        _console.ResetColor();
    }

    public bool Confirm(string question, ConsoleKey defaultAnswer = ConsoleKey.N)
    {
        ConsoleKey response;
        char displayChar;
        string questionWithDefault =
            $"{question} {(defaultAnswer == ConsoleKey.Y ? "[Y/n]" : "[y/N]")} ";

        while (true)
        {
            _console.Write(questionWithDefault);
            ConsoleKeyInfo keyInfo = _console.ReadKey(true);

            if (keyInfo.Key is ConsoleKey.Y or ConsoleKey.N or ConsoleKey.Enter)
            {
                response = keyInfo.Key == ConsoleKey.Enter ? defaultAnswer : keyInfo.Key;
                displayChar =
                    keyInfo.Key == ConsoleKey.Enter
                        ? (defaultAnswer == ConsoleKey.Y ? 'y' : 'n')
                        : keyInfo.KeyChar;

                _console.WriteLine(displayChar.ToString());
                break;
            }
            _console.WriteLine(keyInfo.KeyChar.ToString());
        }

        LogToFile($"{questionWithDefault}{displayChar}", OutputLevel.Info);
        return response == ConsoleKey.Y;
    }

    public void Complete()
    {
        WriteLine(MediaSorterConstants.Separator);
        WriteLine();
    }

    public void Fatal(string context, Exception ex)
    {
        _console.SetColor(OutputLevel.Error);
        WriteLine($"\n[FATAL ERROR] {context}", OutputLevel.Error);
        WriteLine($"Message: {ex.Message}", OutputLevel.Error);
        WriteLine($"Stack: {ex.StackTrace}", OutputLevel.Error);
        WriteLine($"Details logged to: {LogFileName}", OutputLevel.Error);
        _console.ResetColor();

        LogToFile(
            $"[!] FATAL ERROR: {context}\n    Message: {ex.Message}\n    Stack: {ex.StackTrace}",
            OutputLevel.Error
        );
    }

    public T ExecuteWithProgress<T>(string label, Func<IProgress<double>, T> action)
    {
        _console.Write($"{label}... ");
        var sw = Stopwatch.StartNew();
        T result;
        using (var progress = new ProgressBar(_console))
        {
            result = action(new Progress<double>(progress.Report));
        }
        sw.Stop();
        var duration = $"{sw.Elapsed.TotalSeconds:N1}s";
        _console.WriteLine($"Done ({duration}).");
        LogToFile($"{label}... Done ({duration}).", OutputLevel.Info);
        return result;
    }

    public T PromptForEnum<T>(string prompt, T defaultValue)
        where T : struct, Enum
    {
        T selected;
        string formattedPrompt = $"{prompt}: ";

        while (true)
        {
            _console.Write(formattedPrompt);
            string? input = _console.ReadLine()?.Trim();
            bool isDefault = string.IsNullOrEmpty(input);

            if (isDefault)
            {
                selected = defaultValue;
                _console.RewriteLine($"{formattedPrompt}{Convert.ToInt32(selected)}");
                break;
            }

            if (int.TryParse(input, out int intChoice) && Enum.IsDefined(typeof(T), intChoice))
            {
                selected = (T)Enum.ToObject(typeof(T), intChoice);
                break;
            }
        }

        LogToFile($"{formattedPrompt}{Convert.ToInt32(selected)}", OutputLevel.Info);
        return selected;
    }

    public int PromptForInt(
        string prompt,
        int defaultValue,
        int min = int.MinValue,
        int max = int.MaxValue
    )
    {
        int result;
        string formattedPrompt = $"{prompt}: ";

        while (true)
        {
            _console.Write(formattedPrompt);
            string? input = _console.ReadLine()?.Trim();
            bool isDefault = string.IsNullOrEmpty(input);

            if (isDefault)
            {
                result = defaultValue;
                _console.RewriteLine($"{formattedPrompt}{result}");
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
                _console.WriteLine();
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
        LogToFile($"UTC offset -> {finalOffset}", OutputLevel.Info);
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
            _console.Write($"UTC offset for media files: {offset}");
        }
    }

    public void WriteLine(string? message = null, OutputLevel level = OutputLevel.Info)
    {
        if (level != OutputLevel.Debug)
        {
            _console.WriteLine(message);
        }
        LogToFile(message ?? string.Empty, level);
    }

    public void Write(string message, OutputLevel level = OutputLevel.Info)
    {
        if (level != OutputLevel.Debug)
        {
            _console.Write(message);
        }
        LogToFile(message, level);
    }

    public void Flush()
    {
        _logStreamWriter?.Flush();
    }

    public string? ReadLine()
    {
        return _console.ReadLine();
    }

    public ConsoleKey ReadKey(bool intercept = true)
    {
        return _console.ReadKey(intercept).Key;
    }

    private void LogToFile(string message, OutputLevel level)
    {
        if (_logStreamWriter == null)
            return;

        lock (_lock)
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var levelStr = level.ToString().ToUpper().PadRight(5);

            var lines = message.Split(["\r\n", "\r", "\n"], StringSplitOptions.None);
            foreach (var line in lines)
            {
                _logStreamWriter.WriteLine($"[{timestamp}] [{levelStr}] {line}");
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
