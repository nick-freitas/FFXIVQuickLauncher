using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace XIVLauncher.Mac.ViewModels;

public sealed class AsyncCommand : ICommand
{
    private readonly Func<Task> execute;
    private readonly Func<bool>? canExecute;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
    {
        this.execute = execute;
        this.canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public bool CanExecute(object? parameter) => this.canExecute?.Invoke() ?? true;

    public async void Execute(object? parameter)
        => await this.execute();

    public void RaiseCanExecuteChanged()
        => this.CanExecuteChanged?.Invoke(this, EventArgs.Empty);
}
