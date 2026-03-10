using System.Text.Json.Serialization;
using Vima.MediaSorter.Domain;

namespace Vima.MediaSorter.Processors;

[JsonSerializable(typeof(FindDuplicatesFile))]
internal partial class SourceGenerationContext : JsonSerializerContext
{
}