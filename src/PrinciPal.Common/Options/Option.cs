namespace PrinciPal.Common.Options;

public readonly struct Option<T> : IEquatable<Option<T>>
{
    private readonly T? _value;
    private readonly bool _hasValue;

    private Option(T value)
    {
        _value = value;
        _hasValue = true;
    }

    public bool IsSome => _hasValue;

    public bool IsNone => !_hasValue;

    public static Option<T> Some(T value)
    {
        if (value is null)
            throw new ArgumentNullException(nameof(value), "Cannot create Some with a null value. Use None instead.");

        return new Option<T>(value);
    }

    public static Option<T> None => default;

    /// <summary>
    /// Forces callers to handle both Some and None cases.
    /// This is the primary way to extract a value from an Option.
    /// </summary>
    public TResult Match<TResult>(Func<T, TResult> some, Func<TResult> none) =>
        _hasValue ? some(_value!) : none();

    /// <summary>
    /// Forces callers to handle both Some and None cases (void variant).
    /// </summary>
    public void Switch(Action<T> some, Action none)
    {
        if (_hasValue)
            some(_value!);
        else
            none();
    }

    /// <summary>
    /// Transforms the inner value if Some, otherwise returns None.
    /// </summary>
    public Option<TResult> Map<TResult>(Func<T, TResult> map) =>
        _hasValue ? Option<TResult>.Some(map(_value!)) : Option<TResult>.None;

    /// <summary>
    /// Chains an operation that itself returns an Option (flatMap).
    /// </summary>
    public Option<TResult> Bind<TResult>(Func<T, Option<TResult>> bind) =>
        _hasValue ? bind(_value!) : Option<TResult>.None;

    /// <summary>
    /// Returns the inner value if Some, otherwise returns the provided default.
    /// </summary>
    public T ValueOr(T defaultValue) =>
        _hasValue ? _value! : defaultValue;

    /// <summary>
    /// Returns the inner value if Some, otherwise evaluates the factory.
    /// </summary>
    public T ValueOr(Func<T> defaultFactory) =>
        _hasValue ? _value! : defaultFactory();

    /// <summary>
    /// Filters the Option: returns None if the predicate fails.
    /// </summary>
    public Option<T> Where(Func<T, bool> predicate) =>
        _hasValue && predicate(_value!) ? this : None;

    /// <summary>
    /// Converts to a Result, using the provided error if None.
    /// </summary>
    public Results.Result<T> ToResult(Abstractions.IError error) =>
        _hasValue
            ? Results.Result<T>.Success(_value!)
            : Results.Result<T>.Failure(error);

    public static implicit operator Option<T>(T? value) =>
        value is null ? None : Some(value);

    public bool Equals(Option<T> other) =>
        _hasValue == other._hasValue &&
        (!_hasValue || EqualityComparer<T>.Default.Equals(_value!, other._value!));

    public override bool Equals(object? obj) =>
        obj is Option<T> other && Equals(other);

    public override int GetHashCode() =>
        _hasValue ? EqualityComparer<T>.Default.GetHashCode(_value!) : 0;

    public static bool operator ==(Option<T> left, Option<T> right) =>
        left.Equals(right);

    public static bool operator !=(Option<T> left, Option<T> right) =>
        !left.Equals(right);

    public override string ToString() =>
        _hasValue ? $"Some({_value})" : "None";
}

/// <summary>
/// Non-generic helper for creating Option values.
/// </summary>
public static class Option
{
    public static Option<T> Some<T>(T value) => Option<T>.Some(value);

    public static Option<T> None<T>() => Option<T>.None;

    /// <summary>
    /// Wraps a nullable value into an Option. Null becomes None, non-null becomes Some.
    /// </summary>
    public static Option<T> From<T>(T? value) where T : class =>
        value is null ? Option<T>.None : Option<T>.Some(value);

    /// <summary>
    /// Wraps a nullable value type into an Option.
    /// </summary>
    public static Option<T> From<T>(T? value) where T : struct =>
        value.HasValue ? Option<T>.Some(value.Value) : Option<T>.None;
}
