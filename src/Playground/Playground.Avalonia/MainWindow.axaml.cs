using System.Collections.ObjectModel;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Playground.Common;
using R3;
using SignalsDotnet;

namespace Playground.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        var viewModel = new MainWindowViewModel();
        InitializeComponent();

        this.LoadedObservable()
            .Do(loaded => viewModel.ChatViewModel.Disconnected.Value = !loaded)
            .Subscribe();

        DataContext = viewModel;
    }
}

public class MainWindowViewModel
{
    public MainWindowViewModel()
    {
        UserSettingsViewModel = new UserSettingsViewModel();
        ChatViewModel = new ChatViewModel(UserSettingsViewModel.UserName);
    }

    public ChatViewModel ChatViewModel { get; }
    public UserSettingsViewModel UserSettingsViewModel { get; }
}

public class ChatViewModel
{
    readonly SignalRHubClient _hubClient = new();

    public ChatViewModel(IReadOnlySignal<string> userName)
    {
        var computedFactory = ComputedSignalFactory.Default
                                                   .DisconnectEverythingWhen(Disconnected.Values);
    
        SendCommand = computedFactory.ComputedObservable(() => _hubClient.IsConnected.Value && !string.IsNullOrWhiteSpace(DraftMessage.Value), 
                                                         () => new(false))
                                     .ToReactiveCommand(_ => ToSendMessage.OnNext(new(userName.Value, DraftMessage.Value)));
        
        computedFactory.AsyncEffect(async token =>
        {
            await foreach (var message in _hubClient.GetChatMessages(new(RoomName.Value), token))
            {
                AllChatMessages.Add(message);
            }
        });

        computedFactory.AsyncEffect(async token =>
        {
            await _hubClient.JoinRoom(new(RoomName.Value), userName.Value, ToSendMessage.TakeUntil(token)
                                                                                        .ToAsyncEnumerable(), token);
        }, ConcurrentChangeStrategy.CancelCurrent);

        ToSendMessage.Subscribe(_ => DraftMessage.Value = "");
    }

    public Signal<bool> Disconnected { get; } = new(true);
    public Signal<string> DraftMessage { get; } = new();
    public Subject<ChatMessage> ToSendMessage { get; } = new();
    public Signal<string> RoomName { get; } = new("MyRoom");
    public ObservableCollection<ChatMessage> AllChatMessages { get; } = new();
    public ICommand SendCommand { get; }
}

public class UserSettingsViewModel
{
    public Signal<string> UserName { get; } = new("AvaloniaUser");
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