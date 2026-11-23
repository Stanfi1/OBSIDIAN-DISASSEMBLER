namespace Obsidian.Core.Model;

/// <summary>Failure of binary loading that can be shown to the user verbatim.</summary>
public sealed class BinaryLoadException : Exception
{
    public BinaryLoadException(
        BinaryLoadFailure failure,
        string message,
        Exception? innerException = null)
        : base(message, innerException)
    {
        Failure = failure;
    }

    public BinaryLoadFailure Failure { get; }
}
