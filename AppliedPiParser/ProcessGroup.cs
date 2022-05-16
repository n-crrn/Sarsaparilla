using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using AppliedPi.Model;
using AppliedPi.Processes;

namespace AppliedPi;

public class ProcessGroup : IProcess
{
    public ProcessGroup(IEnumerable<IProcess> toBeLinked)
    {
        Processes = new(toBeLinked);
        Debug.Assert(Processes.Count > 0);
    }

    /// <summary>
    /// This single-item constructor tends to be used as a convenience for conducting tests
    /// with parallel processes.
    /// </summary>
    /// <param name="singleContained">The newly contained sub-process.</param>
    public ProcessGroup(IProcess singleContained)
    {
        Processes = new() { singleContained };
    }

    public IProcess First => Processes.First();

    public List<IProcess> Processes { get; init; }

    public IProcess TryPromote() => Processes.Count == 1 ? Processes[0] : this;

    #region IProcess implementation.

    public IProcess ResolveTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new ProcessGroup(from p in Processes select p.ResolveTerms(subs));
    }

    public IEnumerable<string> VariablesDefined()
    {
        IEnumerable<string> all = Enumerable.Empty<string>();
        foreach (IProcess p in Processes)
        {
            all = all.Concat(p.VariablesDefined());
        }
        return all;
    }

    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher)
    {
        List<IProcess> found = new();
        foreach (IProcess p in Processes)
        {
            if (matcher(p))
            {
                found.Add(p);
            }
            else
            {
                found.AddRange(p.MatchingSubProcesses(matcher));
            }
        }
        return found;
    }

    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage)
    {
        bool foundConc = false;
        foreach (IProcess subProc in Processes)
        {
            if (foundConc)
            {
                errorMessage = "Cannot return to sequential processes after entering concurrent processes.";
                return false;
            }
            if (!subProc.Check(nw, termResolver, out errorMessage))
            {
                return false;
            }
            foundConc = subProc is ParallelCompositionProcess || subProc is ReplicateProcess;
        }
        errorMessage = null;
        return true;
    }

    public IProcess Resolve(Network nw, TermResolver resolver)
    {
        if (Processes.Count == 1)
        {
            return Processes[0].Resolve(nw, resolver);
        }
        return new ProcessGroup(from p in Processes select p.Resolve(nw, resolver));
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj != null && obj is ProcessGroup pg && Processes.SequenceEqual(pg.Processes);
    }

    public override int GetHashCode() => First.GetHashCode();

    public override string ToString()
    {
        return $"{Processes.Count} sub-processes starting with {First}";
    }

    #endregion

    public string FullDescription => string.Join('\n', Processes);

    #region Applied Pi Code parsing.

    internal static (ProcessGroup? grp, string? errMsg) ReadFromParser(Parser p)
    {
        try
        {
            IProcess initialGrp = InnerReadFromParser(p);
            ProcessGroup grp = initialGrp is ProcessGroup group ? group : new ProcessGroup(initialGrp);

            // Clear out the final period - it is not a problem if this is the end of the file.
            /*try
            {
                p.ReadExpectedToken(".", "Process group");
            }
            catch (EndOfCodeException) { }*/

            return (grp, null);
        }
        catch (ProcessGroupParseException ex)
        {
            return (null, ex.Message);
        }
    }

    private static IProcess InnerReadFromParser(Parser p)
    {
        List<IProcess> processes = new();
        ParallelCompositionProcess? parallelRegister = null;
        string token;
        do
        {
            token = p.ReadNextToken();
            if (token == "|")
            {
                // Pop the last process off the end of the processes list, and create
                // a parallel register.
                if (processes.Count == 0)
                {
                    throw new ProcessGroupParseException("Parallel composition operator '|' at beginning.");
                }
                if (null == parallelRegister)
                {
                    // Swap out the last process on the list with a new parallel composition.
                    IProcess lastProcess = processes[^1];
                    processes.RemoveAt(processes.Count - 1);
                    parallelRegister = new(lastProcess);
                    processes.Add(parallelRegister!);
                }
                // Start on the next process.
                token = p.ReadNextToken();
            }
            else
            {
                parallelRegister = null;
            }

            IProcess? nextProcess = ReadNextProcess(p, token);
            if (nextProcess != null)
            {
                if (parallelRegister != null)
                {
                    parallelRegister.Add(nextProcess);
                }
                else
                {
                    processes.Add(nextProcess);
                }
            }

            try
            {
                token = p.PeekNextToken();
                if (token == ";" || token == ".")
                {
                    // Consume the end of statement tokens, with the exception of the parallel
                    // operator (which will be picked up on the next loop) and the shut-bracket
                    // operator (which will be cleaned up by the compound reader method).
                    // Leave anything else to be read on the next call of the ProcessGroup parser.
                    _ = p.ReadNextToken();
                }
            }
            catch (EndOfCodeException)
            {
                token = "."; // Trigger end of process group processing.
            }
                
        } while (token == ";" || token == "|");
        return processes.Count == 1 ? processes[0] : new ProcessGroup(processes);
    }

    private static IProcess? ReadNextProcess(Parser p, string token)
    {
        return token switch
        {
            "!" => ReadReplicateProcess(p),
            "in" => ReadInChannelProcess(p),
            "out" => ReadOutChannelProcess(p),
            "event" => ReadEventProcess(p),
            "new" => ReadNewProcess(p),
            "let" => ReadLetProcess(p),
            "if" => ReadIfProcess(p),
            "(" => ReadCompoundProcess(p),
            _ => ReadPossibleCallProcess(token, p) // Last one may return null.
        };
    }

    private static IProcess ReadReplicateProcess(Parser p)
    {
        IProcess? innerProcess = ReadNextProcess(p, p.ReadNextToken());
        if (innerProcess == null)
        {
            throw new ProcessGroupParseException("The replicate operation has to apply to a process.");
        }
        return new ReplicateProcess(innerProcess);
    }

    private static IProcess ReadInChannelProcess(Parser p)
    {
        string stmtType = "In";

        p.ReadExpectedToken("(", stmtType);
        string channelName = p.ReadNameToken(stmtType);
        p.ReadExpectedToken(",", stmtType);

        List<(string, string)> paramList = new();

        string token = p.ReadNextToken();
        if (token == "(")
        {
            do
            {
                List<string> variables = new();
                do
                {
                    variables.Add(p.ReadNameToken(stmtType));
                    token = p.ReadNextToken();
                } while (token == ",");
                if (token != ":")
                {
                    throw new UnexpectedTokenException(":", token, stmtType);
                }
                string piType = p.ReadNameToken(stmtType);
                foreach (string variableName in variables)
                {
                    paramList.Add((variableName, piType));
                }
                token = p.ReadNextToken();
            } while (token == ",");
            if (token != ")")
            {
                throw new UnexpectedTokenException(")", token, stmtType);
            }
        }
        else
        {
            // Token should hold the name of the variable.
            InvalidNameTokenException.Check(token, stmtType);
            p.ReadExpectedToken(":", stmtType);
            paramList.Add((token, p.ReadNameToken(stmtType)));
            p.ReadExpectedToken(")", stmtType);
        }
        //ReadStatementEnd(p, stmtType);

        return new InChannelProcess(channelName, paramList);
    }

    private static IProcess ReadOutChannelProcess(Parser p)
    {
        string stmtType = "Out";

        p.ReadExpectedToken("(", stmtType);
        string channelName = p.ReadNameToken(stmtType);
        p.ReadExpectedToken(",", stmtType);
        (Term sentTerm, string? maybeToken) = Term.ReadTermAndNextToken(p, stmtType);
        if (maybeToken == null)
        {
            p.ReadExpectedToken(")", stmtType);
        }
        else
        {
            UnexpectedTokenException.Check(")", maybeToken, stmtType);
        }
        //ReadStatementEnd(p, stmtType);
        return new OutChannelProcess(channelName, sentTerm);
    }

    private static IProcess ReadEventProcess(Parser p)
    {
        string stmtType = "Event (process)";
        Term t = Term.ReadTerm(p, stmtType);
        //ReadStatementEnd(p, stmtType);
        return new EventProcess(t);
    }

    private static IProcess ReadNewProcess(Parser p)
    {
        string stmtType = "New";
        string varName = p.ReadNameToken(stmtType);
        p.ReadExpectedToken(":", stmtType);
        string piType = p.ReadNameToken(stmtType);
        //ReadStatementEnd(p, stmtType);
        return new NewProcess(varName, piType);
    }

    private static LetProcess ReadLetProcess(Parser p)
    {
        string stmtType = "Let";
        TuplePattern tp = TuplePattern.ReadPatternAndNextToken(p, stmtType);
        string token = p.ReadNextToken();
        UnexpectedTokenException.Check("=", token, stmtType);

        string peekToken = p.PeekNextToken();
        ITermGenerator ifTerm;
        if (peekToken == "if")
        {
            _ = p.ReadNextToken();
            ifTerm = ReadIfTerm(p, stmtType);
        }
        else
        {
            ifTerm = Term.ReadTerm(p, stmtType);
        }
        p.ReadExpectedToken("in", stmtType);

        //IProcess? guardedProcess = ReadNextProcess(p, p.ReadNextToken());
        IProcess guardedProcess = InnerReadFromParser(p);

        IProcess? elseProcess = null;
        try
        {
            peekToken = p.PeekNextToken();
            if (peekToken == "else")
            {
                _ = p.ReadNextToken();
                //elseProcess = ReadNextProcess(p, p.ReadNextToken());
                elseProcess = InnerReadFromParser(p);
            }
        }
        catch (EndOfCodeException) { } // EOC does not matter in this case.

        return new(tp, ifTerm, guardedProcess, elseProcess);
    }

    private static IfTerm ReadIfTerm(Parser p, string stmtType)
    {
        IComparison cmp = ReadComparisonAndThen(p);
        ITermGenerator trueVal = ReadITermGenerator(p, stmtType);
        p.ReadExpectedToken("else", stmtType);
        ITermGenerator falseVal = ReadITermGenerator(p, stmtType);
        return new(cmp, trueVal, falseVal);
    }

    private static ITermGenerator ReadITermGenerator(Parser p, string stmtType)
    {
        // See if we have a nested if statement.
        string peekToken = p.PeekNextToken();
        if (peekToken == "if")
        {
            _ = p.ReadNextToken();
            return ReadIfTerm(p, stmtType);
        }
        else
        {
            return Term.ReadTerm(p, stmtType);
        }
    }

    private static IProcess ReadIfProcess(Parser p)
    {
        IComparison cmp = ReadComparisonAndThen(p);

        // Read through the guarded process.
        /*IProcess? guardedProcesses = ReadNextProcess(p, p.ReadNextToken());
        if (guardedProcesses == null)
        {
            throw new ProcessGroupParseException("Guarded process not found when parsing 'if' clause.");
        }*/
        IProcess guardedProcesses = InnerReadFromParser(p);

        // Is there an else process?
        IProcess? elseProcesses = null;
        try
        {
            string token = p.PeekNextToken();
            if (token == "else")
            {
                _ = p.ReadNextToken();
                //elseProcesses = ReadNextProcess(p, p.ReadNextToken());
                elseProcesses = InnerReadFromParser(p);
            }
        }
        catch (EndOfCodeException) { } // EOC oes not matter in this case.

        return new IfProcess(cmp, guardedProcesses, elseProcesses);
    }

    private static IComparison ReadComparisonAndThen(Parser p)
    {
        List<string> cmpTokens = new();
        string token = p.ReadNextToken();
        while (token != "then")
        {
            cmpTokens.Add(token);
            token = p.ReadNextToken();
        }
        return ComparisonParser.Parse(cmpTokens);
    }

    private static IProcess ReadCompoundProcess(Parser p)
    {
        /*(ProcessGroup? innerGrp, string? innerErrMsg) = ReadFromParser(p);
        if (innerErrMsg != null)
        {
            throw new ProcessGroupParseException(innerErrMsg);
        }*/
        // There may be a semi-colon here, let's move past it.
        //ReadStatementEnd(p, "ProcessGroup");
        IProcess innerGrp = InnerReadFromParser(p);
        p.ReadExpectedToken(")", "Bracketed");
        return innerGrp!;
    }

    private static IProcess? ReadPossibleCallProcess(string currentToken, Parser p)
    {
        if (currentToken == "!")
        {
            return null;
        }
        string stmtType = "Call";
        Term t = Term.ReadTermParameters(p, currentToken, stmtType);
        //ReadStatementEnd(p, stmtType);
        return new CallProcess(t);
    }

    /// <summary>
    /// When reading processes, a semi-colon, pipe or right bracket is used to separate
    /// processes and a full-stop is used to indicate termination. The semi-colon is
    /// not interesting to the higher-level parser, but the others are. This statement
    /// will peek ahead and skip semi-colons, leave the other tokens in place, and 
    /// throw an exception if there is any other token. End of code is ignored.
    /// </summary>
    /// <param name="p">The parser to use.</param>
    /// <param name="statementType">
    /// The type of statement being read. This value is used to create the error message.
    /// </param>
    /*private static void ReadStatementEnd(Parser p, string statementType)
    {
        string peekedToken;
        try
        {
            peekedToken = p.PeekNextToken();
        }
        catch (EndOfCodeException)
        {
            return;
        }

        if (peekedToken == ";")
        {
            _ = p.ReadNextToken();
        }
        else if (peekedToken != "." && peekedToken != "|" && peekedToken != ")")
        {
            throw new UnexpectedTokenException(". or ;", peekedToken, statementType);
        }
    }*/

    #endregion
}

internal class ProcessGroupParseException : Exception
{
    public ProcessGroupParseException(string errMsg) : base(errMsg) { }
}
