namespace AppliedPi.Model;

public class AttackerQuery
{

    public AttackerQuery(Term leakTerm)
    {
        LeakQuery = leakTerm;
    }

    public Term LeakQuery { get; init; }

    public override bool Equals(object? obj) => obj is AttackerQuery aq && LeakQuery.Equals(aq.LeakQuery);

    public override int GetHashCode() => LeakQuery.GetHashCode();

    public override string ToString() => $"query attacker({LeakQuery})";

}
