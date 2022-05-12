namespace StatefulHorn;

/// <summary>
/// This is a "marker" interface to indicate that the given type of message is only composed of
/// elements that are variable, and hence can be assigned to. There are no methods, but this 
/// marker allows the use of the type system to validate program logic at compile time.
/// </summary>
public interface IAssignableMessage : IMessage { }
