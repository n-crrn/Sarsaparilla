using System;
using System.Collections.Generic;
using System.Text;

using AppliedPi.Statements;

namespace AppliedPi;

public class Parser
{
    /// <summary>
    /// The Applied Pi Code that has been provided to the Parser for parsing. The program size
    /// expected is relatively small (typically less than 1kB) so there is nothing gained from
    /// using some form of stream to read it.
    /// </summary>
    public string Code { get; init; } = "";

    private int Offset = -1;

    public Parser(string codeToParse)
    {
        Code = codeToParse;
    }

    public RowColumnPosition GetRowColumn()
    {
        int row = 1;
        int lastNextLine = 0;
        for (int i = 0; i < Offset; i++)
        {
            if (Code[i] == '\n')
            {
                row++;
                lastNextLine = i;
            }
        }
        return new(row, Offset - lastNextLine);
    }

    internal (char, char) ReadNext()
    {
        Offset++;
        if (Offset >= Code.Length)
        {
            throw new EndOfCodeException();
        }
        if (Offset == Code.Length - 1)
        {
            return (Code[Offset], '\0');
        }
        return (Code[Offset], Code[Offset + 1]);
    }

    internal char ReadNextChar()
    {
        Offset++;
        if (Offset >= Code.Length)
        {
            throw new EndOfCodeException();
        }
        return Code[Offset];
    }

    internal char ReadNextCharAfterWhiteSpace()
    {
        char current;
        do
        {
            current = ReadNextChar();
        } while (char.IsWhiteSpace(current));
        return current;
    }

    private static readonly List<char> OperatorChars = new() { ':', ';', '.', ',', '(', ')', '=', '<', '>', '[', ']', '!', '&', '|' };

    internal string PeekNextToken()
    {
        string token = ReadNextToken();
        Offset -= token.Length;
        return token;
    }

    internal string ReadNextToken()
    {
    start:
        char c = ReadNextCharAfterWhiteSpace();
        if (c == '(')
        {
            char prevC = c;
            c = ReadNextChar();
            if (c == '*')
            {
                ReadThroughComment();
                goto start; // May need to go through another comment.
            }
            else
            {
                Offset--; // Step backwards (put the latest character back for consideration).
                return prevC.ToString(); // The character '(' is an operator in and of itself.
            }
        }

        // Read until we find either white space or an operator.
        StringBuilder buffer = new();
        buffer.Append(c);
        // Condition for continuing the read changes based on whether this character is an
        // operator.
        if (OperatorChars.Contains(c))
        {
            // Only add further characters if it is possibly a multicharacter operator.
            if (c == '=' || c == '|' || c == '&' || c == '<')
            {
                c = ReadNextChar();
                while (!char.IsWhiteSpace(c) && OperatorChars.Contains(c))
                {
                    buffer.Append(c);
                    c = ReadNextChar();
                }
                Offset--;
            }
        }
        else
        {
            c = ReadNextChar();
            while (!char.IsWhiteSpace(c) && !OperatorChars.Contains(c))
            {
                buffer.Append(c);
                c = ReadNextChar();
            }
            Offset--;
        }
        return buffer.ToString();
    }

    /// <summary>
    /// This method skips a comment. When it is called, it is assumed that "(*" has already
    /// been read.
    /// </summary>
    private void ReadThroughComment()
    {
        char c, n;
        do
        {
            (c, n) = ReadNext();
            // Handle any nested comments - which are apparently allowed.
            if (c == '(' && n == '*')
            {
                Offset++; // Move beyond the '*'.
                ReadThroughComment();
            }
        } while (!(c == '*' && n == ')'));
        Offset += 2; // Move beyond the ')'.
    }

    /// <summary>
    /// This convenience method reads the next token and ensures that it is the expected
    /// one. If it is not, then an UnexpectedTokenException is raised to be caught by the
    /// loop in ReadNextStatement().
    /// </summary>
    /// <param name="expectedToken">The token that is expected.</param>
    /// <param name="statementType">
    /// The type of statement being read. This value is used to create the error message.
    /// </param>
    internal void ReadExpectedToken(string expectedToken, string statementType)
    {
        string nextToken = ReadNextToken();
        if (nextToken != expectedToken)
        {
            throw new UnexpectedTokenException(expectedToken, nextToken, statementType);
        }
    }

