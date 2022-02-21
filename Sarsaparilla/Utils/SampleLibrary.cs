namespace Sarsaparilla;

public static class SampleLibrary
{

    public static readonly string Basic =
@"k(x), k(y) -[ ]-> k(enc(x,y))
// Test comment.
-[ ]-> k(z[])
init SD(init[])
query leak enc(z[], z[])
";

    public static readonly string PaperExample1 =
@"// This example comes from Li Li et al 2017, and is described in appendix A.
// This example should demonstrate no leak.
// Globally known facts.
-[]->k(left[])
-[]-> k(right[])
-[]-> k(init[])
// Global derived knowledge.
k(m), k(pub) -[]-> k(enc_a(m, pub))
k(enc_a(m, pk(sk))), k(sk) -[]-> k(m)
k(sk) -[]-> k(pk(sk))
k(p), k(n) -[ ]-> k(h(p, n))
// Session commencement and state transitions - should '_' be added as a variable/message stand-in?
n([bobl], l[])(a0), n([bobr], l[])(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k([bobl])
n([bobl], l[])(a0), n([bobr], l[])(a0), m(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k([bobr])
//n([bobl], l[]), n([bobr], r[]), k(mf) -[ ]-> k(enc_a(<mf, [bobl], [bobr]>, pk(sksd[])))
k(x)(a0) -[(SD(m), a0)]-> <a0: SD(h(m, x))>
//-[ (SD(m), a0) ]-> <a0: SD(init[])>
// Reading from states and inputs.
k(a), k(b), k(c) -[ ]-> k(enc_a(<a, b, c>, pk(sksd[])))
k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, left[])), a0) ]-> k(sl)
k(enc_a(<mf, sl, sr>, pk(sksd[])))(a0) -[ (SD(h(mf, right[])), a0) ]-> k(sr)
-[ (SD(m), a0) ]-> k(m)
init SD(init[])
query leak <[bobl], [bobr]>
";

    public static readonly string PaperExample2 =
@"// This example comes from Li Li et al 2017, and is described in appendix A.
// This example should demonstrate a leak.
// Based on Dr Dong's walkthrough.
(1) = k(sk) -[ ]-> k(pk(sk))
(2) = k(m), k(pub) -[ ]-> k(aenc(m, pub))
(3) = k(sk), k(aenc(m, pk(sk))) -[ ]-> k(m)
(4) = k(p), k(n) -[ ]-> k(h(p, n))
// (5), (6), (7) and (8) are tuple methods, not required for sarsaparilla code.
(9) = -[ ]-> k(left[])
(10) = -[ ]-> k(right[])
(11) = -[ ]-> k(init[])
(12) = k(mf) -[ ]-> k(aenc(<mf, bob_l[], bob_r[]>, pk(sksd[])))
(13) = k(aenc(<mf, sl, sr>, pk(sksd[])))(a1) -[ (SD(h(mf, left[])), a1) ]-> k(sl)
(14) = k(aenc(<mf, sl, sr>, pk(sksd[])))(b1) -[ (SD(h(mf, right[])), b1) ]-> k(sr)
(15) = -[ (SD(m), c1) ]-> k(m)
(16) = k(x)(d1) -[ (SD(m), d1) ]-> <d1: SD(h(m, x))>
// (17) = k(bob_l[]), k(bob_r[]) -[ ]-> leak(<bob_l[], bob_r[]>)
init SD(init[])
query leak <bob_l[], bob_r[]>
";

}
