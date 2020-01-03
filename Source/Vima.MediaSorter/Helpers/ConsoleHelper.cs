using System;

namespace Vima.MediaSorter.Helpers
{
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
                {
                    Console.WriteLine();
                }
            }

            return response;
        }

        private static ConsoleKey AskYesNoQuestionWithDefaultAnswer(string question, ConsoleKey defaultAnswer)
        {
            Console.Write(defaultAnswer == ConsoleKey.Y ? $"{question} [Y/n] " : $"{question} [y/N] ");
            ConsoleKey response = Console.ReadKey(false).Key;
            Console.WriteLine();
            if (response == ConsoleKey.Y || response == ConsoleKey.N)
            {
                return response;
            }

            return defaultAnswer;
        }
    }
}