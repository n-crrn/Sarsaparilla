using System.Collections.Generic;

namespace StatefulHorn;

/// <summary>
/// An object used to track how rules were generated.
/// </summary>
public interface IRuleSource
{

    public string Describe();

    public List<IRuleSource> Dependencies { get; }

}
