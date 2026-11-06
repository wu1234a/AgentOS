using System.Text;

namespace AgentOS.AgentAdapters.Processes;

/// <summary>
/// Splits a character stream into complete lines so redaction always sees whole records.
/// A credential split across two reads therefore cannot leak as two partial log lines.
/// </summary>
internal sealed class LineBuffer(int maxLineLength = 65_536)
{
    private readonly StringBuilder _line = new();

    public bool HasPartialLine => _line.Length > 0;

    public string TakeCompletedLine()
    {
        var line = _line.ToString().TrimEnd('\r');
        _line.Clear();
        return line;
    }

    public void Append(char character)
    {
        if (_line.Length < maxLineLength)
        {
            _line.Append(character);
        }
    }
}
