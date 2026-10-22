using System.Windows;
using System.Windows.Controls;
using Custodian.App.ViewModels;

namespace Custodian.App.Controls;

/// <summary>
/// 沙盒卡片区的模板选择器：普通应用用 <c>AppCardTemplate</c>，
/// 「标准应用程序」文件夹块（紧凑视图）用 <c>FolderCardTemplate</c>。
/// 两者同在一个 WrapPanel 里流动，所以文件夹块天然排在应用列表最后。
/// </summary>
public sealed class CardItemTemplateSelector : DataTemplateSelector
{
    public DataTemplate? AppTemplate { get; set; }

    public DataTemplate? FolderTemplate { get; set; }

    public override DataTemplate? SelectTemplate(object item, DependencyObject container) =>
        item is StandardFolderItem ? FolderTemplate : AppTemplate;
}
