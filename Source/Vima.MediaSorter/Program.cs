using System.IO;

namespace Vima.MediaSorter;

public class Program
{
    public static void Main()
    {
        string sourceDirectory = Directory.GetCurrentDirectory();
        MediaFileProcessor processor = new(sourceDirectory);
        processor.Process();
    }
}