using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using AppliedPi.Model;
using AppliedPi.Processes;

namespace AppliedPi;

public class ProcessGroup : IProcess
{
    public ProcessGroup(IEnumerable<IProcess> toBeLinked, RowColumnPosition? definedAt)
    {
        Processes = new(toBeLinked);
        DefinedAt = definedAt;
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

    public IProcess SubstituteTerms(IReadOnlyDictionary<string, string> subs)
    {
        return new ProcessGroup(from p in Processes select p.SubstituteTerms(subs), DefinedAt);
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
        return new ProcessGroup(from p in Processes select p.Resolve(nw, resolver), DefinedAt);
    }

    public RowColumnPosition? DefinedAt { get; private init; }

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
            return (grp, null);
        }
        catch (ProcessGroupParseException ex)
        {
            return (null, ex.Message);
        }
    }

    private static IProcess InnerReadFromParser(Parser p)
    {
        RowColumnPosition startPos = p.GetRowColumn();
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
                    parallelRegister = new(lastProcess, lastProcess.DefinedAt);
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
        return processes.Count == 1 ? processes[0] : new ProcessGroup(processes, startPos);
    }

    private static IProcess? ReadNextProcess(Parser p, string token)
    {
        return token switch
        {
            "!" => ReadReplicateProcess(p),
            "in" => ReadInChannelProcess(p),
            "out" => ReadOutChannelProcess(p),
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
        return new ReplicateProcess(innerProcess, innerProcess.DefinedAt);
    }

    private static IProcess ReadInChannelProcess(Parser p)
    {
        string stmtType = "In";
        RowColumnPosition pos = p.GetRowColumn();

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

        return new InChannelProcess(channelName, paramList, pos);
    }

    private static IProcess ReadOutChannelProcess(Parser p)
    {
        string stmtType = "Out";
        RowColumnPosition pos = p.GetRowColumn();
        p.ReadExpectedToken("(", stmtType);
        string channelName = p.ReadNameToken(stmtType);
        p.ReadExpectedToken(",", stmtType);
        Term sentTerm = Term.ReadTerm(p, stmtType);
        p.ReadExpectedToken(")", stmtType);
        return new OutChannelProcess(channelName, sentTerm, pos);
    }

    private static IProcess ReadNewProcess(Parser p)
    {
        string stmtType = "New";
        RowColumnPosition pos = p.GetRowColumn();
        string varName = p.ReadNameToken(stmtType);
        p.ReadExpectedToken(":", stmtType);
        string piType = p.ReadNameToken(stmtType);
        return new NewProcess(varName, piType, pos);
    }

    private static LetProcess ReadLetProcess(Parser p)
    {
        RowColumnPosition pos = p.GetRowColumn();
        string stmtType = "Let";
        TuplePattern tp = TuplePattern.ReadPattern(p, stmtType);
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

        IProcess guardedProcess = InnerReadFromParser(p);

        IProcess? elseProcess = null;
        try
        {
            peekToken = p.PeekNextToken();
            if (peekToken == "else")
            {
                _ = p.ReadNextToken();
                elseProcess = InnerReadFromParser(p);
            }
        }
        catch (EndOfCodeException) { } // EOC does not matter in this case.

        return new(tp, ifTerm, guardedProcess, elseProcess, pos);
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
        RowColumnPosition pos = p.GetRowColumn();

        // Read through the guarded process.
        IProcess guardedProcesses = InnerReadFromParser(p);

        // Is there an else process?
        IProcess? elseProcesses = null;
        try
        {
            string token = p.PeekNextToken();
            if (token == "else")
            {
                _ = p.ReadNextToken();
                elseProcesses = InnerReadFromParser(p);
            }
        }
        catch (EndOfCodeException) { } // EOC oes not matter in this case.

        return new IfProcess(cmp, guardedProcesses, elseProcesses, pos);
    }

    private static IComparison ReadComparisonAndThen(Parser p)
    {
        // Working out "what is a tuple?", "what is a name?", "what is an operator?" etc is a big
        // headache. So we simply rip everything up into parts, and then try to reassemble
        // in a sensible fashion.

        List<string> tkns = new();
        string token = p.ReadNextToken();
        while (token != "then")
        {
            tkns.Add(token);
            token = p.ReadNextToken();
        }
        return ComparisonParser.Parse(tkns);
    }

    private static IProcess ReadCompoundProcess(Parser p)
    {
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
        RowColumnPosition? definedAt = p.GetRowColumn();
        string stmtType = "Call";
        Term t = Term.ReadTermParameters(p, currentToken, stmtType);
        return new CallProcess(t, definedAt);
    }

    #endregion
}

internal class ProcessGroupParseException : Exception
{
    public ProcessGroupParseException(string errMsg) : base(errMsg) { }
}
