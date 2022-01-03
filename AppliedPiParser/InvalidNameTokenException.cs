using System;

namespace AppliedPi;

/// <summary>
/// This exception is raised if a token that is expected to be a name turns out to not be a
/// name. Using this exception improves the readability of the code within the Parser.
/// </summary>
internal class InvalidNameTokenException : Exception
{
    public InvalidNameTokenException(string foundToken, string statementType) :
        base($"Expected name token, instead found '{foundToken}' while reading {statementType} statement.")
    { }

    public static void Check(string foundToken, string statementType)
    {
        if (!Parser.IsValidName(foundToken))
        {
            throw new InvalidNameTokenException(foundToken, statementType);
        }
    }
}
