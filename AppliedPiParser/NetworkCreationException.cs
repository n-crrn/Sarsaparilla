using System;

namespace AppliedPi;

/// <summary>
/// This exception is thrown when a Network cannot be created due to a flaw encountered in 
/// the Applied Pi Code used to create it.
/// </summary>
public class NetworkCreationException : Exception
{
    /// <summary>
    /// This is the constructor for when creation fails due to a syntax error.
    /// </summary>
    /// <param name="msg">Error message from the parser.</param>
    /// <param name="posn">The position where the error occurred.</param>
    public NetworkCreationException(string msg, RowColumnPosition posn) : base($"Error at {posn}: {msg}") { }

    /// <summary>
    /// This constructor is used when the creation fails due to a lack of coherence.
    /// </summary>
    /// <param name="msg">A description of the flaw found.</param>
    public NetworkCreationException(string msg) : base(msg) { }
}
