using System;

namespace StatefulHorn;

public record RuleAddedArgs(int Line, string ClauseSource);

public record RuleUpdateArgs(int Line, Rule? CompiledRule, string? Error);

/// <summary>
/// The interface fulfilled by ClauseCompiler. Having a separate interface allows the mocking of
/// the compiler for user interface testing purposes.
/// </summary>
public interface IClauseCompiler
{

    public event Action<IClauseCompiler>? OnReset;

    public event Action<IClauseCompiler, RuleAddedArgs>? OnRuleAddition;

    public event Action<IClauseCompiler, RuleUpdateArgs>? OnRuleUpdate;

    public event Action<IClauseCompiler, string>? OnGeneralWarning;

    public event Action<IClauseCompiler, Universe>? OnComplete;

}
