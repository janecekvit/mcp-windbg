# Smoke test — terylon-github adapter

This file exists only to give a pull request something to diff. It is created by a
smoke test of the `terylon-github` adapter in the Terylon marketplace, which had
never run its write path against a live repository.

**Delete this branch and this file once the test is done.** Nothing here belongs
in `main`.

What the test exercises, in order:

| Step | Operation |
|---|---|
| 1 | `check-preconditions` — `gh auth status` |
| 2 | `create-pull-request` — `gh pr create --body-file` |
| 3 | `update-pr-description` — `gh pr edit --body-file` |
| 4 | `post-pr-thread` — a pull-request-wide comment |
| 5 | `list-threads` — read back what step 4 wrote |

The `--body-file` path is the point: the GitHub body of `forge-ops` forbids inlining
a body, because a description carrying backticks, quotes and newlines does not
survive shell quoting intact.
