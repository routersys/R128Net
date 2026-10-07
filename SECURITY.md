# Security Policy

English | [日本語](SECURITY.ja.md)

## Supported versions

Security fixes are made only for the latest release.

| Version | Supported |
|---|---|
| Latest release | Yes |
| Earlier releases | No |

A fix is published as the version that follows the latest release. Before reporting, check that the problem also occurs on the latest release.

## What is in scope

Problems that the code or the package of R128Net causes. For example:

- Out-of-bounds memory access, memory corruption or a process crash that argument values or the contents of an audio buffer trigger through the public API, including values that the documented argument requirements are meant to reject
- A public member that reads or writes the native state after `Dispose` instead of throwing `ObjectDisposedException`
- A workflow in this repository that lets a pull request from a fork obtain write permission or read a secret

Problems that the .NET runtime, another library or the application using R128Net causes are out of scope. Report them to their developers.

A numerical difference from the original libebur128 is not a vulnerability. Open a bug report for it.

## How to report

Do not write a vulnerability in a public issue. It could be exploited before a fix is published.

1. Open `https://github.com/routersys/R128Net/security/advisories/new` and report it privately.
2. If that is not possible, report it by email to github.routersys@gmail.com.

Include the following in the report. Leave out what you do not know.

| Item | Content |
|---|---|
| Environment | The versions of R128Net and the .NET runtime, the operating system and the processor architecture on which you confirmed the problem |
| Reproduction | The calls in the order they were made, and the values that trigger the problem |
| Impact | What the problem allows |
| Workaround | A way to avoid the harm until the fix, if you know one |

## After a report

The maintainer confirms the report and replies with the outlook for a fix.

Please do not disclose the details until a version with the fix is published. The maintainer publishes the details after that.
