# Specs

Every change to this repo bigger than a typo starts as a spec in this folder:
`docs/specs/NNN-<slug>.md`. The spec is the source of truth for that change.
The process and the template live in CompCode's hub repo:
`hub/docs/41-spec-driven-development.md` and `hub/templates/SPEC.md`.

## Rules for anyone building from a spec, people and agents alike

1. Read the whole spec before the first change. If the code and the spec disagree, stop and add a row to the spec's §5.
2. Take the lowest-numbered unclaimed task in §6b whose needs are met, and put your name in *Claim*.
3. Change only the files the task names. If another file must change, say so in the commit body.
4. One task = one commit. Subject: `NNN-Tn: <what changed>`.
5. Run the task's check. Pass: next task. Fail: fix it inside this task before moving on.
6. **Missing detail: ask, never guess.** If the task does not say something you need, stop that task, add a §5 row with the question, report it in the RESPONSE BLOCK, and take a task that does not depend on it.
7. Never build anything listed in §4 "Out of scope", however small.
8. Branch `spec/NNN-<slug>`. One draft PR per spec, with each task's check output pasted. Nobody pushes to the default branch.
9. Fill the RESPONSE BLOCK at the end of the spec, including PROBLEMS and STATE.
