using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;

using AppliedPi.Model;

namespace AppliedPi;

/// <summary>
/// A Network is a fully detailed, complete system of communicating processes. This is the 
/// model object for further analysis of a system.
/// </summary>
public class Network
{

    public Network()
    {
        _PiTypes = new() { "bitstring" };
        _FreeDeclarations = new();
        _Constants = new();
        _Tables = new();
        _Events = new();
        _Queries = new();
        _Constructors = new();
        _Destructors = new();
        _LetDefinitions = new();
    }

    public static Network CreateFromCode(string appliedPiCode)
    {
        Network nw = new();
        Parser p = new(appliedPiCode);
        ParseResult result = p.ReadNextStatement();
        while (result.Successful)
        {
            result.Statement?.ApplyTo(nw);
            result = p.ReadNextStatement();
        }

        // Check that we haven't had a failure.
        if (!result.AtEnd)
        {
            throw new NetworkCreationException(result.ErrorMessage, result.ErrorPosition!);
        }

        return nw;
    }

    #region PiType declarations.

    internal List<string> _PiTypes;

    public IReadOnlyList<string> PiTypes { get => _PiTypes.ToImmutableList(); }

    #endregion
    #region Free name declarations.

    internal List<FreeDeclaration> _FreeDeclarations;

    public IReadOnlyList<FreeDeclaration> FreeDeclarations { get => _FreeDeclarations; }

    #endregion
    #region Constant declarations.

    internal List<Constant> _Constants;

    public IReadOnlyList<Constant> Constants { get => _Constants; }

    #endregion
    #region Table declarations.

    internal List<Table> _Tables;

    public IReadOnlyList<Table> Tables { get => _Tables; }

    #endregion
    #region Event declarations.

    internal List<Event> _Events;

    public IReadOnlyList<Event> Events { get => _Events; }

    #endregion
    #region Query declarations.

    internal List<Query> _Queries;

    public IReadOnlyList<Query> Queries { get => _Queries; }

    #endregion
    #region Constructor and destructor declarations.

    internal List<Constructor> _Constructors;
    internal List<Destructor> _Destructors;

    public IReadOnlyList<Constructor> Constructors => _Constructors;

    public IReadOnlyList<Destructor> Destructors => _Destructors;

    #endregion
    #region Processes (including let statements).

    private readonly SortedList<string, UserDefinedProcess> _LetDefinitions;
    public ProcessGroup? MainProcess;

    // FIXME: Include a method to return a "compiled" process, that includes all the let declarations
    // to have the appropriate substitutions done and replaced into the MainProcess.

    internal void AddUserDefinedProcess(UserDefinedProcess proc)
    {
        _LetDefinitions[proc.Name] = proc;
    }

    public bool HasDefinitionFor(string name) => _LetDefinitions.ContainsKey(name);

    public IReadOnlyDictionary<string, UserDefinedProcess> LetDefinitions => _LetDefinitions;

    #endregion
    #region Model coherence checking.

    public (bool, string? errMsg) Validate()
    {
        // FIXME: Write me. May need to create a whole type to encapsulate returned errors.
        // A NetworkDefects class.
        throw new NotImplementedException();
    }

    #endregion
    #region Basic object overrides - important for unit testing.

    public override bool Equals(object? obj)
    {
        return obj != null &&
            obj is Network nw &&
            _PiTypes.SequenceEqual(nw._PiTypes) &&
            _FreeDeclarations.SequenceEqual(nw._FreeDeclarations) &&
            _Constants.SequenceEqual(nw._Constants) &&
            _Tables.SequenceEqual(nw._Tables) &&
            _Events.SequenceEqual(nw._Events) &&
            _Queries.SequenceEqual(nw._Queries) &&
            _Constructors.SequenceEqual(nw._Constructors) &&
            _Destructors.SequenceEqual(nw._Destructors);
    }

    public override int GetHashCode() => (Constructors, Destructors).GetHashCode();

    #endregion
}
