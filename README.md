# SARSAPARILLA

This is the repository for the "State Aware Research Studying All Protocols' Authoritative
Representation In Latest Logic Analyser". The analyser is written by Nick Curran with 
the aim of attaining a Master in Computer Science through The University of Queensland.
Please note that this analyser is not yet fully functional.

## Solution Structure

The solution is written in C# using Microsoft's Blazor WebAssembly framework. It is 
composed of the following projects:

1. *StatefulHorn*: This library provides data structures and algorithms for working with
   Stateful Horn clauses. Stateful Horn clauses are Horn clauses that incorporate a
   trace of the states that they are valid in, and include rules dictating how 
   states may be mutated in the rule-set.

2. *AppliedPi*: This is a library for parsing the typed Applied Pi language, as originally
   demonstrated by ProVerif. Applied Pi "programs" are parsed into Networks representing
   a set of communicating processes.

3. *SarsparillaTests*: This is the automated testing suite for the whole solution. It is
   built using the MSTest framework.

4. *SarsaWidgets*: This is a Razor component library providing the user controls (widgets)
   that allows a web-based user to write, view and query Stateful Horn clauses and 
   Applied Pi networks.

5. *SarsaWidgetTests*: This is a Blazor WebAssembly application that allows for the quick
   viewing of SarsaWidget components in various states during development. This is not
   intended for the use of normal users.

6. *Sarsaparilla*: This is the Blazor WebAssembly application intended to allow for
   research into new protocol representations.

## Licensing

All files in this project are provided subject to the GNU General Public License version 3.
The details of this license are included in the file [COPYING.txt](/COPYING.txt).
