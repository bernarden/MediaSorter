using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Vima.MediaSorter;

public class Program
{
    public static int Main(string[] args)
    {
        Option<string> directoryOption = new("--directory", "-d")
        {
            Description = "The directory to process. Defaults to the executable's launch directory."
        };
        Option<bool> simulateOption = new("--simulate")
        {
            Description = "If true, only simulate the process without actual file operations."
        };

        RootCommand rootCommand = new("Sorts media files into organised folders.");
        rootCommand.Add(directoryOption);
        rootCommand.Add(simulateOption);
        rootCommand.SetAction(parseResult => RunMediaSorter(
            parseResult.GetValue(directoryOption),
            parseResult.GetValue(simulateOption))
        );

        ParseResult parseResult = rootCommand.Parse(args);
        return parseResult.Invoke();
    }

    private static int RunMediaSorter(string? directory, bool simulate)
    {
        string directoryPath = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
        MediaSorterSettings settings = new() { Directory = directoryPath, SimulateMode = simulate };

        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            Console.WriteLine($"Vima MediaSorter v{fileVersionInfo.FileVersion}");

            Console.WriteLine($"Processing files in: {settings.Directory}");
            MediaFileProcessor processor = new(settings);
            processor.Process();
            return 0;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Configuration Error: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"An unhandled error occurred: {ex.Message}");
            return 1;
        }
    }
}