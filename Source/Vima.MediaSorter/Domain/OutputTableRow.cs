namespace Vima.MediaSorter.Domain;

public class OutputTableRow(string key, string value, bool condition = true)
{
    public string Key { get; set; } = key;
    public string Value { get; set; } = value;
    public bool Condition { get; set; } = condition;
}
