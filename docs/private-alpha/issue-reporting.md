# Private alpha issue reporting

Create an issue only in the invited tester location named in your invitation.
Use the bundled [issue template](issue-report-template.md). Include the exact
Foundry, Windows, provider, and host versions; the project or sample; steps;
expected and actual behavior; and every visible diagnostic code.

For crashes or failures, open **Tools > Recovery and Diagnostics** and create a
diagnostic ZIP. It is never uploaded automatically. Review `issue-report.md`,
`system-summary.json`, `bundle-manifest.json`, and each failure report before
sharing. Paths are redacted by default. Do not attach source code, stream keys,
tokens, credentials, chat logs, or personal data unless they are essential and
you have deliberately removed unrelated information.

Classify impact as: blocking (data loss, crash, unsafe deployment), major (core
journey cannot finish), normal (workaround exists), or cosmetic. A maintainer
may ask for a smaller reproduction; they should not request an entire private
project by default.

