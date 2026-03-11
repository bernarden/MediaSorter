using System;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.UI;

public interface IConsole
{
    bool IsOutputRedirected { get; }

    ConsoleKeyInfo ReadKey(bool intercept);
    string? ReadLine();

    void Write(string? value, OutputLevel level);
    void WriteLine(string? value, OutputLevel level);

    void ClearCurrentLine();
    void RewriteLine(string value, OutputLevel level);
}

public class DefaultConsole : IConsole
{
    private OutputLevel _currentLevel = OutputLevel.Info;

    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);

    public string? ReadLine() => Console.ReadLine();

    public void Write(string? value, OutputLevel level)
    {
        SetColor(level);
        Console.Write(value);
    }

    public void WriteLine(string? value, OutputLevel level)
    {
        SetColor(level);
        Console.WriteLine(value);
    }

    public void ClearCurrentLine()
    {
        if (IsOutputRedirected)
            return;

        int currentLineCursor = Console.CursorTop;
        Console.SetCursorPosition(0, currentLineCursor);
        Console.Write(new string(' ', Console.WindowWidth));
        Console.SetCursorPosition(0, currentLineCursor);
    }

    public void RewriteLine(string value, OutputLevel level)
    {
        if (IsOutputRedirected)
            return;

        Console.SetCursorPosition(0, Console.CursorTop - 1);
        ClearCurrentLine();
        WriteLine(value, level);
    }

    private void SetColor(OutputLevel level)
    {
        if (IsOutputRedirected || _currentLevel == level)
            return;

        _currentLevel = level;
        switch (level)
        {
            case OutputLevel.Warn:
                Console.ForegroundColor = ConsoleColor.Yellow;
                break;
            case OutputLevel.Error:
                Console.ForegroundColor = ConsoleColor.Red;
                break;
            case OutputLevel.Debug:
                Console.ForegroundColor = ConsoleColor.DarkGray;
                break;
            case OutputLevel.Info:
                Console.ResetColor();
                break;
        }
    }
}
