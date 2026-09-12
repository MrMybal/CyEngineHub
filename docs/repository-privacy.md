# Repository privacy

Use the GitHub noreply email address from your account settings for commits. The
privacy check also checks annotated tag identities. Keep personal paths, private
environment files, certificates, local assistant state and runtime data outside Git.

Enable the local pre-commit check in a fresh clone:

```sh
git config user.email "YOUR_GITHUB_NOREPLY_ADDRESS"
git config core.hooksPath .githooks
```

Python 3 is required for the local check. GitHub Actions additionally scans the
reachable commit history with Gitleaks. These checks reduce accidental exposure;
they are not a substitute for reviewing staged files and release contents.

Review `git diff --cached` before committing. Use relative project paths and
synthetic screenshots. Never put credentials into reports or issue attachments.
Build outputs and release archives need a separate privacy review before uploading.
Changing Git history does not remove old installers, remote caches or third-party
clones. After an authorized history rewrite, use a fresh clone rather than merging
or pushing an old checkout, which can reintroduce removed data.

Release builds must use a fresh source snapshot and a new output directory, never
an old publication folder. `tools/release/build-windows.ps1` maps local compiler
paths to a synthetic path, excludes symbols, and checks both staging and ZIP
contents with `scripts/check-release-privacy.py`. Python 3 is required.
Additionally unpack the generated Inno installer and inspect its payload with
`--directory`; a scan of compressed installer bytes alone is insufficient. Compare
the unpacked files to the audited staging files by SHA-256 before publication.
Run Gitleaks with redaction on the outgoing history and source snapshot as well.
These checks detect known patterns and private files, not all possible personal data.

The removed Bridge key is detected by a pattern, never by including its full value
in an allowlist, test, report or scanner. `python scripts/test_privacy.py` exercises
ASCII/UTF-16 detection using synthetic fixtures only.
