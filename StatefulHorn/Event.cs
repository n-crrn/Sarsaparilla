using StatefulHorn.Messages;
using System.Collections.Generic;

namespace StatefulHorn;

/// <summary>
/// An element that is either a premise of a rule or a result of a rule.
/// </summary>
public class Event : ISigmaUnifiable
{

    /// <summary>
    /// Describes what kind of Event an Event is: know, new or make.
    /// </summary>
    public enum Type
    {
        /// <summary>
        /// If a value is known.
        /// </summary>
        Know,

        /// <summary>
        /// If a nonce is generated at that point.
        /// </summary>
        New,

        /// <summary>
        /// If a new message is made using nonces at that point.
        /// </summary>
        Make
    }

    #region Creation methods.

    /// <summary>
    /// Create a new Event with the given type and message.
    /// </summary>
    /// <param name="t">Type of event.</param>
    /// <param name="msg">Message of the event.</param>
    private Event(Type t, IMessage msg)
    {
        EventType = t;
        Message = msg;
        ContainsVariables = msg.ContainsVariables;
    }

    /// <summary>Create a Know event with the given message.</summary>
    /// <param name="msg">Message of the event.</param>
    /// <returns>New Know event.</returns>
    public static Event Know(IMessage msg) => new(Type.Know, msg);

    /// <summary>Create a New event with the given message.</summary>
    /// <param name="nonce">Message of the event.</param>
    /// <returns>New New event.</returns>
    public static Event New(NonceMessage nonce) => new(Type.New, nonce);

    /// <summary>Create a Make event with the given message.</summary>
    /// <param name="msg">Message of the event.</param>
    /// <returns>New Make event.</returns>
    public static Event Make(IMessage msg) => new(Type.Make, msg);

    /// <summary>
    /// Create a new event with substitutions conducted on the Message.
    /// </summary>
    /// <param name="sigma">Substitution map.</param>
    /// <returns>
    /// A new event if the SigmaMap contains items. Otherwise, the same event is returned.
    /// </returns>
    public Event Substitute(SigmaMap sigma)
    {
        return sigma.IsEmpty ? this : new(EventType, Message.Substitute(sigma));
    }

    #endregion
    #region Properties.

    /// <summary>The kind of event this is.</summary>
    public Type EventType { get; private init; }

    /// <summary>The message of the event.</summary>
    public readonly IMessage Message;

    /// <summary>Convenience property which is true if the event is a Know event.</summary>
    public bool IsKnow => EventType == Type.Know;

    /// <summary>Convenience property which is true if the event is a New event.</summary>
    public bool IsNew => EventType == Type.New;

    /// <summary>Convenience property which is true if the event is a Make event.</summary>
    public bool IsMake => EventType == Type.Make;

    #endregion
    #region Filtering

    /// <summary>
    /// Determines if the given message is contained as a sub-message within the Event.
    /// </summary>
    /// <param name="msg">Message to search for.</param>
    /// <returns>True if found, false otherwise.</returns>
    public bool ContainsMessage(IMessage msg) => Message.ContainsMessage(msg);

    #endregion
    #region Unification determination

    public bool ContainsVariables { get; init; }

    /// <summary>Cache for the Variables property.</summary>
    private HashSet<IMessage>? _Variables;

    public IReadOnlySet<IMessage> Variables
    {
        get
        {
            if (_Variables == null)
            {
                _Variables = new();
                Message.CollectVariables(_Variables);
            }
            return _Variables;
        }
    }

    public bool CanBeUnifiedTo(ISigmaUnifiable other, Guard g, SigmaFactory subs)
    {
        return other is Event ev 
            && IsKnow 
            && ev.IsKnow 
            && Message.DetermineUnifiedToSubstitution(ev.Message, g, subs);
    }

    public bool CanBeUnifiableWith(ISigmaUnifiable other, Guard fwdG, Guard bwdG, SigmaFactory subs)
    {
        return other is Event ev
            && IsKnow
            && ev.IsKnow
            && Message.DetermineUnifiableSubstitution(ev.Message, fwdG, bwdG, subs);
    }

    #endregion
    #region Basic object overrides.

    public override bool Equals(object? obj)
    {
        return obj is Event ev && EventType == ev.EventType && Message.Equals(ev.Message);
    }

    public override int GetHashCode() => Message.GetHashCode();

    public static bool operator ==(Event? ev1, Event? ev2) => Equals(ev1, ev2);

    public static bool operator !=(Event? ev1, Event? ev2) => !Equals(ev1, ev2);

    /// <summary>Cache for the ToString() method.</summary>
    private string? Description;

    public override string ToString()
    {
        if (Description == null)
        {
            Description = EventType switch
            {
                Type.Know => $"know({Message})",
                Type.New => $"new({Message})",
                Type.Make => $"make({Message})",
                _ => throw new System.NotImplementedException()
            };
        }
        return Description;
    }

    #endregion
}
