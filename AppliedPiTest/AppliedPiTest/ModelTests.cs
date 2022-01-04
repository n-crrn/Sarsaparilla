﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using AppliedPi;
using AppliedPi.Model;
using AppliedPi.Processes;

namespace AppliedPiTest;

/// <summary>
/// Instances of this class manage tests of the model building by Parser instances.
/// </summary>
[TestClass]
public class ModelTests
{
    /// <summary>
    /// Ensures that a Network can be correctly constructed from its Applied Pi Code
    /// representation.
    /// </summary>
    [TestMethod]
    public void BasicModelTest()
    {
        string testSource = "free A, B, C: channel.\n" +
            "free D: kitten [private].\n" +
            "type kitten.\n" +
            "type (* Random comment *) dog.\n" +
            "type host.\n" +
            "event beginB(host, host).\n" +
            "event endB(host, host).\n" +
            "(* Another surprise comment. *)\n" +
            "fun pk(skey): pkey.\n" +
            "fun sencrypt(bitstring,nonce): bitstring.\n" +
            "reduc forall x: bitstring, y: skey; decrypt(encrypt(x, y),y) = x.\n" +
            "table keys(host, pkey).\n" +
            "query x: host, y: host; inj-event(endB(x)) ==> inj-event(startB(x))." +
            "const c1: tag [data].";

        List<string> expectedPiTypes = new() { "bitstring", "kitten", "dog", "host" };
        List<FreeDeclaration> expectedFreeDecls = new()
        {
            new("A", "channel", false),
            new("B", "channel", false),
            new("C", "channel", false),
            new("D", "kitten", true)
        };
        List<Event> expectedEvents = new()
        {
            new("beginB", new() { "host", "host" }),
            new("endB", new() { "host", "host" })
        };
        List<Constructor> expectedConstructors = new()
        {
            new("pk", new() { "skey" }, "pkey"),
            new("sencrypt", new() { "bitstring", "nonce" }, "bitstring")
        };
        List<Destructor> expectedDestructors = new()
        {
            new(new("decrypt", new() { new("encrypt", new() { new("x"), new("y") }), new("y") }),
                "x",
                new() { { "x", "bitstring" }, { "y", "skey" } })
        };
        List<Table> expectedTables = new()
        {
            new("keys", new() { "host", "pkey" })
        };
        List<Query> expectedQueries = new()
        {
            new(new("inj-event", new() { new("endB", new() { new("x") }) }),
                new("inj-event", new() { new("startB", new() { new("x") }) }),
                new() { { "x", "host" }, { "y", "host" } })
        };
        List<Constant> expectedConstants = new() { new("c1", "tag", "data") };

        Network nw = Network.CreateFromCode(testSource);

        // Go through and check that everything matches.
        AssertListsMatch(expectedPiTypes, nw.PiTypes, "PiTypes");
        AssertListsMatch(expectedFreeDecls, nw.FreeDeclarations, "Free Declarations");
        AssertListsMatch(expectedEvents, nw.Events, "Events");
        AssertListsMatch(expectedConstructors, nw.Constructors, "Constructors");
        AssertListsMatch(expectedDestructors, nw.Destructors, "Destructors");
        AssertListsMatch(expectedTables, nw.Tables, "Tables");
        AssertListsMatch(expectedQueries, nw.Queries, "Queries");
        AssertListsMatch(expectedConstants, nw.Constants, "Contants");
    }

    /// <summary>
    /// Provides a basic check of processes.
    /// </summary>
    [TestMethod]
    public void BasicProcessModelTest()
    {
        string testSource = "process new A: key;\n" +
            "let bv = pk(A) in out(c, bv);\n" +
            "let (=xB, =pkB) = checksign(cert2, pk2) in\n" +
            "in(c, other: bitstring);\n" +
            "insert keys(A, bv).";
        Network nw = Network.CreateFromCode(testSource);
        Assert.IsNotNull(nw.MainProcess);

        // The expected processes are created after to allow for debugging of model code
        // without interference from correct creation code. For instance, a Debug.WriteLine
        // call can be added to a constructor, and we can then see how the parsing is 
        // conducting object creation.
        List<TuplePattern.Element> let2Elements = new()
        {
            new(true, "xB", null),
            new(true, "pkB", null)
        };
        List<IProcess> processes = new()
        {
            new NewProcess("A", "key"),
            new LetProcess(TuplePattern.CreateBasic(new() { "bv" }),
                           new Term("pk", new() { new Term("A") })),
            new OutChannelProcess("c", new Term("bv", new())),
            new LetProcess(new(let2Elements), new Term("checksign", new() { new Term("cert2"), new Term("pk2") })),
            new InChannelProcess("c", new() { ("other", "bitstring") }),
            new InsertTableProcess(new Term("keys", new() { new("A"), new("bv") }))
        };
        ProcessGroup expectedMain = new(new List<(IProcess, bool)>(from p in processes select (p, false)));

        try
        {
            Assert.AreEqual(expectedMain, nw.MainProcess);
        }
        catch (Exception)
        {
            DebugMainProcess(expectedMain, nw.MainProcess);
            throw;
        }
    }

