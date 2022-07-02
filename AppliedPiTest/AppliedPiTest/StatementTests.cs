using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

using AppliedPi;
using AppliedPi.Model;
using AppliedPi.Statements;

namespace SarsaparillaTests.AppliedPiTest;

/// <summary>
/// Instances of this class manage tests of the statement parsing by Parser instances.
/// </summary>
[TestClass]
public class StatementTests
{
    /// <summary>
    /// Test that Applied Pi Code with comments and all high level statement types except
    /// "let" and "process" can be parsed correctly.
    /// </summary>
    [TestMethod]
    public void BasicStatementTypes()
    {
        // Initialise.
        string testSource = "(* This is a test example *)\n" +
            "set ignoreTypes = false.\n" +
            "free c: channel.\n" +
            "free A, B: host [private].\n" +
            "type host.\n" +
            "fun pk(skey): pkey.\n" +
            "reduc forall x: bitstring, y: skey; decrypt(encrypt(x, pk(y)),y) = x.\n" +
            "not attacker(new skA).\n" +
            "query attacker(pkB).\n" +
            "query attacker(new pkC); attacker(pk(D)).\n" +
            "const c1: tag [data].";
        List<IStatement> expectedStatements = new()
        {
            new FreeStatement(new() { "c" }, "channel", false, null),
            new FreeStatement(new() { "A", "B" }, "host", true, null),
            new TypeStatement("host", null),
            new Constructor("pk", new() { "skey" }, "pkey", false, null),
            new Destructor(
                new("decrypt",
                    new()
                    {
                        new("encrypt", new()
                        {
                            new("x"),
                            new("pk",
                                new() { new("y") }),
                        }),
                        new("y")
                    }), 
                "x", 
                new() { { "x", "bitstring" }, { "y", "skey" } },
                null
            ),
            new QueryStatement(new List<Term>() { new("pkB") }, null),
            new QueryStatement(new List<Term>() { new("pkC"), new("pk", new() { new("D") })}, null),
            new Constant("c1", "tag", "data", null)
        };
        Parser p = new(testSource);

        // Execute and test as we go. It is most useful to have the test fail as soon as 
        // possible. Otherwise, the error investigated may be far downstream of where
        // the root cause is.
        int foundStatementsCount = 0;
        ParseResult pr = p.ReadNextStatement();
        while (!pr.AtEnd && pr.Successful)
        {
            IStatement expectedStmt = expectedStatements[foundStatementsCount];
            foundStatementsCount++;

            Assert.AreEqual(expectedStmt, pr.Statement);

            pr = p.ReadNextStatement();
        }

        if (!pr.AtEnd && !pr.Successful)
        {
            string afterMsg = $"(after {foundStatementsCount} successful statements)";
            Assert.Fail($"Error encountered at {pr.ErrorPosition} while parsing {afterMsg}: {pr.ErrorMessage}");
        }

        Assert.AreEqual(expectedStatements.Count, foundStatementsCount, "Statements read do not match expected number of statements.");
    }
}
