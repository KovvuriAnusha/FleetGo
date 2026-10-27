namespace FleetGo.Mobile.Tests;

/// <summary>
/// Small assertion helpers, for the cases where <c>Assert</c>'s own overloads are ambiguous or
/// unclear at the call site.
/// </summary>
internal static class AssertThat
{
    /// <summary>
    /// Asserts that a sequence contains exactly these items, in this order.
    /// <para>
    /// This exists because passing a collection expression straight to xUnit's Equal leaves
    /// the compiler to choose between its single-value and its sequence overload, and the
    /// expression converts to both. Naming the sequence comparison removes the question, and
    /// makes the intent - order matters - obvious to whoever reads the test next.
    /// </para>
    /// </summary>
    public static void Sequence<T>(IEnumerable<T> expected, IEnumerable<T> actual) =>
        Assert.Equal(expected, actual);
}
