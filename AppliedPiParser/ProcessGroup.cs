using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;

using AppliedPi.Model;
using AppliedPi.Processes;

namespace AppliedPi;

public class ProcessGroup : IProcess
{
    public ProcessGroup(List<(IProcess, bool)> toBeLinked)
    {
        Debug.Assert(toBeLinked.Count > 0);
        Processes = toBeLinked;
        for (int i = 1; i < Processes.Count; i++)
        {
            Processes[i - 1].Process.Next = Processes[i].Process;
        }
        //RequiredNames = CollectRequiredNames();
    }

    /// <summary>
    /// This single-item constructor tends to be used as a convenience for conducting tests
    /// with parallel processes.
    /// </summary>
    /// <param name="singleContained">The newly contained sub-process.</param>
    /// <param name="replicated">Whether that sub-process is replicated.</param>
    public ProcessGroup(IProcess singleContained, bool replicated)
    {
        Processes = new() { (singleContained, replicated) };
        // No need to link if there is only one.
    }

    public IProcess First => Processes[0].Process;

    public List<(IProcess Process, bool Replicated)> Processes { get; init; }

    #region Basic object overrides - important for unit testing.

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
    #region IProcess implementation (excluding ToString())

    public IProcess? Next { get; set; }

    #endregion

    public void SubstituteVariables(List<(string, string)> substitutions)
    {
        // FIXME: Write me.
        throw new NotImplementedException();
    }

    public string FullDescription
    {
        get
        {
            StringBuilder builder = new();
            foreach ((IProcess p, bool replicated) in Processes)
            {
                if (replicated)
                {
                    builder.Append('!');
                }
                builder.Append(p.ToString());
                builder.Append('\n');
            }
            return builder.ToString();
        }
    }

    #region Applied Pi Code parsing.

    internal static (ProcessGroup? grp, string? errMsg) ReadFromParser(Parser p)
    {
        try
        {
            List<(IProcess, bool)> processes = new();
            ParallelCompositionProcess? parallelRegister = null;
            bool replicated = false;
            string token = p.ReadNextToken();
            do
            {
                if (!replicated)
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
                            (IProcess lastProcess, bool lastReplicated) = processes[^1];
                            processes.RemoveAt(processes.Count - 1);
                            parallelRegister = new(lastProcess, lastReplicated);
                            processes.Add((parallelRegister!, false));
                        }
                        // Start on the next process.
                        token = p.ReadNextToken();
                    }
                    else
                    {
                        parallelRegister = null;
                    }
                }
                IProcess? nextProcess = token switch
                {
                    "in" => ReadInChannelProcess(p),
                    "out" => ReadOutChannelProcess(p),
                    "event" => ReadEventProcess(p),
                    "new" => ReadNewProcess(p),
                    "let" => ReadLetProcess(p),
                    "if" => ReadIfProcess(p),
                    "get" => ReadTableGetProcess(p),
                    "insert" => ReadTableInsertProcess(p),
                    "(" => ReadCompoundProcess(p),
                    _ => ReadPossibleCallProcess(token, p) // Last one may return null.
                };
                if (nextProcess != null)
                {
                    if (parallelRegister != null)
                    {
                        parallelRegister.Add(nextProcess, replicated);
                    }
                    else
                    {
                        processes.Add((nextProcess, replicated));
                    }
                }
                replicated = token == "!";
                token = p.ReadNextToken();
            } while (token != "." && token != ")");
            return (new ProcessGroup(processes), null);
        }
        catch (ProcessGroupParseException ex)
        {
            return (null, ex.Message);
        }
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
        LetProcess process;
        if (peekToken == "if")
        {
            _ = p.ReadNextToken();
            process = new(tp, ReadIfTerm(p, stmtType));
        }
        else
        {
            Term value = Term.ReadTerm(p, stmtType);
            process = new(tp, value);
        }
        //UnexpectedTokenException.Check("in", token, stmtType);
        p.ReadExpectedToken("in", stmtType);
        return process;
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
        ProcessGroup guardedProcesses = ReadIfSubProcess(p);

        // Is there an else process?
        ProcessGroup? elseProcesses = null;
        string token = p.PeekNextToken();
        if (token == "else")
        {
            _ = p.ReadNextToken();
            elseProcesses = ReadIfSubProcess(p);
        }

        return new IfProcess(cmp, guardedProcesses.First, elseProcesses?.First);
    }

    private static ProcessGroup ReadIfSubProcess(Parser p)
    {
        List<(IProcess, bool)> processes = new();
        string token = p.ReadNextToken();
        while (token == "let")
        {
            processes.Add((ReadLetProcess(p), false));
            token = p.ReadNextToken();
        }
        bool replicated = token == "!";
        if (replicated)
        {
            token = p.ReadNextToken();
        }
        IProcess? guardedProcess = token switch
        {
            "in" => ReadInChannelProcess(p),
            "out" => ReadOutChannelProcess(p),
            "event" => ReadEventProcess(p),
            "if" => ReadIfProcess(p),
            "get" => ReadTableGetProcess(p),
            "insert" => ReadTableInsertProcess(p),
            _ => null
        };
        if (guardedProcess == null)
        {
            throw new UnexpectedTokenException("in, out, event, if, get or insert", token, "if");
        }
        processes.Add((guardedProcess, replicated));
        return new(processes);
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
