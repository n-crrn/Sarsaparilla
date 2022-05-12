using System;

using AppliedPi.Model;

namespace AppliedPi;

public class UnrecognisedTermException : Exception
{

    public UnrecognisedTermException(Term t) : base($"Term {t} not recognised.") { }

}
