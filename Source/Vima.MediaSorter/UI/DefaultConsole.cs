using System;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.UI;

public interface IConsole
{
    bool IsOutputRedirected { get; }

    void Write(string? value);
    void WriteLine(string? value = null);

    ConsoleKeyInfo ReadKey(bool intercept);
    string? ReadLine();

    void SetColor(OutputLevel level);
    void ResetColor();

    void ClearCurrentLine();
    void RewriteLine(string value);
}

public class DefaultConsole : IConsole
{
    public bool IsOutputRedirected => Console.IsOutputRedirected;

    public void Write(string? value) => Console.Write(value);

    public void WriteLine(string? value = null) => Console.WriteLine(value);

    public ConsoleKeyInfo ReadKey(bool intercept) => Console.ReadKey(intercept);

    public string? ReadLine() => Console.ReadLine();

    public void SetColor(OutputLevel level)
    {
        if (IsOutputRedirected)
            return;

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
        }
    }

    public void ResetColor()
    {
        if (IsOutputRedirected)
            return;

        Console.ResetColor();
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

    public void RewriteLine(string value)
    {
        if (IsOutputRedirected)
            return;

        Console.SetCursorPosition(0, Console.CursorTop - 1);
        ClearCurrentLine();
        Console.WriteLine(value);
    }
}
