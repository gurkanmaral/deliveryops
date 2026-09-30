using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;

namespace DeliveryOps.PosBridge.Wpf.ViewModels;

public abstract class ObservableObject : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

/// <summary>Command that receives the clicked item (a product tile, cart line or order row).</summary>
public sealed class ParameterCommand<T>(Func<T, Task> execute) : ICommand
{
    private bool _executing;
    public event EventHandler? CanExecuteChanged;
    public bool CanExecute(object? parameter) => !_executing && parameter is T;

    public async void Execute(object? parameter)
    {
        if (parameter is not T value || _executing) return;
        _executing = true; CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await execute(value); }
        finally { _executing = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }
}
