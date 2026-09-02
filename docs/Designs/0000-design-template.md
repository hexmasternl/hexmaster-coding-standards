# Design template

**Status:** draft
**Applies to:** which languages, frameworks, or project types this covers

## Intent

What this design is for, in two or three sentences. State the problem it solves so a reader
can tell whether their situation is the one being addressed.

A design document describes a pattern or convention to follow. If you are recording a
one-time choice rather than a repeatable pattern, write an ADR instead.

## The pattern

Describe the shape to follow. Be concrete: name the types, the folders, the method
signatures, the naming rules. A design nobody can apply without asking a follow-up question
is not finished.

```csharp
// Show the pattern in code. A short, complete example beats a long prose description.
```

## Why this way

The reasoning behind the shape. What breaks if you do it differently? Which failure has this
pattern actually prevented in practice?

This section is what stops the design being cargo-culted. A rule with a stated reason can be
applied with judgement; a rule without one gets followed into situations it was never meant
for.

## When not to apply it

Every pattern has a boundary. Name the cases where following this would be wrong, and say
what to do instead. Designs without an escape hatch get worked around silently.

## Examples

**Good.** A real example, ideally from this codebase, with a note on what makes it correct.

**Bad.** A realistic violation and the specific problem it causes. Avoid strawmen — the
useful bad example is the one a competent person would plausibly write.

## Related

Link the ADRs that motivated this design and the structure documents it interacts with, using
their catalog ids.
