using System.Windows.Input;

namespace SysPwrMon.Services;

/// <summary>
/// Minimal ICommand so the tray menu items carry commands. H.NotifyIcon's
/// ContextFlyout builds a Win32 PopupMenu from the MenuFlyout and invokes each
/// MenuFlyoutItem's Command — not its Click event — so the items need a real
/// ICommand, not a RoutedEventHandler.
/// Trade-off: no CanExecuteChanged wiring. The tray menu never re-queries
/// CanExecute, so the event bodies are empty; the tension between a live
/// notification (correctness if CanExecute ever changed) and dead weight
/// resolves to omitting it — no state feeds the commands, so CanExecute is
/// constant.
/// </summary>
public sealed class RelayCommand : ICommand
{
    private readonly Action _execute;

    public RelayCommand(Action execute)
    {
        _execute = execute;
    }

    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => _execute();

    // Explicit interface implementation: ICommand requires the event; no
    // subscriber ever fires it here (nothing re-queries CanExecute).
    event EventHandler? ICommand.CanExecuteChanged { add { } remove { } }
}
