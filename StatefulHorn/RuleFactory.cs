using System;
using System.Collections.Generic;

namespace StatefulHorn;

/// <summary>
/// A helper class for creating new Rules. Instances of this class allow rules to be created by
/// registering and tracking snapshots during the rule construction.
/// </summary>
/// <example>
/// The following is an example of how to create a basic state consistent rule:
/// <code>
/// // Creates rule "know(x), know(y) -[ ]-&gt; know(enc(x, y))"
/// RuleFactory f = new();
/// f.SetNextLabel("test-rule");
/// f.RegisterPremises(Event.Know(new VariableMessage("x")), Event.Know(new VariableMessage("y")));
/// IMessage enc = new FunctionMessage("enc", new() { new VariableMessage("x"), new VariableMessage("y") } );
/// Rule r = f.CreateStateConsistentRule(Event.Know(enc));
/// </code>
/// The following is an example of how to create a state transfering rule:
/// <code>
/// // Creates rule "know(x)(a0) -[ (SD(init[]), a0) ]-&gt; &lt;a0: SD(x)&gt;"
/// RuleFactory f = new();
/// Snapshot ss = f.RegisterState(new State("SD", new NameMessage("init")));
/// f.RegisterPremises(ss, Event.Know(new VariableMessage("x")));
/// ss.TransfersTo = new State("SD", new VariableMessage("x"));
/// Rule r = f.CreateStateTransferringRule();
/// </code>
/// </example>
public class RuleFactory
{
    /// <summary>Creates a new factory. No special setup is required to use the new instance.</summary>
    public RuleFactory()
    {
        Snapshots = new();
        Premises = new();
        _GuardStatements = new();
    }

    /// <summary>
    /// Clear all previously set information (guards, labels, premises and snapshots).
    /// </summary>
    public void Reset()
    {
        Label = null;
        Snapshots.Clear();
        Premises.Clear();
        _GuardStatements = new();
    }

    private Guard _GuardStatements;

    /// <summary>
    /// Sets the Guard to be used for the next created rule.
    /// </summary>
    public Guard? GuardStatements
    {
        get => _GuardStatements;
        set => _GuardStatements = value ?? Guard.Empty;
    }

    #region User-friendly labelling.

    /// <summary>
    /// Either set to the user's preferred label, or null. If set to null, an
    /// automatically generate label will be used for the rule.
    /// </summary>
    private string? Label;

    /// <summary>
    /// Counter used to help generate unique automatic labels for rules.
    /// </summary>
    private int UnlabelledCounter;

    /// <summary>
    /// Sets the label to be used for the next created Rule. If the label is not set, then an
    /// automatically generated label of form "Rule {number}" will be used.
    /// </summary>
    /// <param name="newLabel">
    /// The label string to use. If null, then an automatically generated label will be used.
    /// </param>
    public void SetNextLabel(string? newLabel)
    {
        Label = newLabel;
    }

    /// <summary>
    /// If a label has been manually set, it returns that label. Otherwise, it returns a newly
    /// generated automatic label of form "Rule {number]".
    /// </summary>
    /// <returns>The label to use for the next rule.</returns>
    private string GetValidLabel()
    {
        if (Label == null)
        {
            string autoLabel = $"Rule {UnlabelledCounter}";
            UnlabelledCounter++;
            return autoLabel;
        }
        return Label;
    }

    #endregion
    #region Snapshot registering.

    /// <summary>The list of currently registered Snapshots.</summary>
    private readonly List<Snapshot> Snapshots;

    /// <summary>
    /// Registers a State, and returns a Snapshot object that can be referenced by premises,
    /// marked for a state transition or have relations with other Snapshots set.
    /// </summary>
    /// <param name="cond">The state to register.</param>
    /// <returns>A snapshot for further referencing.</returns>
    public Snapshot RegisterState(State cond)
    {
        Snapshot ss = new(cond);
        Snapshots.Add(ss);
        return ss;
    }

    #endregion
    #region Premise setting and validation.

    /// <summary>
    /// The list of currently registered premises. This is maintained as a list instead of a set
    /// in order to maintain the user supplied ordering of premises. This ordering is maintained
    /// to assist readability, as users will expect the premises to be in the order they added
    /// the premises.
    /// </summary>
    private readonly List<Event> Premises;

    /// <summary>
    /// Add a premise in preparation for the creation of the next rule. If the premise is already
    /// registered, it is ignored to ensure that there is only one instance of a premise at any
    /// one time.
    /// </summary>
    /// <param name="prem">Premise to register.</param>
    public void RegisterPremise(Event prem)
    {
        CheckPremisesAreValid(prem);
        if (!Premises.Contains(prem))
        {
            Premises.Add(prem);
        }
    }

    /// <summary>
    /// Convenience method to register multiple premises at once.
    /// </summary>
    /// <param name="premisesToReg">Premises to add to the next rule.</param>
    /// <seealso cref="RegisterPremise(Event)"/>
    public void RegisterPremises(params Event[] premisesToReg)
    {
        foreach (Event premise in premisesToReg)
        {
            RegisterPremise(premise);
        }
    }

    public void RegisterPremises(Snapshot ss, params Event[] premisesToReg)
    {
        RegisterPremises(ss, (IEnumerable<Event>)premisesToReg);
    }