    /// <summary>
    /// This convenience method reads the next token and ensures that it is a valid name.
    /// If it is not, then an InvalidNameTokenException is raised to be caught by the loop
    /// in ReadNextStatement();
    /// </summary>
    /// <param name="statementType">
    /// A description of the statement currently being read. This is used to create the
    /// error message if the token is not a name.
    /// </param>
    /// <returns>The valid token read.</returns>
    internal string ReadNameToken(string statementType)
    {
        string nextToken = ReadNextToken();
        if (!IsValidName(nextToken))
        {
            throw new InvalidNameTokenException(nextToken, statementType);
        }
        return nextToken;
    }

    /// <summary>
    /// A common construct in AppliedPi is a "flat" term - that is, one where there a name, 
    /// followed by a series of names in brackets. Whereas term parsing allows an arbitrary
    /// depth of term recursion, often it is not needed and is actually contrary to the user's
    /// intention.
    /// </summary>
    /// <param name="termType">
    /// A descriptor for the statement being parsed. This value is used when creating the
    /// error messages.
    /// </param>
    /// <returns>
    /// A tuple containing the name of the term, a list of parameter names and an error
    /// message. If the parse has failed, then the first two elements will be null. If
    /// the parse succeeds, the final string will be null.
    /// </returns>
    internal (string?, List<string>?, string?) ReadRawFlatTerm(string termType)
    {
        string token = ReadNextToken();
        if (!Parser.IsValidName(token))
        {
            return (null, null, $"Expected {termType} name, instead found '{token}'.");
        }
        string name = token;
        if (ReadNextToken() != "(")
        {
            return (null, null, $"Expected '(' when parsing {termType} statement, instead found '{token}'.");
        }

        List<string> columns = new();
        do
        {
            token = ReadNextToken();
            if (!Parser.IsValidName(token))
            {
                return (null, null, $"Expected parameter name as part of {termType}, instead found '{token}'.");
            }
            columns.Add(token);
            token = ReadNextToken();
        } while (token == ",");

        if (token != ")")
        {
            return (null, null, $"Expected ')' when parsing end of {termType} statement, instead found '{token}'.");
        }
        return (name, columns, null);
    }

    /// <summary>
    /// A common construct in AppliedPi is a "flat" term - that is, one where there a name, 
    /// followed by a series of names in brackets. Whereas term parsing allows an arbitrary
    /// depth of term recursion, often it is not needed and is actually contrary to the user's
    /// intention.
    /// </summary>
    /// <param name="termType">
    /// A description of the type of statement being read. This value is used when creating the
    /// error messages.
    /// </param>
    /// <returns>
    /// A tuple containing: (0) the name of the term, and (1) the parameter names of the term.
    /// </returns>
    internal (string, List<string>) ReadFlatTerm(string termType)
    {
        string name = ReadNameToken(termType);
        ReadExpectedToken("(", termType);
        List<string> paramList = new();
        string splitToken;
        do
        {
            paramList.Add(ReadNameToken(termType));
            splitToken = ReadNextToken();
        } while (splitToken == ",");
        if (splitToken != ")")
        {
            throw new UnexpectedTokenException(")", splitToken, termType);
        }
        return (name, paramList);
    }

    /// <summary>
    /// Reads a parameter list of the form "param1: type1, param2: type2" etc.
    /// </summary>
    /// <param name="termType">
    /// A description of the type of the statement being read. This value is used when creating
    /// the error messages.
    /// </param>
    /// <returns>
    /// A tuple containing: (0) the parameter list read, (1) the next term and (2) an error
    /// message if there were problems. If the error message is null, then there was no
    /// error message and (0) and (1) are both non-null. If the error message is null,
    /// then there was the problem described in the message and (0) and (1) are both null.
    /// </returns>
    internal (SortedList<string, string>?, string?, string?) ReadParameterTypeList(string termType)
    {
        SortedList<string, string> paramList = new();
        string token;
        do
        {
            string paramName = ReadNameToken(termType);
            if (paramList.ContainsKey(paramName))
            {
                return (null, null, $"Duplicate parameter name {paramName} in {termType}.");
            }
            ReadExpectedToken(":", termType);
            paramList[paramName] = ReadNameToken(termType);
            token = ReadNextToken();
        } while (token == ",");
        return (paramList, token, null);
    }

