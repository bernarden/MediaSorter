using System;

namespace Vima.MediaSorter.Domain;

public class ErroredFileMove(string sourcePath, string? destinationPath, Exception exception)
    : FileMove(sourcePath, destinationPath)
{
    public Exception Exception { get; } = exception;
}
