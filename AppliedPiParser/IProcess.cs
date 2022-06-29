using System;
using System.Collections.Generic;

namespace AppliedPi;

/// <summary>
/// Represents a process in a Pi-Calculus model. A process may itself have sub-processes.
/// </summary>
public interface IProcess
{

    // Force implementation of a proper textual representation of the process.
    public string ToString();

    /// <summary>
    /// Replace one set of terms with another set of terms. The keys are the terms to be
    /// replaced.
    /// </summary>
    /// <param name="subs">Substitutions.</param>
    /// <returns>A new process with the terms replaced.</returns>
    public IProcess SubstituteTerms(IReadOnlyDictionary<string, string> subs);

    /// <summary>Returns names that are used within a process.</summary>
    /// <returns>Names used within a process.</returns>
    public IEnumerable<string> VariablesDefined();

    /// <summary>
    /// Retrieve sub-processes of this process that match the given predicate.
    /// </summary>
    /// <param name="matcher">When this predicate is true, the sub-process is returned.</param>
    /// <returns>Processes that match the given predicate.</returns>
    public IEnumerable<IProcess> MatchingSubProcesses(Predicate<IProcess> matcher);

    /// <summary>
    /// First step of ensuring that the terms used within a process are consistent with the
    /// rest of the model. Checking adds terms to the term resolver, and returns false if 
    /// existing terms in the resolver contradict the usage of these terms.
    /// </summary>
    /// <param name="nw">Network containing the full model.</param>
    /// <param name="termResolver">Term Resolver object that records term types.</param>
    /// <param name="errorMessage">
    /// A return string used to provide a textual explanation as to why the check may have
    /// failed. If this method returns true, then the string will be null.
    /// </param>
    /// <returns>True if terms were found to be consistent, false otherwise.</returns>
    public bool Check(Network nw, TermResolver termResolver, out string? errorMessage);

    /// <summary>
    /// Second step of ensuring that terms used within a process are consisten with the 
    /// rest of the model. Returns a process that matches the fully resolved terms.
    /// </summary>
    /// <param name="nw">Network containing the full model.</param>
    /// <param name="resolver">Term Resolver to use to create the returned process.</param>
    /// <returns>A process with terms updated to match the terms in the resolver.</returns>
    public IProcess Resolve(Network nw, TermResolver resolver);

    /// <summary>
    /// The location within the source code where the process is defined. This property
    /// should not be part of the equality tests between processes.
    /// </summary>
    public RowColumnPosition? DefinedAt { get; }

}
