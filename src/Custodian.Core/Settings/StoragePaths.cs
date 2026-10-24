namespace Custodian.Core.Settings;

/// <summary>当前进程使用的配置档案。</summary>
public enum StorageProfile
{
    /// <summary>正式实例：读写 <c>~/.custodian</c>。</summary>
    Production,

    /// <summary>开发实例（--debug / CUSTODIAN_DEBUG=1）：读正式配置，写临时目录，绝不改动正式配置。</summary>
    Development,
}

/// <summary>
/// 配置存放位置。
///
/// 正式实例：<c>%USERPROFILE%.custodiansettings.json</c>。
/// 开发实例：**读**正式实例的配置（这样调试就是真实设置），**写**到 <c>%TEMP%custodian-dev</c>，
/// 避免调试过程污染正式配置。
/// </summary>
public static class StoragePaths
{
    public const string FolderName = ".custodian";
    public const string SettingsFileName = "settings.json";

    /// <summary>正式配置目录：~/.custodian。</summary>
    public static string ProductionRoot { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        FolderName);

    /// <summary>开发实例的写入目录：%TEMP%custodian-dev。</summary>
    public static string DevelopmentRoot { get; } = Path.Combine(Path.GetTempPath(), "custodian-dev");

    /// <summary>当前进程使用的档案（默认正式）。</summary>
    public static StorageProfile Profile { get; private set; } = StorageProfile.Production;

    public static void Initialize(StorageProfile profile) => Profile = profile;

    public static bool IsDevelopment => Profile == StorageProfile.Development;

    /// <summary>读取配置的路径：始终是正式配置，开发实例也不会改它。</summary>
    public static string ReadSettingsPath => Path.Combine(ProductionRoot, SettingsFileName);

    /// <summary>写入配置的路径：正式实例写正式目录，开发实例写临时目录。</summary>
    public static string WriteSettingsPath => Path.Combine(
        Profile == StorageProfile.Development ? DevelopmentRoot : ProductionRoot,
        SettingsFileName);

    /// <summary>诊断用：一句话描述当前配置布局。</summary>
    public static object Describe() => new
    {
        profile = Profile.ToString(),
        productionRoot = ProductionRoot,
        developmentRoot = DevelopmentRoot,
        readPath = ReadSettingsPath,
        writePath = WriteSettingsPath,
    };
}
