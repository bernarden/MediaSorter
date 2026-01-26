namespace Vima.MediaSorter.Domain;

public class SuccessfulFileMove(string sourcePath, string destinationPath)
    : FileMove(sourcePath, destinationPath)
{ }
