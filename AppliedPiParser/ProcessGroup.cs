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

    public IEnumerable<string> Terms()
    {
        foreach (IProcess subProcess in Processes)
        {
            foreach (string itg in subProcess.Terms())
            {
                yield return itg;
            }
        }
    }

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
            List<IProcess> processes = new();
            ParallelCompositionProcess? parallelRegister = null;
            string token = p.ReadNextToken();
            do
            {
                if (token == "|")
                {
                    // Pop the last process off the end of the processes list, and create
                    // a parallel register.
                    if (processes.Count == 0)
                    {
                        return (null, $"Parallel composition operator '|' at beginning.");
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

                token = p.ReadNextToken();
            } while (token != "." && token != ")");
            return (new ProcessGroup(processes), null);
        }
        catch (ProcessGroupParseException ex)
        {
            return (null, ex.Message);
        }
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
            "get" => ReadTableGetProcess(p),
            "insert" => ReadTableInsertProcess(p),
            "mutate" => ReadMutateProcess(p),
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
        p.ReadStatementEnd(stmtType);

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
        p.ReadStatementEnd(stmtType);
        return new OutChannelProcess(channelName, sentTerm);
    }

    private static IProcess ReadEventProcess(Parser p)
    {
        string stmtType = "Event (process)";
        Term t = Term.ReadTerm(p, stmtType);
        p.ReadStatementEnd(stmtType);
        return new EventProcess(t);
    }

    private static IProcess ReadNewProcess(Parser p)
    {
        string stmtType = "New";
        string varName = p.ReadNameToken(stmtType);
        p.ReadExpectedToken(":", stmtType);
        string piType = p.ReadNameToken(stmtType);
        p.ReadStatementEnd(stmtType);
        return new NewProcess(varName, piType);
    }

    private static LetProcess ReadLetProcess(Parser p)
    {
        string stmtType = "Let";
        (TuplePattern tp, string? possToken) = TuplePattern.ReadPatternAndNextToken(p, stmtType);
        string token = possToken ?? p.ReadNextToken();
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

        IProcess? guardedProcess = ReadNextProcess(p, p.ReadNextToken());
        if (guardedProcess == null)
        {
            throw new ProcessGroupParseException("A let expression cannot be the final process in a collection.");
        }

        peekToken = p.PeekNextToken();
        IProcess? elseProcess = null;
        if (peekToken == "else")
        {
            _ = p.ReadNextToken();
            elseProcess = ReadNextProcess(p, p.ReadNextToken());
        }

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
        IProcess? guardedProcesses = ReadNextProcess(p, p.ReadNextToken());
        if (guardedProcesses == null)
        {
            throw new ProcessGroupParseException("Guarded process not found when parsing 'if' clause.");
        }

        // Is there an else process?
        IProcess? elseProcesses = null;
        string token = p.PeekNextToken();
        if (token == "else")
        {
            _ = p.ReadNextToken();
            elseProcesses = ReadNextProcess(p, p.ReadNextToken());
        }

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

    private static IProcess ReadTableGetProcess(Parser p)
    {
        string stmtType = "Get table";
        string tableName = p.ReadNameToken(stmtType);
        p.ReadExpectedToken("(", stmtType);
        List<(bool, string)> matchAssignList = new();
        string token;
        do
        {
            token = p.ReadNextToken();
            bool matcher = token == "=";
            if (matcher)
            {
                token = p.ReadNameToken(stmtType);
            }
            else
            {
                InvalidNameTokenException.Check(token, stmtType);
            }
            matchAssignList.Add((matcher, token));
            token = p.ReadNextToken();
        } while (token == ",");
        UnexpectedTokenException.Check(")", token, stmtType);
        p.ReadExpectedToken("in", stmtType);
        return new GetTableProcess(tableName, matchAssignList);
    }

    private static IProcess ReadTableInsertProcess(Parser p)
    {
        string stmtType = "Insert table";
        Term t = Term.ReadTerm(p, stmtType);
        p.ReadStatementEnd(stmtType);
        return new InsertTableProcess(t);
    }

    private static IProcess ReadMutateProcess(Parser p)
    {
        string stmtType = "Mutate";
        p.ReadExpectedToken("(", stmtType);
        string stateCellName = p.ReadNameToken(stmtType);
        p.ReadExpectedToken(",", stmtType);
        (Term setTerm, string? maybeToken) = Term.ReadTermAndNextToken(p, stmtType);
        if (maybeToken == null)
        {
            p.ReadExpectedToken(")", stmtType);
        }
        else
        {
            UnexpectedTokenException.Check(")", maybeToken, stmtType);
        }
        p.ReadStatementEnd(stmtType);
        return new MutateProcess(stateCellName, setTerm);
    }

    private static IProcess ReadCompoundProcess(Parser p)
    {
        (ProcessGroup? innerGrp, string? innerErrMsg) = ReadFromParser(p);
        if (innerErrMsg != null)
        {
            throw new ProcessGroupParseException(innerErrMsg);
        }
        // There may be a semi-colon here, let's move past it.
        p.ReadStatementEnd("ProcessGroup");
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
        p.ReadStatementEnd(stmtType);
        return new CallProcess(t);
    }

    #endregion
}

internal class ProcessGroupParseException : Exception
{
    public ProcessGroupParseException(string errMsg) : base(errMsg) { }
}
