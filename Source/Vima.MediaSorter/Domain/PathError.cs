using System;

namespace Vima.MediaSorter.Domain;

public class PathError(string path, Exception exception)
{
    public string Path { get; } = path;
    public Exception Exception { get; } = exception;
}
