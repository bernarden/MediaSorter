using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Processors;

public interface IProcessor
{
    ProcessorOptions Option { get; }
    public void Process();
}