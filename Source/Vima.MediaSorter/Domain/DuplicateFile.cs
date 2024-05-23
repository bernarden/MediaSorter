namespace Vima.MediaSorter.Domain;

public class DuplicateFile(string originalFilePath, string destinationFilePath)
{
    public string OriginalFilePath { get; } = originalFilePath;
    public string DestinationFilePath { get; } = destinationFilePath;
}