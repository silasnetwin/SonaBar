# Security policy

SonaBar records speech locally and types into other applications, so a security report
is always taken seriously.

## Reporting a vulnerability

Please **do not** open a public issue for a security problem.

Use GitHub's private reporting form instead:

1. Open the **Security** tab of this repository.
2. Choose **Report a vulnerability**.
3. Describe the problem, the affected version and the steps to reproduce it.

If the private form is unavailable, contact the repository owner directly through
GitHub and ask for a private channel.

Please include:

- the SonaBar version from **Settings → About**,
- the Windows version and build,
- whether the problem needs a specific application in the foreground,
- any log excerpt from `%LOCALAPPDATA%\SilasNet\SonaBar\Logs`, with personal text removed.

## What to expect

- An acknowledgement as soon as possible, normally within a few days.
- A short assessment of severity and affected versions.
- A fix in a private branch, followed by a release note that credits the report if you want.

## Scope

In scope:

- insertion into the wrong window or into password fields,
- clipboard handling that leaves content behind,
- data written outside `%LOCALAPPDATA%\SilasNet\SonaBar`,
- network traffic the application is not supposed to make,
- privilege escalation through the installer or the updater.

Out of scope:

- accuracy of speech recognition or of local rewriting,
- problems that require an already compromised computer,
- third-party model files, which are governed by their own licenses.

## Design commitments

- No account, no telemetry and no remote transcription.
- No listening network service; the application does not open a port.
- Update manifests require HTTPS and a SHA-256 digest per asset.
- Model downloads are pinned to SHA-256 and verified before use.
