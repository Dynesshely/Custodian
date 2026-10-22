using System.Text;

namespace Custodian.Core.Ini;

public sealed record IniEntry(string Key, string Value);

public sealed class IniSection
{
    private readonly List<IniEntry> _entries = new();

    public IniSection(string name) => Name = name;

    public string Name { get; }

    public IReadOnlyList<IniEntry> Entries => _entries;

    public string? GetValue(string key)
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_entries[i].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                return _entries[i].Value;
            }
        }

        return null;
    }

    public IEnumerable<string> GetValues(string key) =>
        _entries.Where(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase)).Select(e => e.Value);

    public bool GetBool(string key, bool fallback = false) =>
        GetValue(key) is { } v ? IsTrue(v) : fallback;

    internal void Add(IniEntry entry) => _entries.Add(entry);

    private static bool IsTrue(string value) =>
        value.Trim() is "y" or "Y" or "yes" or "true" or "1";
}

/// <summary>
/// 保序、可重复键的 INI 文档模型。Sandboxie.ini 里同一个键会重复出现且顺序有意义，
/// 所以这里既不做字典化，也不做去重。
/// </summary>
public sealed class IniDocument
{
    private readonly List<IniSection> _sections = new();

    public IReadOnlyList<IniSection> Sections => _sections;

    public IniSection? Find(string name) =>
        _sections.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<IniSection> FindByPrefix(string prefix) =>
        _sections.Where(s => s.Name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

    public static IniDocument Load(string path)
    {
        var bytes = File.ReadAllBytes(path);
        return Parse(Decode(bytes));
    }

    public static IniDocument Parse(string text)
    {
        var document = new IniDocument();
        IniSection? current = null;

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');
            var trimmed = line.Trim();

            if (trimmed.Length == 0 || trimmed[0] is ';' or '#')
            {
                continue;
            }

            if (trimmed[0] == '[' && trimmed[^1] == ']')
            {
                current = new IniSection(trimmed[1..^1].Trim());
                document._sections.Add(current);
                continue;
            }

            var equals = trimmed.IndexOf('=');
            if (equals <= 0 || current is null)
            {
                continue;
            }

            var key = trimmed[..equals].Trim();
            var value = trimmed[(equals + 1)..].Trim();
            current.Add(new IniEntry(key, value));
        }

        return document;
    }

    private static string Decode(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return new UnicodeEncoding(false, false).GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return new UnicodeEncoding(true, false).GetString(bytes, 2, bytes.Length - 2);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        }

        return Encoding.UTF8.GetString(bytes);
    }
}
