namespace Vima.MediaSorter.Domain;

public class DuplicateFile
{
    public DuplicateFile(string originalFilePath, string destinationFilePath)
    {
        OriginalFilePath = originalFilePath;
        DestinationFilePath = destinationFilePath;
    }

    public string OriginalFilePath { get; }
    public string DestinationFilePath { get; }
}