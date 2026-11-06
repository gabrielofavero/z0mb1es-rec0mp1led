namespace Abi.Fixtures;

public class Box<T>
{
    public T Value;
    public event EventHandler<T>? Changed;
    public Box(T value) => Value = value;
    public T Echo(T value) => value;
    public string Echo(string value) => value;
    public U Convert<U>(T value, U fallback) => fallback;
    public ref readonly T ReadOnlyRef() => ref Value;
    public ref T MutableRef() => ref Value;
    public void Raise(T value) => Changed?.Invoke(this, value);
    public sealed class Nested { public T NestedValue = default!; }
}

public abstract class Base<T>
{
    public abstract T Transform(T value);
}

public sealed class Derived : Base<int>
{
    public override int Transform(int value) => value;
}

public static class FixtureUse
{
    public static int Exercise()
    {
        var box = new Box<int>(3);
        box.Changed += (_, _) => { };
        box.Value = box.Echo(4);
        var nested = new Box<int>.Nested { NestedValue = box.Value };
        return box.Convert(nested.NestedValue, "ok").Length + new Derived().Transform(1);
    }
}
