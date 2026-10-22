using System.IO;
using System.Text;

namespace Custodian.IconForge;

internal static class Program
{
    private const string Tool = "IconForge";

    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            (string command, CliOptions options) = CliOptions.Parse(args);
            return command switch
            {
                "generate" => Commands.Generate(options),
                "inspect" => Commands.Inspect(options),
                "preview" => Commands.Preview(options),
                "verify" => Commands.Verify(options),
                "logo-v2" => LogoSheets.Run(options),
                "logo-a2" => LogoA2Sheets.Run(options),
                "logo-a5" => LogoA5Sheets.Run(options),
                "logo-a4c" => LogoA4cSheets.Run(options),
                "a4c-probe" => LogoA4cProbe.Run(options),
                "final-sheets" => FinalSheets.Run(options),
                "v3" => V3Sheets.Run(options),
                "v3b" => V3bSheets.Run(options),
                "v3c" => V3bSheets.Run(options),
                "v3d" => V3bSheets.Run(options),
                "" or "help" or "-h" or "--help" => Commands.Help(),
                _ => Commands.Unknown(command),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[{Tool}] 失败: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.StackTrace);
            return 2;
        }
    }
}

/// <summary>Very small "--key value" / "flag" parser; no external dependencies.</summary>
internal sealed class CliOptions
{
    private readonly Dictionary<string, string> values = new(StringComparer.OrdinalIgnoreCase);

    public List<string> Positional { get; } = new();

    public string? Get(string key) => values.TryGetValue(key, out string? value) ? value : null;

    public bool Has(string key) => values.ContainsKey(key);

    public static (string Command, CliOptions Options) Parse(string[] args)
    {
        var options = new CliOptions();
        string command = string.Empty;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                string key = arg[2..];
                if (i + 1 < args.Length && !args[i + 1].StartsWith("-", StringComparison.Ordinal))
                {
                    options.values[key] = args[++i];
                }
                else
                {
                    options.values[key] = "1";
                }
            }
            else if (command.Length == 0)
            {
                command = arg;
            }
            else
            {
                options.Positional.Add(arg);
            }
        }

        return (command, options);
    }
}