    /// <summary>
    /// Tests that parallel processes can be incorporated into a larger process.
    /// </summary>
    [TestMethod]
    public void ParallelProcessModelTest()
    {
        string testSource = "process\n" +
            "out(c, bv) | out(d, bx) | out(e, bw);\n" +
            "(!in(c, x: bitstring) | in(d, y: bitstring));\n" +
            "(!out(c, x) | !in(c, x: bitstring)).";
        Network nw = Network.CreateFromCode(testSource);
        Assert.IsNotNull(nw.MainProcess);

        // Build the expected model of processes.
        ParallelCompositionProcess line1 = new(new OutChannelProcess("c", new("bv")), false);
        line1.Add(new OutChannelProcess("d", new("bx")), false);
        line1.Add(new OutChannelProcess("e", new("bw")), false);
        ParallelCompositionProcess line2 = new(new InChannelProcess("c", new() { ("x", "bitstring") }), true);
        line2.Add(new InChannelProcess("d", new() { ("y", "bitstring") }), false);
        ParallelCompositionProcess line3 = new(new OutChannelProcess("c", new("x")), true);
        line3.Add(new InChannelProcess("c", new() { ("x", "bitstring") }), true);
        List<(IProcess, bool)> expectedProcesses = new()
        {
            (line1, false),
            (new ProcessGroup(line2, false), false),
            (new ProcessGroup(line3, false), false)
        };
        ProcessGroup expectedMain = new(expectedProcesses);

        try
        {
            Assert.AreEqual(expectedMain, nw.MainProcess);
        }
        catch (Exception)
        {
            DebugMainProcess(expectedMain, nw.MainProcess);
            throw;
        }
    }

    /// <summary>
    /// Tests that comparison handling and the general "if" statement model is handled
    /// correctly.
    /// </summary>
    [TestMethod]
    public void IfProcessModelTest()
    {
        string testSource = "process\n" +
            "let h = if X = A then A else C in\n" +
            "if h <> A && h <> B then\n" +
            "  insert some_keys(h, key);" +
            "else insert some_keys(h, other_key).";
        Network nw = Network.CreateFromCode(testSource);
        Assert.IsNotNull(nw.MainProcess);

        // Build the expected model of processes.
        LetProcess line1 = new(TuplePattern.CreateSingle("h"), new IfTerm(new NameComparison(true, "X", "A"), new Term("A"), new Term("C")));
        BooleanComparison line2Cmp = new(BooleanComparison.Type.And, new NameComparison(false, "h", "A"), new NameComparison(false, "h", "B"));
        InsertTableProcess line3 = new(new Term("some_keys", new() { new("h"), new("key") }));
        InsertTableProcess line4 = new(new Term("some_keys", new() { new("h"), new("other_key") }));
        IfProcess lines2to4 = new(line2Cmp, line3, line4);
        List<(IProcess, bool)> expectedProcesses = new()
        {
            (line1, false),
            (lines2to4, false)
        };
        ProcessGroup expectedMain = new(expectedProcesses);

        try
        {
            Assert.AreEqual(expectedMain, nw.MainProcess);
        }
        catch (Exception)
        {
            DebugMainProcess(expectedMain, nw.MainProcess);
            throw;
        }
    }

