using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Custodian.App.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    /// <summary>IsSelected 变化时需要联动别的计算属性时的钩子。</summary>
    protected virtual void OnSelectedChanged()
    {
    }

    protected void RaiseSelectedChanged() => OnSelectedChanged();
}
