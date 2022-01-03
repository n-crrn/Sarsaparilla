namespace AppliedPi;

public interface IProcess
{
    public IProcess? Next { get; set; }

    public string ToString();

    // FIXME: Consider whether there is a requirement for listing the variables
    // required for this to work, and listing the variables created.

    // FIXME: Add a facility to provide a formatted string in the Pi calculus.
}
