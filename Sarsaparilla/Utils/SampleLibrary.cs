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
(3) = new([bob_l], l_sl[]), new([bob_r], l_sr[]), know(m_f) -[ ]-> know(enc_a((m_f, [bob_l], [bob_r]), pk(sksd[])))
(4) = know(enc_a((m_f, s_l, s_r), pk(sksd[])))(a_1) -[ (SD(init[]), a_0), (SD(h(m_f, left[])), a_1) : {a_0 =< a_1} ]-> know(s_l)
(5) = -[ (SD(init[]), a_0), (SD(m), a_2) : {a_0 =< a_2} ]-> know(m)
(6) = know(x)(a_3) -[ (SD(init[]), a_0), (SD(m), a_3) : {a_0 =< a_3} ]-> <a_3: SD(h(m, x))>
(7) = know(sk) -[ ]-> know(pk(sk))
(8) = know(enc_a((m_f, s_l, s_r), pk(sksd[])))(a_5) -[ (SD(init[]), a_0), (SD(h(m_f, right[])), a_5) : {a_0 =< a_5 } ]-> know(s_r)
(9) = new([bob_l], l_sl[]), new([bob_r], l_sr[]), know([bob_l]), know([bob_r]) -[ ]-> leak(([bob_l], [bob_r]))
";

    public static readonly string PaperExample2 =
@"// This example comes from Li Li et al 2017, and is described in appendix A.
// This example should demonstrate a leak.
(1) = know(m), know(pub) -[ ]-> know(enc_a(m, pub))
(2) = know(enc_a(m, pk(sk))), know(sk) -[ ]-> know(m)
(3) = know(m_f) -[ ]-> know(enc_a((m_f, bob_l[], bob_r[]), pk(sksd[])))
(4) = know(enc_a((m_f, s_l, s_r), pk(sksd[])))(a_1) -[ (SD(init[]), a_0), (SD(h(m_f, left[])), a_1) : {a_0 =< a_1} ]-> know(s_l)
(5) = -[ (SD(init[]), a_0), (SD(m), a_2) : {a_0 =< a_2} ]-> know(m)
(6) = know(x)(a_3) -[ (SD(init[]), a_0), (SD(m), a_3) : {a_0 =< a_3} ]-> <a_3: SD(h(m, x))>
(7) = know(sk) -[ ]-> know(pk(sk))
(8) = know(enc_a((m_f, s_l, s_r), pk(sksd[])))(a_5) -[ (SD(init[]), a_0), (SD(h(m_f, right[])), a_5) : {a_0 =< a_5 } ]-> know(s_r)
(9) = know(bob_l[]), know(bob_r[]) -[ ]-> leak(([bob_l], [bob_r]))
";

}