    /// <summary>
    /// Convenience method to register multiple premises at once, and set the snapshot that they
    /// correspond with.
    /// </summary>
    /// <param name="ss">Snapshot to register all premises with.</param>
    /// <param name="premisesToReg">Premises to add to the next rule.</param>
    /// <exception cref="RuleConstructionException">
    /// Thrown if the snapshot is not registered with this RuleFactory instance.
    /// </exception>
    /// <seealso cref="RegisterPremise(Event)"/>
    public void RegisterPremises(Snapshot ss, IEnumerable<Event> premisesToReg)
    {
        // Make sure that the given snapshot has been registered.
        if (!Snapshots.Contains(ss))
        {
            throw new RuleConstructionException("Snapshot not previously registered.");
        }
        ss.AddPremises(premisesToReg);
        foreach (Event premise in premisesToReg)
        {
            RegisterPremise(premise);
        }
    }

    #endregion
    #region Rule creation.

    /// <summary>
    /// Use the previously set list of guards, premises and snapshots to generate a new state
    /// consistent rule. All previously set rule values for the Factory are reset.
    /// </summary>
    /// <param name="result">The event to be created.</param>
    /// <returns>A new state consistent rule.</returns>
    /// <exception cref="RuleConstructionException">
    /// Thrown if the given result event is of the wrong type (i.e. New or Init) or the result
    /// event is in the premise list. If thrown, previously set rule values for the Factory are
    /// not reset.
    /// </exception>
    /// <seealso cref="Reset"/>
    public StateConsistentRule CreateStateConsistentRule(Event result)
    {
        CheckResultEventIsValid(result);
        string lbl = GetValidLabel();
        SnapshotTree tree = new(Snapshots);
        StateConsistentRule r = new(lbl, _GuardStatements, new(Premises), tree, result);
        Reset();
        return r;
    }

    /// <summary>
    /// Use the previously set list of guards, premises and snapshots to generate a new
    /// state transferring rule. All previously set rule values for the Factory are reset.
    /// </summary>
    /// <returns>A new state transferring rule.</returns>
    /// <exception cref="RuleConstructionException">
    /// Thrown if no Snapshot has their TransfersTo member set. If this is the case, a State
    /// Transferring Rule cannot be created as there is no State to transfer to. If thrown,
    /// previously set rule values for the Factory are not reset.
    /// </exception>
    /// <seealso cref="Reset"/>
    public StateTransferringRule CreateStateTransferringRule()
    {
        string lbl = GetValidLabel();
        SnapshotTree tree = new(Snapshots);
        StateTransformationSet result = tree.ExtractStateTransformations();
        if (result.IsEmpty)
        {
            string emptyMsg = "Attempted to create state transformation rule when there are no " +
                "transformations specified in snapshot tree.";
            throw new RuleConstructionException(emptyMsg);
        }
        StateTransferringRule r = new(lbl, _GuardStatements, new(Premises), tree, result);
        Reset();
        return r;
    }

    #endregion
    #region Rule validation.

    /// <summary>
    /// Ensure that the premise events are of the valid types: Know, New or Init. An exception
    /// is thrown if at least one of the premise events is not valid.
    /// </summary>
    /// <param name="events">Events to check.</param>
    /// <exception cref="RuleConstructionException">
    /// Thrown if an invalid event is found.
    /// </exception>
    private static void CheckPremisesAreValid(params Event[] events)
    {
        foreach (Event ev in events)
        {
            if (ev.EventType == Event.Type.Accept || ev.EventType == Event.Type.Leak)
            {
                string msg = $"Attmpted to set '{ev}' as premise event: only know, init and new " +
                    "events can be set as premises.";
                throw new RuleConstructionException(msg);
            }
        }
    }

    /// <summary>
    /// Ensure that the given result event is of the correct type (Know, Accept or Leak) and is
    /// not included in the premise list. If an issue is found, an exception is thrown.
    /// </summary>
    /// <param name="ev">Intended result event.</param>
    /// <exception cref="RuleConstructionException">
    /// Thrown if the event has an incorrect type or is included in the premises.
    /// </exception>
    private void CheckResultEventIsValid(Event ev)
    {
        // Ensure it is the correct type.
        if (ev.EventType == Event.Type.Init || ev.EventType == Event.Type.New)
        {
            string msg = $"Cannot set '{ev}' as result event: only know, accept and leak events can be valid results.";
            throw new RuleConstructionException(msg);
        }
        // Ensure the event is not in the premises.
        foreach (Event premise in Premises)
        {
            if (ev.Equals(premise))
            {
                string dupMsg = $"Cannot have rule result '{ev}' as both premise and result of rule.";
                throw new RuleConstructionException(dupMsg);
            }
        }
    }

    #endregion
}

/// <summary>
/// Exception thrown when a logical inconsistency is detected whilst constructing new rules using a
/// RuleFactory instance.
/// </summary>
public class RuleConstructionException : Exception
{
    /// <summary>
    /// Creates a new RuleConstructionException with a message string.
    /// </summary>
    /// <param name="msg">Description of error encountered.</param>
    public RuleConstructionException(string msg) : base(msg) { }
}
