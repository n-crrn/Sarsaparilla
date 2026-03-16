# Symbolic Concepts

The purpose of this document is to explain Sarsaparilla's conceptual underpinings.
This text is based on my thesis report "State-Aware Security Protocol Verifier"
by Nick Curran (2022).

## Horn Clauses

Horn Clauses are named for Alfred Horn and were introduced in
[^1]. They form the basis of the Prolog programming
language, and are hence also sometimes referred to as Prolog
rules. The modern form of the Horn Clause is as follows:

> $p_1 \land p_2 \land … \land p_n \rightarrow r$

Where $p_1, p_2, \ldots, p_n$ are premises, and $r$ is true if all of
its premises are.

A protocol can be represented as three sets of Prolog rules [^2]:

1. Rules representing the computational abilities of the attacker,
   such as the ability to calculate hash values.

2. Facts corresponding to the attacker's initial knowledge, such as
   constants used in the protocol. They are modelled by rules with no
   premises.

3. Rules representing the protocol itself, based on the messages
   sent.

Though Horn Clauses can be used to describe cryptographic
protocols, a flexible higher level representation that is more
readily understood is available in the form of $\pi$-Calculus.

## Modelling Concurrent Systems with $\pi$-Calculus

The original $\pi$-Calculus was introduced in [^3] and [^4]. It can be
viewed as a model of
computation where **interactional** behaviour is rigorously described
rather than **computational** behaviour[^5].

>  ... logicians came up with Turing machines, register machines
>  (on which imperative programming languages are built) and the lambda
>  calculus (on which the notion of parametric procedure is
>  founded. None of these models is concerned with interaction, as we
>  would normally understand the term. Their basic activity consists of
>  reading or writing on a storage medium (tape or registers), or
>  invoking a procedure with actual parameters. Instead, we shall work
>  with a model whose basic action is to communicate across an
>  interface with a **handshake**, which means that the two
>  participants synchronise this action.

A π-Calculus model is composed of processes, which communicate
with each other by sending names over channels. These names can
include channels, which allows a network of processes to reconfigure
itself. Processes may either be executed sequentially, concurrently,
or upon receipt of a message over a channel. One process may event be
defined as having infinite replicants.

Mobile, distributed systems can be reasoned about using
π-Calculus. Operations include determining observational
equivalence and bisimulation.

As a full description of the semantics of π-Calculus cannot be
included in this document. However, the
[Wikipedia page](https://en.wikipedia.org/wiki/%CE%A0-calculus) has
an excellent explanation with examples.

## Translating $\pi$-Calculus into Horn Clauses

A $\pi$-Calculus model may be translated into a set of Horn Clauses
based on what messages may be sent - and therefore, what the attacker
knows.[^7] A full algorithm for the translation
is given in [^7] but a descriptive translation
for each type of $\pi$-Calculus process is given in the following table:

| $\pi$ Process | Syntax | ... triggers ... |
| --- | --- | --- |
| Nil process | $0$ | Nothing. |
| Parallel composition | $P\ \|\ Q$ | Union of clauses of the sub-processes. |
| Replication | $!P$ | Adding a fresh session identifier to the sequence list. |
| Restriction | $\nu\ a$ | Replacing the restricted name with a name parameterised by the sequence list. |
| Input | $c(a)$ | Adding the input message to the input set. |
| Output | $\overline{c} \langle a \rangle$ | A Horn Clause is generated with the premises of the input set and an output message. |
| Expression evaluation | * | Union of clauses where the evaluation succeeds and when it fails. |
| Conditional | ** | If the conditional can be met, the union of both sub-processes. Otherwise, just the ``else'' sub-process. |

\* let $x = g(M_1, ..., M_2)$ in $P$ else $Q$

\** if $M = N$ then $P$ else $Q$

To be able to use the Horn Clause representation for a protocol, there
are two approximations that must be accepted:

- "Freshness is modeled by letting new names be functions of
  messages previously received by the creator of the name in the run
  of the protocol." [^2] By freshness, this means
  the creation of nonce values that are created during the run of the
  protocol.
- "A step of the protocol can be completed several times, as long
  as the previous steps have been completed at least once between the
  same principals." [^2]

These approximations can result in false attacks as the set of
derivable set of facts from a system is ever-increasing and not
associated with the state where they arise.[^9] For
example, [ProVerif](https://bblanche.gitlabpages.inria.fr/proverif/)
may report an attack where a nonce is leaked despite
the fact that the leak occurs well after the nonce ceased to be
relevant.

## Applied $\pi$-Calculus

Applied $\pi$-Calculus is to $\pi$-Calculus what Lisp is to
$\lambda$-Calculus. It provides a series of written semantics and
extensions to the pure $\pi$-Calculus that make it more practically
useful.

For example, pure $\pi$-Calculus only allows for messages that are
atomic names. However, there is little practical advantages to a model
author pretending that integers are not primitive. This makes Applied
$\pi$-Calculus closer to a realistic programming or modelling
language.[^7]

Examples of extensions added to $\pi$-Calculus by Applied
$\pi$-Calculus are:

- Algebraic datatypes such as pairs, tuples, arrays and lists.[^7]
- Look-up tables.[^8]
- Restricting the use of certain sorts (types)
  in certain circumstances.[^7]

The ProVerif Specification language is the dialect of Applied
$\pi$-Calculus used by the verification tool
[ProVerif](https://bblanche.gitlabpages.inria.fr/proverif/).[^7]



---

[^1]: A. Horn, “On sentences which are true of direct unions of algebras,” The Journal of
Symbolic Logic, vol. 16, no. 1, pp. 14–21, Mar. 1951. doi: 10.2307/2268661.

[^2]: B. Blanchet, “An efficient cryptographic protocol verifier based on prolog rules,” in 14th
IEEE Computer Security Foundations Workshop 2001, 2001, pp. 82–96. doi: 10.1109/
CSFW.2001.930138.

[^3]: R. Milner, J. Parrow, and D. Walker, “A calculus of mobile processes, i,” Information
and Computation, vol. 100, no. 1, pp. 1–40, 1992. doi: 10.1016/0890-5401(92)90008-4.

[^4]: R. Milner, J. Parrow, and D. Walker, “A calculus of mobile processes, ii,” Information
and Computation, vol. 100, no. 1, pp. 41–77, 1992.

[^5]: R. Milner, Communicating and Mobile Systems: The π-Calculus. New York, USA: Cam-
bridge University Press, 1999.

[^6]: B. Blanchet, “Modeling and verifying security protocols with the applied pi calculus and
proverif,” in Foundations and Trends in Privacy and Security, 1–2, vol. 1, Now Publishers
Inc, 2016, pp. 1–135. doi: 10.1561/3300000004.

[^7]: M. Abadi, B. Blanchet, and C. Fournet, “The applied pi calculus: Mobile values, new
names, and secure communication,” Journal of the ACM, vol. 65, no. 1, pp. 1–41, 2018.
doi: 10.1145/3127586.

[^8]: B. Blanchet, “Modeling and verifying security protocols with the applied pi calculus and
proverif,” in Foundations and Trends in Privacy and Security, 1–2, vol. 1, Now Publishers
Inc, 2016, pp. 1–135. doi: 10.1561/3300000004.

[^9]: M. Arapinis, E. Ritter, and M. Ryan, “Statverif: Verification of stateful processes,” in
2011 IEEE 24th Computer Security Foundations Symposium, 2011, pp. 33–47. doi: 10.
1109/CSF.2011.10.