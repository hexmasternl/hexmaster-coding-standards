# ADR template

**Status:** draft
**Date:** 2026-09-02

## Context

What is the situation that forces a decision? State the constraints, the pressures, and what
is currently true — not what you plan to do. A reader should be able to disagree with the
decision below on the basis of this section alone.

Keep it factual. If a constraint comes from outside the team (a platform limit, a licence, a
deadline), say where it comes from.

## Decision

One or two sentences, in the present tense, stating what has been decided.

> We will do X.

Then the reasoning: why this option and not the obvious alternative. This is the part future
readers come for — a decision without a rationale is a rule nobody can safely change.

## Alternatives considered

For each option seriously weighed:

**Option name.** What it would have looked like, what it bought, and the specific reason it
lost. "Rejected as too complex" is not a reason; "rejected because it needs a bootstrap step
a newcomer will forget" is.

An ADR with no alternatives is usually an ADR written after the fact. If there genuinely was
no other option, say so and say why.

## Consequences

What becomes true once this is adopted, both good and bad. Include the costs you are
accepting knowingly — those are what a future reader needs when the decision starts to hurt.

- What gets easier.
- What gets harder.
- What now has to be maintained, monitored, or paid for.

## Status history

Record transitions rather than editing the status in place, so the trail survives:

- `draft` — 2026-09-02, opened for discussion.

When an ADR is replaced, set its status to `superseded` and link the replacement here. Never
delete a superseded ADR; the reason a decision was reversed is as valuable as the decision.
