using System;
using System.Diagnostics;
using System.Text;

namespace Vima.MediaSorter.UI;

public class ConsoleHelper
{
    public const string Separator =
        "================================================================================";
    public const string TaskSeparator =
        "--------------------------------------------------------------------------------";

    public static ConsoleKey AskYesNoQuestion(string question, ConsoleKey? defaultAnswer = null)
    {
        if (defaultAnswer == null)
        {
            return AskYesNoQuestionWithoutDefaultAnswer(question);
        }

        return AskYesNoQuestionWithDefaultAnswer(question, defaultAnswer.Value);
    }

    private static ConsoleKey AskYesNoQuestionWithoutDefaultAnswer(string question)
    {
        while (true)
        {
            Console.Write($"{question} [y/n] ");
            ConsoleKey key = Console.ReadKey(true).Key;
            if (key is ConsoleKey.Y or ConsoleKey.N)
            {
                Console.WriteLine(key == ConsoleKey.Y ? "y" : "n");
                return key;
            }

            Console.WriteLine();
        }
    }

    private static ConsoleKey AskYesNoQuestionWithDefaultAnswer(string question, ConsoleKey defaultAnswer)
    {
        while (true)
        {
            Console.Write($"{question} {(defaultAnswer == ConsoleKey.Y ? "[Y/n]" : "[y/N]")} ");
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);

            if (keyInfo.Key is ConsoleKey.Y or ConsoleKey.N or ConsoleKey.Enter)
            {
                ConsoleKey result = keyInfo.Key == ConsoleKey.Enter ? defaultAnswer : keyInfo.Key;
                char displayChar = keyInfo.Key == ConsoleKey.Enter
                    ? (defaultAnswer == ConsoleKey.Y ? 'y' : 'n')
                    : keyInfo.KeyChar;

                Console.WriteLine(displayChar);
                return result;
            }
            Console.WriteLine(keyInfo.KeyChar);
        }
    }

    public static TimeSpan GetVideoUtcOffsetFromUser()
    {
        TimeSpan offsetTimeSpan = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        string originalSign = offsetTimeSpan.TotalMinutes >= 0 ? "+" : "";
        string formattedOffset =
            $"{originalSign}{offsetTimeSpan.Hours:00}:{offsetTimeSpan.Minutes:00}";
        StringBuilder offset = new(formattedOffset);

        DisplayOffset();

        while (true)
        {
            ConsoleKeyInfo keyInfo = Console.ReadKey(true);
            if (keyInfo.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (keyInfo.Key == ConsoleKey.Backspace)
            {
                if (offset.Length > 0) offset.Remove(offset.Length - 1, 1);
            }
            else if (!char.IsControl(keyInfo.KeyChar))
            {
                int position = offset.Length;
                if (position < 6 && IsAllowedCharacter(keyInfo.KeyChar, position))
                    offset.Append(keyInfo.KeyChar);
            }

            DisplayOffset();
        }

        return ConvertOffsetToTimeSpan(offset.ToString());

        TimeSpan ConvertOffsetToTimeSpan(string offsetResult)
        {
            if (string.IsNullOrEmpty(offsetResult) || offsetResult.Length != 6)
                throw new ArgumentException("Invalid offset format. Must be in the format ±hh:mm");

            char signResult = offsetResult[0];
            if (signResult != '+' && signResult != '-')
                throw new ArgumentException("Offset must start with a '+' or '-' sign.");

            if (!int.TryParse(offsetResult.AsSpan(1, 2), out int hours) ||
                !int.TryParse(offsetResult.AsSpan(4, 2), out int minutes))
                throw new ArgumentException("Invalid hours or minutes in offset.");

            TimeSpan timeSpan = new(hours, minutes, 0);
            if (signResult == '-') timeSpan = timeSpan.Negate();
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
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write($"UTC offset for media files: {offset}");
        }
    }

    public static T ExecuteWithProgress<T>(string label, Func<IProgress<double>, T> serviceCall)
    {
        Console.Write($"{label}... ");
        var sw = Stopwatch.StartNew();
        T result;
        using (var progress = new ProgressBar())
        {
            result = serviceCall(new Progress<double>(progress.Report));
        }
        sw.Stop();
        Console.WriteLine($"Done ({sw.Elapsed.TotalSeconds:N1}s).");
        return result;
    }

    public static T PromptForEnum<T>(string prompt, T defaultValue)
        where T : struct, Enum
    {
        while (true)
        {
            Console.Write($"{prompt}: ");
            int cursorLeft = Console.CursorLeft;
            string? input = Console.ReadLine()?.Trim();
            bool isDefault = string.IsNullOrEmpty(input);
            bool isValid = !isDefault &&
                           int.TryParse(input, out int intChoice) &&
                           Enum.IsDefined(typeof(T), intChoice);
            if (isDefault || isValid)
            {
                T selected = isDefault ? defaultValue : (T)Enum.ToObject(typeof(T), int.Parse(input!));
                Console.SetCursorPosition(cursorLeft, Console.CursorTop - 1);
                Console.WriteLine($"{Convert.ToInt32(selected)}".PadRight(Console.WindowWidth - cursorLeft));
                return selected;
            }
        }
    }
}