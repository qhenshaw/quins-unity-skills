---
name: clarify
description: Clarify the current task by grilling the user through multiple-choice questions shown one at a time (AskUserQuestion), challenging weak answers, until no open question or ambiguity is left; then summarize the decisions and ask one final question that confirms its assumptions and starts the work. Use when the user explicitly asks to clarify a task before work starts ("/clarify", "clarify this task", "ask me clarifying questions", "let's clarify before you start"). A request to explain or describe something ("clarify what this function does") is a normal answer, and "grill me" belongs to the grilling skill.
---

# Clarify: grill the task, one question at a time

Goal: reach a shared understanding of the task where nothing is silently assumed. The user answers decisions;
you find facts. Every question is a multiple-choice `AskUserQuestion` the user can answer with one click.

Think of the task as a **design tree**: each decision branches into the decisions that hang off it. The
**frontier** is every open decision whose prerequisites are already settled.

## 1. Before the first question

1. Read the request, the relevant code, project docs (`CLAUDE.md`, READMEs) and memory. Facts are your job: anything
   the environment can answer (where X lives, what Y currently does, which pattern the codebase uses) is looked up,
   never asked.
2. Slow or broad lookups go to a background Explore subagent. While it runs, ask the frontier questions that don't
   depend on it; only questions downstream of the lookup wait.
3. Draft the whole design tree now, so later turns only update it.

## 2. The loop

Each turn is one `AskUserQuestion` call and nothing else: no prose before or after it, minimal thinking.

- **Batch the frontier**: put up to 4 frontier questions in one call. The dialog shows them one at a time with no
  wait between them. A question whose options depend on another answer in the same set goes in the next call.
- **Order by pruning power**: ask first the decisions whose answers rule out whole branches (purpose, approach,
  placement). Details come later, so a big answer can make them moot.
- **Progress line**: start each question's text with `[Settled N · Open ~M]`. The dialog may be all the user sees.
- After the answers, update the tree, recompute the frontier, and send the next call.

## 3. Writing a question

- Each question stands on its own: name the thing being decided and any fact you found that it rests on. Earlier
  chat may be hidden behind the dialog.
- 2–4 options, mutually exclusive (or `multiSelect: true` when choices genuinely combine). The tool adds "Other"
  itself.
- The first option is your pick, labelled `(Recommended)`, with the reason in its description. Every option's
  description states its consequence or trade-off.
- `header` is a ≤12-character chip ("Trigger", "Storage").

## 4. Pushback and delegation

- **Pushback**: when an answer looks weak, risky, or contradicts an earlier decision, ask once more: one line
  naming the concern, options "Keep <answer>" / the alternative. The second answer stands.
- **Delegation**: "you decide", "skip", "doesn't matter" → take your recommended option, tag it *Claude's call*
  in the tree, and move on.

## 5. Assumptions

Small choices you would otherwise make silently (naming, defaults, file placement that follows an obvious
convention) are not asked one by one. Collect them as you go; the final question (section 7) confirms them together
with the go-ahead.

## 6. Done criterion

You stop asking only when both hold:

- **The frontier is empty**: every branch of the tree visited.
- **Coverage**: each of these has a settled answer (asked, delegated, or one of the assumptions):
  goal and why · scope · out of scope · constraints (tech, performance, compatibility) · edge cases and failure
  behaviour · approach and structure · done criteria · verification (how it gets tested).

There is no question cap; long interviews are fine. The user can say "decide the rest" at any point, which settles
every open decision as *Claude's call*.

## 7. Finish: confirm and go in one question

1. Post a decisions summary: a table of decision → answer, with *Claude's call* items tagged so the user can
   revisit them. In plan mode, write it into the plan file too.
2. Ask ONE single-select `AskUserQuestion` that both confirms the assumptions and gives the go-ahead. Its text
   lists the assumptions as a short numbered list, each item standing on its own (the dialog may hide the chat),
   and says the decisions summary is above. Options:
   - `Accept and go (Recommended)`: every assumption and decision stands; the task starts immediately.
   - `Correct something`: the user says which assumption or decision is wrong, by number or in their own words
     (usually through "Other" or the option's notes).

   With no assumptions, the text says so and the question is just the go-ahead on the summary.
3. **Accept and go** → start the task in the same turn, with no further confirmation.
   **A correction** → if it settles the item outright ("2 should be X"), update it; otherwise it reopens that
   decision as a real question, together with the questions that hung off it. Then post the updated summary and
   ask this final question again.
4. Never start on an unclear answer: an empty, skipped or ambiguous reply is not a go. Ask once in plain text
   whether to go ahead.

Don't use a `multiSelect` "untick the wrong ones" list for the assumptions: an unanswered or skipped multi-select
comes back as "[No preference]", which can't be told apart from "all fine".
