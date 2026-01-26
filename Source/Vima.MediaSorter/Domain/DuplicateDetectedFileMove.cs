namespace Vima.MediaSorter.Domain;

public class DuplicateDetectedFileMove(string sourcePath, string destinationPath)
    : FileMove(sourcePath, destinationPath)
{ }
