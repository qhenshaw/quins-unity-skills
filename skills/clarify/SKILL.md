---
name: clarify
description: Clarify the current task by grilling the user through multiple-choice questions shown one at a time (AskUserQuestion), challenging weak answers, until no open question or ambiguity is left; then summarize the decisions and wait for a go-ahead. Use when the user explicitly asks to clarify a task before work starts ("/clarify", "clarify this task", "ask me clarifying questions", "let's clarify before you start"). A request to explain or describe something ("clarify what this function does") is a normal answer, and "grill me" belongs to the grilling skill.
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

## 5. Assumption sweep

Small choices you would otherwise make silently (naming, defaults, file placement that follows an obvious
convention) go into ONE single-select question near the end. Put the assumptions themselves in the question text
(a short numbered list, each item standing on its own), then offer:

- `All correct (Recommended)` — every assumption is confirmed.
- `Some are wrong` — the user names which (usually through "Other"); each named item becomes a real question on
  the frontier.

Don't use a `multiSelect` "untick the wrong ones" list: an unanswered or skipped multi-select comes back as
"[No preference]", which can't be told apart from "all fine". If the answer still comes back empty or unclear,
treat the assumptions as *Claude's call*, say so in the decisions summary, and move on.

## 6. Done criterion

You stop only when both hold:

- **The frontier is empty**: every branch of the tree visited.
- **Coverage**: each of these has a settled answer (asked, delegated, or a confirmed assumption):
  goal and why · scope · out of scope · constraints (tech, performance, compatibility) · edge cases and failure
  behaviour · approach and structure · done criteria · verification (how it gets tested).

There is no question cap; long interviews are fine. The user can say "decide the rest" at any point, which settles
every open decision as *Claude's call*.

## 7. Finish

1. Post a decisions summary: a table of decision → answer, with *Claude's call* items tagged so the user can
   revisit them. In plan mode, write it into the plan file too.
2. Ask "Shared understanding reached?" as an `AskUserQuestion` (Yes, go / Change something).
3. Start the task only after the user says go. "Change something" reopens that decision and re-asks the questions
   that hung off it.
