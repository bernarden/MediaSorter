using System;

namespace Vima.MediaSorter.Domain;

public class CreatedOn(DateTime date, CreatedOnSource source)
{
    public DateTime Date { get; } = date;
    public CreatedOnSource Source { get; } = source;
}