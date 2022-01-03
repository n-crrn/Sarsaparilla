using System;

namespace AppliedPi;

/// <summary>
/// This exception is raised within the Parser if a token different to the expected one is
/// found. Using this exception improves the readability of the code within the Parser.
/// </summary>
internal class UnexpectedTokenException : Exception
{
    public UnexpectedTokenException(string expectedToken, string foundToken, string statementType) :
        base($"Expected '{expectedToken}', instead found '{foundToken}' while reading {statementType} statement.")
    { }

    /// <summary>
    /// Throws an UnexpectedTokenException if the foundToken does not match the expectedToken.
    /// </summary>
    /// <param name="expectedToken">Which value we expected.</param>
    /// <param name="foundToken">The value found.</param>
    /// <param name="statementType">A description of the larger lexical construct being parsed.</param>
    public static void Check(string expectedToken, string? foundToken, string statementType)
    {
        if (expectedToken != foundToken)
        {
            string dispToken = foundToken ?? "␀";
            throw new UnexpectedTokenException(expectedToken, dispToken, statementType);
        }
    }
}
