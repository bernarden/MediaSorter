using System.Threading;
using System.Threading.Tasks;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Processors;

public interface IProcessor
{
    ProcessorOptions Option { get; }
    public Task Process(CancellationToken token = default);
}
