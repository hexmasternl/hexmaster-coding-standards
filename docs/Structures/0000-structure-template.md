# Structure template

**Status:** draft
**Applies to:** which kind of repository or project this layout covers

## Intent

What kind of project this layout is for and what it optimises for — reviewability, test
isolation, deployment shape. A reader should be able to tell in one paragraph whether their
project is in scope.

## Layout

Show the tree, annotated. Annotations are the point: a bare tree tells a reader where to put
files but not why, so the first unusual case gets guessed at.

```
repository-root/
  Solution.slnx                    # solution at the root, not under src/
  global.json                      # pinned SDK
  Directory.Build.props            # properties shared by every project
  src/
    Company.Product.Host/          # the executable: composition, transport, endpoints
    Company.Product.Domain/        # the logic worth unit testing, no hosting types
  tests/
    Company.Product.Domain.Tests/  # mirrors the project under test, one to one
```

## Rules

The constraints that make the layout hold, stated so a reviewer can check them:

- Where a new project goes, and what it may reference.
- Which dependency directions are forbidden, and why.
- What must never appear in which folder.

Write these as things a reviewer can point at in a pull request. A rule that cannot be
checked will not be enforced.

## Naming

The naming convention for projects, folders, namespaces, and test projects, with an example
of each. Say what the test project for a given project is called — that mapping is the one
people most often get wrong.

## Why this way

What this layout buys, and what the obvious alternative costs. If a folder exists purely to
make something testable or independently deployable, say so — that is the fact that keeps
someone from collapsing it later.

## When not to apply it

The project sizes or shapes where this layout is overkill or insufficient, and what to reach
for instead.

## Related

Link the ADRs that drove this layout and any designs that assume it, using their catalog ids.
