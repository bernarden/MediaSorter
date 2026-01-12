using System;
using System.CommandLine;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Vima.MediaSorter.Domain;
using Vima.MediaSorter.Processors;

namespace Vima.MediaSorter;

public class Program
{
    public static int Main(string[] args)
    {
        Option<string> directoryOption = new("--directory", "-d")
        {
            Description = "The directory to process. Defaults to the executable's launch directory."
        };

        RootCommand rootCommand = new("Sorts media files into organised folders.");
        rootCommand.Add(directoryOption);
        rootCommand.SetAction(parseResult => RunMediaSorter(parseResult.GetValue(directoryOption)));

        ParseResult parseResult = rootCommand.Parse(args);
        return parseResult.Invoke();
    }

    private static int RunMediaSorter(string? directory)
    {
        string directoryPath = string.IsNullOrWhiteSpace(directory) ? Directory.GetCurrentDirectory() : directory;
        MediaSorterSettings settings = new() { Directory = directoryPath };

        try
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
            Console.WriteLine($"Vima MediaSorter v{fileVersionInfo.FileVersion}");

            Console.WriteLine($"Processing files in: {settings.Directory}");
            new IdentifyAndSortNewMediaProcessor(settings).Process();
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