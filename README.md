# DataSentry

[![CI](https://github.com/BMichal93/DataSentry/actions/workflows/ci.yml/badge.svg)](https://github.com/BMichal93/DataSentry/actions/workflows/ci.yml)

DataSentry is a GDPR-aware file cleanup tool for Windows. It scans a directory tree, classifies every file, and produces a recommendation per file:

- **Delete** — junk, temporary, stale, or duplicate files.
- **Retain** — files in active use, or likely under a retention obligation.
- **Review** — files that appear to contain personal data and need a human decision.

DataSentry recommends; the user decides. Nothing is deleted without explicit confirmation, and deletion always means the Windows recycle bin, never a permanent delete.

![The Search tab after a scan: every row says what it found and why it matters](docs/search-tab.png)

Every row is judged by the danger it carries, not by the disk space it frees. A row says what was found in the file and why that matters — and it never says what the data *is*.

## The GDPR / PII angle

Shared drives accumulate spreadsheets full of personal data that nobody remembers keeping. Finding those files is DataSentry's main job, and it follows three rules without exception:

- **Only types and counts are ever reported** — "3 phone numbers, 1 IBAN", never the matched values themselves. Not on screen, not in logs, not in the database. A tool that copies the data it was built to police would itself be a liability.
- **Personal data is never auto-deleted.** A PII finding overrides any delete recommendation, because personal data may also be under a legal retention obligation (invoices and tax records typically 5–7 years). Special category data under GDPR Art. 9 — health, biometric, political opinions, and the rest — always lands under Review.
- **Detection is confidence-scored, never binary.** Formats with a checksum are validated (mod-97 for IBAN, Luhn for payment cards, the PESEL checksum) to cut false positives.

Scan reports themselves follow storage limitation (GDPR Art. 5(1)(e)): they are purged automatically 30 days after the scan.

### The retention deadline flag

Personal data may not be kept forever, and a file holding it is not just clutter — it is data whose legal basis for being kept is running out, or already has. So for every file with a PII finding, DataSentry compares the later of its creation and last-edit dates against the typical five-year finance/tax retention period. A file inside the last six months of that period, or past it, earns an amber **Retention deadline** badge, and ordinary personal data that would otherwise have been left to rest is raised to **Review**.

DataSentry cannot know the real obligation attached to any given file — periods differ by document type and jurisdiction. The badge is therefore a prompt for a human decision, never a legal assertion: a breached retention period is a reason to review, not a licence to purge.

![A Review row, expanded: the retention deadline explained in plain language, with types and counts only — never the values](docs/review-row.png)

## Architecture

Three layers in separate projects, with dependencies pointing inward only:

| Layer | Project | Contains | References |
|---|---|---|---|
| UI | `DataSentry.UI` | WPF views, view models, composition root | Core (and Data at the composition root only) |
| Business logic | `DataSentry.Core` | Domain models, scan engine, classification rules, PII detectors | nothing |
| Data | `DataSentry.Data` | File system access, text extraction, SQLite persistence via EF Core | Core |

`DataSentry.Core` knows nothing about the file system, the database, or the UI — it talks to abstractions (`IFileSource`, `IScanResultStore`) that `DataSentry.Data` implements. That is what makes every classification rule unit-testable without touching a disk.

Each rule (junk, staleness, duplicates) and each PII detector is its own class behind a common interface, so new rules are added without touching existing ones. Scanning is streaming end to end: file contents are hashed lazily and only when a cheaper signal suggests a duplicate, and only the first part of a file is read for PII sampling.

## Build, run, test

Requires the [.NET SDK](https://dotnet.microsoft.com/download) (see `global.json` for the version) on Windows — the UI is WPF and targets `net8.0-windows`.

```powershell
dotnet build DataSentry.sln
dotnet run --project src/DataSentry.UI
dotnet test DataSentry.sln
```

There is nothing to configure: the SQLite database is a file the app creates on first run, and all defaults are chosen in code.

Scans can also run on a daily schedule via the Windows Task Scheduler (the clock icon next to the search bar); scheduled scans run headlessly and their reports appear in the Reports tab.

### A single, self-contained executable

To produce a build that runs on a Windows machine with no .NET installed — one file you can hand to someone and they double-click:

```powershell
dotnet publish src/DataSentry.UI -c Release -r win-x64
```

The output is `DataSentry.exe` (a self-contained, single-file `win-x64` build) alongside a `tessdata` folder holding the OCR language model, which Tesseract opens by path. The native libraries the app depends on — SQLite, Tesseract, and the PDF rasteriser — are bundled into the exe and unpacked on first run. The publish settings live in [`DataSentry.UI.csproj`](src/DataSentry.UI/DataSentry.UI.csproj) and switch on only when a runtime is named, so an ordinary `dotnet build` stays framework-dependent and quick.

## CI

Every push and pull request builds the solution and runs the full NUnit test suite on `windows-latest` — see [`.github/workflows/ci.yml`](.github/workflows/ci.yml).

## License

Apache 2.0 — see [LICENSE](LICENSE).
