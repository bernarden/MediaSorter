using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Processors;

public interface IProcessor
{
    ProcessorOption Option { get; }
    public void Process();
}