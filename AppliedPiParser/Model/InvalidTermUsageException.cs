using System;

namespace AppliedPi.Model;

public class InvalidTermUsageException : Exception
{

    public InvalidTermUsageException(Term t, string scenario) :
        base($"Invalid use of {t}: {scenario}.")
    { }

}
