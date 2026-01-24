using System;
using System.Text;

namespace Vima.MediaSorter.UI;

public class ConsoleHelper
{
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
        ConsoleKey response = ConsoleKey.Enter;
        while (response != ConsoleKey.Y && response != ConsoleKey.N)
        {
            Console.Write($"{question} [y/n] ");
            response = Console.ReadKey(false).Key;
            if (response != ConsoleKey.Enter)
                Console.WriteLine();
        }

        return response;
    }

    private static ConsoleKey AskYesNoQuestionWithDefaultAnswer(string question, ConsoleKey defaultAnswer)
    {
        Console.Write(defaultAnswer == ConsoleKey.Y ? $"{question} [Y/n] " : $"{question} [y/N] ");
        ConsoleKey response = Console.ReadKey(false).Key;
        Console.WriteLine();
        return response is ConsoleKey.Y or ConsoleKey.N ? response : defaultAnswer;
    }

    public static TimeSpan GetVideoUtcOffsetFromUser()
    {
        TimeSpan offsetTimeSpan = TimeZoneInfo.Local.GetUtcOffset(DateTime.Now);
        string originalSign = offsetTimeSpan.TotalMinutes >= 0 ? "+" : "";
        string formattedOffset = $"{originalSign}{offsetTimeSpan.Hours:00}:{offsetTimeSpan.Minutes:00}";
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
                _ => char.IsDigit(ch)
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
}