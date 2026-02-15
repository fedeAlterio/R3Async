using System.Collections.ObjectModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Playground.Common;
using R3;
using SignalsDotnet;
using SignalsDotnet.Helpers;

namespace Playground.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var vm = new MainWindowViewModel();
        InitializeComponent();

        this.LoadedObservable()
            .Do(loaded => vm.Disconnected.Value = !loaded)
            .Subscribe();

        DataContext = vm;
    }
}

public class MainWindowViewModel
{
    readonly SignalRHubClient _hubClient = new();
    public MainWindowViewModel()
    {
        _hubClient.JoinRoom(new("MyRoom"), "AvaloniaUser", MessagesToSend.ToAsyncEnumerable())
                  .ToObservable()
                  .DisconnectWhen(Disconnected.Values)
                  .Do(AllChatMessages.Add)
                  .Subscribe();
    }
    public Signal<bool> Disconnected { get; } = new(true);
    public Subject<ChatMessage> MessagesToSend { get; } = new();
    public ObservableCollection<ChatMessage> AllChatMessages { get; } = new();
}

file static class Ex
{
    public static Observable<bool> LoadedObservable(this Control control)
    {
        return Observable.FromEventHandler<RoutedEventArgs>(x => control.Loaded += x, x => control.Loaded -= x)
                         .Merge(Observable.FromEventHandler<RoutedEventArgs>(x => control.Unloaded += x, x => control.Unloaded -= x))
                         .Select(_ => control.IsLoaded)
                         .Prepend(control.IsLoaded)
                         .DistinctUntilChanged();
    }
}