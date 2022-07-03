using StatefulHorn;

namespace AppliedPi.Translate;

/// <summary>
/// Provides a high-level abstraction of a rule created expressly for the purpose of expressing
/// rules that change the state of, or rely on the state of, a translated Pi model. This
/// abstraction was especially valuable in allowing for the creation of unit tests for the
/// translation code, though it is no longer used for it.
/// </summary>
public abstract class MutateRule
{

    /// <summary>
    /// A user-readable description of the intent of the rule. This differs from the ToString()
    /// method in that ToString() should be maintained for the purpose of debugging. Label
    /// should be transferred to any generated Stateful Horn Rules.
    /// </summary>
    public string Label { get; protected set; } = string.Empty;

    /// <summary>
    /// Generic conditions that apply to the rule by virtue of being part of an if-then-else
    /// branch or a let branch. It is the responsibility of the IMutateRule implementation
    /// to apply these conditions to generated rules.
    /// </summary>
    public IfBranchConditions Conditions { get; set; } = IfBranchConditions.Empty;

    /// <summary>
    /// Provides the option for annotating the direct user input that led to the creation of this
    /// IMutateRule. It is the responsibility of the IMutateRule implementation to ensure that 
    /// </summary>
    public UserDefinition? DefinedBy { get; set; }

    /// <summary>
    /// Create a Stateful Horn Rule based on the properties of this IMutateRule.
    /// </summary>
    /// <param name="factory">Rule factory to use to generate the new rule.</param>
    /// <returns>New Stateful Horn Rule.</returns>
    public abstract Rule GenerateRule(RuleFactory factory);

    /// <summary>
    /// Provides a recommendation of the minimum number of nession elaborations that should be
    /// added to ensure that the rule is properly applied.
    /// </summary>
    public int RecommendedDepth { get; protected set; } = 0;

    #region Sub-class convenience methods.

    /// <summary>
    /// Convenience method for sub-classes that creates a new State Consistent Rule with the given
    /// resulting event and all Mutate Rule attributes applied.
    /// </summary>
    /// <param name="factory">
    /// Factory to use to create rule. The factory is expected to have had premises and states
    /// previously registered. The factory object will be reset as part of the rule generation 
    /// process.
    /// </param>
    /// <param name="result">Required result of the new rule.</param>
    /// <returns>
    /// A new State Consistent Rule that has the Mutate Rule's Label, Conditions and DefinedBy
    /// attributes applied.
    /// </returns>
    protected Rule GenerateStateConsistentRule(RuleFactory factory, Event result)
    {
        factory.SetNextLabel(Label);
        factory.SetUserDefinition(DefinedBy);
        factory.GuardStatements = Conditions.CreateGuard();
        Rule r = factory.CreateStateConsistentRule(result);
        return r.Substitute(Conditions.CreateSigmaMap());
    }

    /// <summary>
    /// Convenience method for sub-classes that creates a new State Transferring Rule with all
    /// Mutate Rule attributes applied.
    /// </summary>
    /// <param name="factory">
    /// Factory to use to create rule. The factory is expected to have had premises and states
    /// previously registered. The factory object will be reset as part of the rule generation
    /// process.
    /// </param>
    /// <returns>
    /// A new State Transferring Rule that has the Mutate Rule's Label, Conditions and DefinedBy
    /// attributes applied.
    /// </returns>
    protected Rule GenerateStateTransferringRule(RuleFactory factory)
    {
        factory.SetNextLabel(Label);
        factory.SetUserDefinition(DefinedBy);
        factory.GuardStatements = Conditions.CreateGuard();
        Rule r = factory.CreateStateTransferringRule();
        return r.Substitute(Conditions.CreateSigmaMap());
    }

    #endregion

}
