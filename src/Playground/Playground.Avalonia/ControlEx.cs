using Avalonia.Controls;
using Avalonia.Interactivity;
using R3;

namespace Playground.Avalonia;

public static class ControlEx
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