    /// <summary>
    /// Check that we can define processes using let statements, and those statements can be
    /// called.
    /// </summary>
    [TestMethod]
    public void CallProcessModelTest()
    {
        string testSource = "let testProcA = in(c, A: bitstring).\n" +
            "let testProcB(pkA: key, pkB: key) = event beginTest(pkA); out(c, pkB).\n" +
            "process\n" +
            "  testProcA;\n" +
            "  testProcB(c, d).\n";
        Network nw = Network.CreateFromCode(testSource);
        Assert.IsNotNull(nw.MainProcess);

        // Build the expected model of each user defined process.

        // User defined process testProcA.
        ProcessGroup testProcAProcesses = new(new List<(IProcess, bool)>()
            {
                (new InChannelProcess("c", new List<(string, string)>() { ("A", "bitstring") }), false)
            });
        UserDefinedProcess expectedTestProcA = new("testProcA", new(), testProcAProcesses);

        // User defined process testProcB.
        ProcessGroup testProcBProcesses = new(new List<(IProcess, bool)>()
            {
                (new EventProcess(new("beginTest", new() { new("pkA") })), false),
                (new OutChannelProcess("c", new("pkB")), false)
            });
        UserDefinedProcess expectedTestProcB = new("testProcB", new() { ("pkA", "key"), ("pkB", "key") }, testProcBProcesses);

        // The main process.
        ProcessGroup expectedMain = new(new()
        {
            (new CallProcess(new("testProcA")), false),
            (new CallProcess(new("testProcB", new() { new("c"), new("d") })), false)
        });

        IReadOnlyDictionary<string, UserDefinedProcess> letDefs = nw.LetDefinitions;
        Assert.IsTrue(letDefs.ContainsKey("testProcA"), "User defined process 'testProcA' not defined.");
        Assert.IsTrue(letDefs.ContainsKey("testProcB"), "User defined process 'testProcB' not defined.");
        try
        {
            Assert.AreEqual(expectedTestProcA, letDefs["testProcA"]);
            Assert.AreEqual(expectedTestProcB, letDefs["testProcB"]);
            Assert.AreEqual(expectedMain, nw.MainProcess);
        }
        catch (Exception)
        {
            DebugUserDefinedProcess(expectedTestProcA, letDefs["testProcA"]);
            DebugUserDefinedProcess(expectedTestProcB, letDefs["testProcB"]);
            DebugMainProcess(expectedMain, nw.MainProcess);
            throw;
        }
    }

    #region Test convenience methods.

    private static void AssertListsMatch<T>(List<T> expectedList, IReadOnlyList<T> checkList, string description)
    {
        Assert.AreEqual(expectedList.Count, checkList.Count, $"List size don't match for {description}.");

        // Double check that there are no duplicates in the expectedList. This shouldn't
        // happen, but this is a test and we can use this little bit of extra assurance.
        for (int i = 0; i < expectedList.Count; i++)
        {
            T item1 = expectedList[i];
            for (int j = i + 1; j < expectedList.Count; j++)
            {
                T item2 = expectedList[j];
                Assert.AreNotEqual(item1, item2);
            }
        }
        foreach (T expected in expectedList)
        {
            Assert.IsTrue(checkList.Contains(expected), $"Item {expected} missing from {description}.");
        }
    }

    #endregion
    #region Failure condition output helpers.

    private static void DebugWriteProcessGroup(string titleLine, ProcessGroup group)
    {
        Debug.WriteLine(titleLine);
        foreach ((IProcess proc, bool replicated) in group.Processes)
        {
            Debug.Write("  ");
            if (replicated)
            {
                Debug.Write("!");
            }
            Debug.WriteLine(proc);
        }
    }

    /// <summary>
    /// Step through two given process groups, and output the first major difference.
    /// </summary>
    /// <param name="pg1">First group of the comparison.</param>
    /// <param name="pg2">Second group of the comparison.</param>
    private static void DebugDiffProcessGroups(ProcessGroup pg1, ProcessGroup pg2)
    {
        int max = Math.Min(pg1.Processes.Count, pg2.Processes.Count);
        for (int i = 0; i < max; i++)
        {
            if (!pg1.Processes[i].Equals(pg2.Processes[i]))
            {
                Debug.WriteLine("Difference found at index " + i);
                Debug.WriteLine($"  {pg1.Processes[i]} != {pg2.Processes[i]}");
                break;
            }
        }
    }

    private static void DebugMainProcess(ProcessGroup expected, ProcessGroup found)
    {
        DebugWriteProcessGroup("Expected was:", expected);
        DebugWriteProcessGroup("Found was:", found);
        DebugDiffProcessGroups(expected, found);
    }

    private static void DebugUserDefinedProcess(UserDefinedProcess expected, UserDefinedProcess found)
    {
        Debug.WriteLine($"Expected {expected}:");
        Debug.WriteLine(expected.Processes.FullDescription);
        Debug.WriteLine($"Found {found}:");
        Debug.WriteLine(found.Processes.FullDescription);
    }

    #endregion
}