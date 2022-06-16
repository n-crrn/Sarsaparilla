using System;

using StatefulHorn.Query;

namespace StatefulHorn.Parser;

public record RuleAddedArgs(int Line, string Source, Rule? CompiledRule, string? Error);

/// <summary>
/// The interface fulfilled by ClauseCompiler. Having a separate interface allows the mocking of
/// the compiler for user interface testing purposes.
/// </summary>
public interface IClauseCompiler
{

    public event Action<IClauseCompiler>? OnReset;

    public event Action<IClauseCompiler, RuleAddedArgs>? OnRuleAddition;

    public event Action<IClauseCompiler, string>? OnError;

    public event Action<IClauseCompiler, QueryEngine?, string?>? OnComplete;

}
