namespace Custodian.App.ViewModels;

/// <summary>左栏筛选项：全部，或某个沙盒。</summary>
public sealed class RailItemViewModel : ObservableObject
{
    private string _countText = string.Empty;
    private bool _isSelected;

    private RailItemViewModel(string name, string? groupLabel, bool isAll, SandboxSectionViewModel? section)
    {
        Name = name;
        GroupLabel = groupLabel;
        IsAll = isAll;
        Section = section;
    }

    public string Name { get; }

    public string? GroupLabel { get; }

    public bool HasGroupLabel => !string.IsNullOrEmpty(GroupLabel);

    public bool IsAll { get; }

    public SandboxSectionViewModel? Section { get; }

    public string CountText
    {
        get => _countText;
        set => Set(ref _countText, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => Set(ref _isSelected, value);
    }

    public static RailItemViewModel CreateAll() => new("全部", null, true, null);

    public static RailItemViewModel CreateFor(SandboxSectionViewModel section) =>
        new(section.Name, section.HasGroupLabel ? section.GroupLabel : null, false, section);
}
