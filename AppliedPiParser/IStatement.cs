namespace AppliedPi;

/// <summary>
/// Represents a statement (collection of understandable words) in the Applied Calculus
/// </summary>
public interface IStatement
{

    /// <summary>
    /// User representable explanation of the type.
    /// </summary>
    public string StatementType { get; }

    // FIXME: Include interface for retrieving the HTML version of the statement (where 
    // appropriate.)

    /// <summary>
    /// "Executes" the statement upon the given model. For instance, in the case of a 
    /// FreeStatement, it will add a free name declaration to the Network.
    /// </summary>
    /// <param name="nw">Network to ammend.</param>
    public void ApplyTo(Network nw);

    /// <summary>
    /// The location within the source code where the statement is defined. This property
    /// should not be part of the equality tests between statements.
    /// </summary>
    public RowColumnPosition? DefinedAt { get; }

}
