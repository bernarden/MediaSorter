using System;

namespace Vima.MediaSorter.Domain;

public class FileIdentificationError(string filePath, Exception exception)
{
    public string FilePath { get; } = filePath;
    public Exception Exception { get; } = exception;
}