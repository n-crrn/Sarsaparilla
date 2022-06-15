using System;
using System.Collections.Generic;

namespace StatefulHorn;

/// <summary>
/// A static namespace containing many sample knowledge models that can be used for
/// demonstrating the system.
/// </summary>
public class KnowledgeSampleLibrary
{
    #region Properties and methods required to create and access library.

    public static readonly List<(string Title, string Description, string Sample)> Models = new();

    private static void GenerateLibrary(params (string, string)[] symbolNamesDesc)
    {
        Type ksl = typeof(KnowledgeSampleLibrary);
        foreach ((string name, string desc) in symbolNamesDesc)
        {
            Models.Add((name, desc, (string)ksl.GetField(name)!.GetValue(null)!));
        }
    }

    static KnowledgeSampleLibrary()
    {
        GenerateLibrary(
            ("NameTupleCheckSampleCode", "A full featured model without nonces."),
            ("NonceTupleCheckSampleCode", "A full featured model querying nonces."),
            ("PureNameModelCode", "A simple demonstration of knowledge leading to other knowledge."),
            ("GuardedQueryCode", "A simple demonstration of guards in action."),
            ("GuardedStatefulQueryCode", "A demonstration of how nession evolution is affected by guards.")
        );
    }

    #endregion
    #region The Library - Integration Samples

    public const string NameTupleCheckSampleCode =
@"// This example comes from the paper 'A Verification Framework for Stateful
// Security Protocols' in Formal Methods and Software Engineering 2017
// pp 262-280 by L. Li, N. Dong, J. Pang, J. Sun, G. Bai, Y. Liu and 
// J.S. Dong. It should demonstrate that the names bob_l[] and bob_r[] are
// leaked. This particular implementation is based on a description by 
// Dr Naipeng Dong.

// Computational abilities of the attacker.
(1) = k(sk) -[ ]-> k(pk(sk))
(2) = k(m), k(pub) -[ ]-> k(aenc(m, pub))
(3) = k(sk), k(aenc(m, pk(sk))) -[ ]-> k(m)
(4) = k(p), k(n) -[ ]-> k(h(p, n))

// (5), (6), (7) and (8) are tuple methods, not required for Sarsaparilla code.

// Attacker knowledge.
(9) = -[ ]-> k(left[])
(10) = -[ ]-> k(right[])
(11) = -[ ]-> k(init[])

// Protocol/system description.
(12) = k(mf) -[ ]-> k(aenc(<mf, bob_l[], bob_r[]>, pk(sksd[])))
(13) = k(aenc(<mf, sl, sr>, pk(sksd[])))(a1) -[ (SD(h(mf, left[])), a1) ]-> k(sl)
(14) = k(aenc(<mf, sl, sr>, pk(sksd[])))(b1) -[ (SD(h(mf, right[])), b1) ]-> k(sr)
(15) = -[ (SD(m), c1) ]-> k(m)
(16) = k(x)(d1) -[ (SD(m), d1) ]-> <d1: SD(h(m, x))>

init SD(init[])
query leak <bob_l[], bob_r[]>
limit 5
";

    public const string NonceTupleCheckSampleCode =
@"// This example comes from the paper 'A Verification Framework for Stateful
// Security Protocols' in Formal Methods and Software Engineering 2017
// pp 262-280 by L. Li, N. Dong, J. Pang, J. Sun, G. Bai, Y. Liu and 
// J.S. Dong. It should demonstrate that the nonces [bob_l] and [bob_r] are
// NOT leaked. This particular implementation is based on a description by 
// Dr Naipeng Dong.

// Attacker knowledge.
-[]->k(left[])
-[]-> k(right[])
-[]-> k(init[])

// Computational abilities of the attacker.
k(m), k(pub) -[]-> k(enc_a(m, pub))
k(enc_a(m, pk(sk))), k(sk) -[]-> k(m)
k(sk) -[]-> k(pk(sk))
k(p), k(n) -[ ]-> k(h(p, n))

// Legitiment system participant behaviour.
n([bobl])(a0), n([bobr])(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k([bobl])
n([bobl])(a0), n([bobr])(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k([bobr])

// Protocol/system description.
k(x)(a0) -[(SD(m), a0)]-> <a0: SD(h(m, x))>
k(a), k(b), k(c) -[ ]-> k(enc_a(<a, b, c>, pk(sksd[])))
k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k(sl)
k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k(sr)
-[ (SD(m), a0) ]-> k(m)

// The following commented line adds the ability for the system to reset the 
// security device (SD). If it can be reset, then the system can gain access
// to both [bobl] and [bobr] from a single message.
// -[ (SD(m), a0) ]-> <a0: SD(init[])>

init SD(init[])
query leak <[bobl], [bobr]>
limit 5
";

    #endregion
    #region The Library - Functional Samples

    public const string PureNameModelCode =
@"// A simple example of a system where knowing one thing leads to another, and another.
// This example should demonstrate a global attack - that is, one that is not 
// dependent on state.

-[ ]-> k(c[])
k(c[]) -[ ]-> k(d[])
k(d[]) -[ ]-> k(s[])

init SD(init[])
query leak s[]
";

    public const string GuardedQueryCode =
@"// The following simple example demonstrates the use of guards to prevent
// invalid terms from being derived. Specifically, the name 'a[]' cannot
// be used as the first argument to the function 'enc'.

init SD(init[])
[x ~/> a[]] k(x), k(y) -[ ]-> k(enc(x, y))
-[ ]-> k(a[])
-[ ]-> k(b[])

// The following query should fail.
query leak enc(a[], b[])

// If uncommented, the following query should succeed.
// query leak enc(b[], a[])";

    public const string GuardedStatefulQueryCode =
@"// A simple example demonstrating how a guard can prevent nession evolution
// in a system. In this case, the nessions are prevented from elaborating
// a state of SD(test1[]). 

init SD(init[])
-[ ]-> k(test1[])
-[ ]-> k(test2[])
[x ~/> test1[]] k(x)(a0) -[ (SD(init[]), a0) ]-> <a0: SD(x)>
-[ (SD(m), a0) ]-> k(h(m))

// The following query should fail.
query leak h(test1[])

// If uncommented, the following query should succeed. This demonstrates that
// states that are not explicitly disallowed are achieveable.
// query leak h(test2[])
";

    #endregion
}
