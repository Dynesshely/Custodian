namespace Custodian.App;

/// <summary>排障开关：CUSTODIAN_SAFE=mica,hotkey,icons 可逐项关闭原生能力，用于定位崩溃。</summary>
internal static class SafeMode
{
    private static readonly HashSet<string> Features = new(StringComparer.OrdinalIgnoreCase);

    static SafeMode()
    {
        var value = Environment.GetEnvironmentVariable("CUSTODIAN_SAFE");
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            Features.Add(part);
        }
    }

    public static bool Has(string feature) => Features.Contains(feature);

    /// <summary>从 CUSTODIAN_SAFE 里取形如 filter:文本 的参数，用于确定性 UI 自测。</summary>
    public static string? Argument(string prefix)
    {
        var value = Environment.GetEnvironmentVariable("CUSTODIAN_SAFE");
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        foreach (var part in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return part[prefix.Length..];
            }
        }

        return null;
    }
}
