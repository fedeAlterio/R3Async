

using System.Reactive.Linq;
using System.Reactive.Subjects;

var source = new Subject<int>();
var other = new Subject<string>();

var takeUntil = source.TakeUntil(other);

using var subscription = takeUntil.Subscribe(x => Console.WriteLine(x),
                                                   ex => { },
                                                   () => Console.WriteLine("completed"));

source.OnNext(1);
other.OnCompleted();
source.OnNext(2);

Console.ReadLine();