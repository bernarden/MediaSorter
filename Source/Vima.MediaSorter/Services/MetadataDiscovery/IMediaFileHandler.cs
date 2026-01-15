using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Services.MetadataDiscovery;

public interface IMediaFileHandler
{
    bool CanHandle(string extension);

    MediaFile Handle(string filePath);
}
