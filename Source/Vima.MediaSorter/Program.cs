using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace Vima.MediaSorter;

public class Program
{
    public static void Main()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        FileVersionInfo fileVersionInfo = FileVersionInfo.GetVersionInfo(assembly.Location);
        Console.WriteLine($"Vima MediaSorter v{fileVersionInfo.FileVersion}");

        string sourceDirectory = Directory.GetCurrentDirectory();
        MediaFileProcessor processor = new(sourceDirectory);
        processor.Process();
    }
}