    /// <summary>
    /// Set statements are used to configure ProVerif parameters. As this is not ProVerif, 
    /// we can ignore such statements, at least for now. Not statements are also ignored as
    /// I am not yet convinced of their utility.
    /// </summary>
    private void ReadThroughIgnoreableStatement()
    {
        // At this point, "set" should have been read.
        string token;
        do
        {
            token = ReadNextToken();
        } while (token != ".");
    }

    private static readonly List<string> Keywords = new()
    {
        "fun",
        "reduc",
        "forall",
        "free",
        "event",
        "table",
        "type",
        "query",
        "not",
        "set",
        "let",
        "process",
        "insert",
        "get",
        "new",
        "in",
        "out",
        "if",
        "then",
        "else",
        "state"
    };

    internal static bool IsValidName(string token)
    {
        // Check that the token itself makes sense.
        foreach (char c in token)
        {
            if (OperatorChars.Contains(c) || (!char.IsLetterOrDigit(c) && c != '-' && c != '_'))
            {
                return false;
            }
        }
        // Now ensure that it cannot be misinterpreted as a keyword.
        return !Keywords.Contains(token.ToLowerInvariant());
    }

    internal void SkipWhiteSpaceAndComments()
    {
        Offset++;
        while (Offset < Code.Length)
        {
            char c = Code[Offset];
            if (c == '(')
            {
                Offset++;
                if (Offset < Code.Length)
                {
                    if (Code[Offset] == '*')
                    {
                        ReadThroughComment();
                        continue;
                    }
                }
                else
                {
                    Offset--;
                }
            }
            else if (!char.IsWhiteSpace(c))
            {
                // Back off one so that the next Read* method can handle the character.
                Offset--;
                break;
            }
            Offset++;
        }
        // If Offset becomes greater than Code.Length, this is not a problem
        // as it will be caught by the next Read* method call.
    }

    public ParseResult ReadNextStatement()
    {
        SkipWhiteSpaceAndComments();
        if (Offset >= Code.Length)
        {
            return ParseResult.Finished();
        }
        try
        {
            string nextToken = ReadNextToken();
            switch (nextToken)
            {
                case "fun":
                    return ConstructorStatement.CreateFromStatement(this);
                case "reduc":
                    return DestructorStatement.CreateFromStatement(this);
                case "free":
                    return FreeStatement.CreateFromStatement(this);
                case "const":
                    return ConstantStatement.CreateFromStatement(this);
                case "event":
                    return EventStatement.CreateFromStatement(this);
                case "table":
                    return TableStatement.CreateFromStatement(this);
                case "type":
                    return TypeStatement.CreateFromStatement(this);
                case "state":
                    return StateStatement.CreateFromStatement(this);
                case "query":
                    return QueryStatement.CreateFromStatement(this);
                case "not": // Fallthrough...
                case "set":
                    ReadThroughIgnoreableStatement(); // Skip the ignoreable statement and...
                    return ReadNextStatement();       // recurse.
                case "let":
                    return LetStatement.CreateFromStatement(this);
                case "process":
                    return ProcessStatement.CreateFromStatement(this);
                default:
                    return ParseResult.Failure(this, $"The Statement parsing for {nextToken} is not implemented.");
            }
        }
        catch (UnexpectedTokenException utEx)
        {
            return ParseResult.Failure(this, utEx.Message);
        }
        catch (InvalidNameTokenException intEx)
        {
            return ParseResult.Failure(this, intEx.Message);
        }
        catch (EndOfCodeException)
        {
            return ParseResult.Failure(this, "Code ended before the statement did.");
        }
    }

}

/// <summary>
/// This exception is raised within the Parser if the end of the code is reached before it
/// is expected to end. Using this exception improves the readability of the code within
/// the Parser.
/// </summary>
internal class EndOfCodeException : Exception
{
    public EndOfCodeException() : base("End of code read.") { }
}
