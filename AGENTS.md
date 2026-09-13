# Repository instructions

## Branch workflow

- Before changing tracked files, confirm that the working tree is clean and update the local `main` branch from `origin/main`.
- Create a new task branch from the latest `main` before starting implementation. Use the `codex/<short-description>` naming format unless the user specifies another branch name.
- Do not commit directly to `main`.
- Keep one logical task per branch. Continue using the existing task branch when resuming the same work.
- After the branch is merged, return to `main`, update it, and delete the merged local task branch.
- Read-only investigation that does not change repository files does not require a new branch.
