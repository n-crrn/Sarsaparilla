namespace Sarsaparilla;

public static class SampleLibrary
{

    public static readonly string Basic =
@"k(x), k(y) -[ ]-> k(enc(x,y))
// Test comment.
-[ ]-> k(z[])
";

    public static readonly string PaperExample1 =
@"// This example comes from Li Li et al 2017, and is described in appendix A.
// This example should demonstrate no leak.
(1) = know(m), know(pub) -[ ]-> know(enc_a(m, pub))
(2) = know(enc_a(m, pk(sk))), know(sk) -[ ]-> know(m)
(3) = new([bob_l], l_sl[]), new([bob_r], l_sr[]), know(m_f) -[ ]-> know(enc_a(<m_f, [bob_l], [bob_r]>, pk(sksd[])))
(4) = know(enc_a(<m_f, s_l, s_r>, pk(sksd[])))(a_1) -[ (SD(init[]), a_0), (SD(h(m_f, left[])), a_1) : {a_0 =< a_1} ]-> know(s_l)
(5) = -[ (SD(init[]), a_0), (SD(m), a_2) : {a_0 =< a_2} ]-> know(m)
(6) = know(x)(a_3) -[ (SD(init[]), a_0), (SD(m), a_3) : {a_0 =< a_3} ]-> <a_3: SD(h(m, x))>
(7) = know(sk) -[ ]-> know(pk(sk))
(8) = know(enc_a(<m_f, s_l, s_r>, pk(sksd[])))(a_5) -[ (SD(init[]), a_0), (SD(h(m_f, right[])), a_5) : {a_0 =< a_5 } ]-> know(s_r)
(9) = new([bob_l], l_sl[]), new([bob_r], l_sr[]), know([bob_l]), know([bob_r]) -[ ]-> leak(<[bob_l], [bob_r]>)
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
(13) = k(aenc(<mf, sl, sr>, pk(sksd[])))(a1) -[ (SD(init[]), a0), (SD(h(mf, left[])), a1) : {a0 =< a1} ]-> k(sl)
(14) = k(aenc(<mf, sl, sr>, pk(sksd[])))(b1) -[ (SD(init[]), b0), (SD(h(mf, right[])), b1) : {b0 =< b1} ]-> k(sr)
(15) = -[ (SD(init[]), c0), (SD(m), c1) : {c0 =< c1} ]-> k(m)
(16) = k(x)(d1) -[ (SD(init[]), d0), (SD(m), d1) : {d0 =< d1} ]-> <d1: SD(h(m, x))>
(17) = k(bob_l[]), k(bob_r[]) -[ ]-> leak(<bob_l[], bob_r[]>)
";

}
