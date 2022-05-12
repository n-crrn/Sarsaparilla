using System;

using AppliedPi.Model;
using StatefulHorn;
using StatefulHorn.Messages;

namespace AppliedPi.Translate;

public class InvalidComparisonException : Exception
{

    public InvalidComparisonException(string msg) : base(msg) { }

    public InvalidComparisonException(Term term, PiType expectedType) :
        base($"Comparison invalid, {term} was expected to be {expectedType}.")
    { }

    public InvalidComparisonException(Term term, string explanation) :
        base($"Use of {term} in comparison invalid: {explanation}")
    { }

    public InvalidComparisonException(IMessage msg, string explanation) :
        base($"Use of {msg} in comparison invalid: {explanation}")
    { }

    public InvalidComparisonException(IMessage msg, Term destructorTerm) :
        base($"Cannot determine correspondence between {msg} and destructor term {destructorTerm}.")
    { }

    public InvalidComparisonException(FunctionMessage fMsg, Destructor d) :
        base($"Function message {fMsg} does not correspond with destructor {d}.")
    { }

    public InvalidComparisonException(IMessage lhsMsg, IMessage rhsMsg, string explanation) :
        base($"Comparison invalid between {lhsMsg} with {rhsMsg} - {explanation}")
    { }

    public InvalidComparisonException(Term lhsTerm, Term rhsTerm, string explanation) :
        base($"Comparison invalid between {lhsTerm} with {rhsTerm} - {explanation}")
    { }
}
