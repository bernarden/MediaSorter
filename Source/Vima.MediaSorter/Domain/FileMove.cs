namespace Vima.MediaSorter.Domain;

public class FileMove(string sourcePath, string? destinationPath)
{
    public string SourcePath { get; } = sourcePath;
    public string? DestinationPath { get; } = destinationPath;
}
