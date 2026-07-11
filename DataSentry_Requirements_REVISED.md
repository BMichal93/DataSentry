# DataSentry — Technical Specification, REVISED

**Version 1.1** | Continuation of `DataSentry_Requirements.pdf` (v1.0) | Target: .NET 8, WPF, Windows

---

## 14. REVISED — Precedence and scope correction

`CLAUDE.md` is the governing document. Where v1.0 of this specification conflicts with it, `CLAUDE.md` wins and the affected v1.0 section is superseded by the corresponding section below. Everything in v1.0 not contradicted here still stands and is still binding — in particular §7 (detector specifications), §3 (non-functional requirements), and the whole of §11 (full-drive scan robustness), which remain the most detailed and most load-bearing parts of the design.

### 14.1 The product is a recommendation engine, not a report

**Supersedes §1 (Overview), §5 (Risk scoring), §6 (Results display).**

DataSentry scans a directory tree, classifies every file, and produces exactly one **recommendation** per file:

| Recommendation | Meaning |
|---|---|
| **Delete** | Junk, temp, stale, or duplicate. |
| **Retain** | Actively used, or under a legal retention obligation. |
| **Review** | Contains likely PII or special-category data and needs a human decision. |

DataSentry **recommends; the user decides.** Nothing is deleted without explicit confirmation. Deletion goes to the recycle bin, never a permanent delete, and is never the default action.

`RiskLevel` from v1.0 §5 survives, but is demoted from *output* to *input*: it is one signal feeding the recommendation, not the thing the user is shown as the answer. The grid's primary column is the recommendation; risk is secondary detail.

### 14.2 Recommendation priority order

**New. Extends §5.**

When a file matches multiple signals, the first rule that applies wins:

1. **Special-category data** (GDPR Art. 9) → **Review**. Never auto-delete, under any circumstances.
2. **Financial or identity PII** (IBAN, credit card, PESEL, passport, national ID) → **Review**.
3. **Ordinary PII** (name, email, phone, IP address) → **Review** if the file is *also* stale; otherwise flag it but recommend **Retain**.
4. **No PII** → apply the ordinary junk, staleness, and duplicate rules.

**A PII finding always overrides a Delete recommendation.** Personal data is a liability, but it may equally be under a legal retention obligation (in finance, invoices and tax records typically run 5–7 years). Never auto-delete it — surface it.

### 14.3 New detector category: Special category (GDPR Art. 9)

**Extends §2 (FR2) and §7.**

v1.0's five categories (Identity, Financial, Contact, Network, Keyword) gain a sixth, which sits above all of them in the priority order:

| Category | Detects | Validation |
|---|---|---|
| **Special** | Health, biometric, genetic, racial/ethnic origin, political opinion, religious belief, trade union membership, sex life / sexual orientation | Case-insensitive PL/EN term list from an embedded resource, confidence-scored |

This must **not** be folded into v1.0's generic `Keyword` category, which maps to `RiskLevel.Low`. Art. 9 data carries the highest consequence in the whole tool and needs its own category so it can drive rule 1 of §14.2 independently of ordinary keyword noise.

Like every other detector, this one is confidence-scored, never binary.

### 14.4 New rules: junk, staleness, duplicate

**New. No v1.0 equivalent.**

Each is its own class behind a common rule interface, so a new rule is added without touching the existing ones. The scan engine depends on the abstraction, not on the concrete rules.

- **Junk** — temp files, orphaned artefacts, known-disposable extensions and name patterns.
- **Staleness** — not modified or accessed within a threshold. Expressed to the user in plain language ("Not opened in 3 years"), never as a predicate.
- **Duplicate** — content hashing is **lazy**: hash only when a cheaper signal (identical size, similar name) already suggests a duplicate. Never hash a whole tree speculatively.

### 14.5 Persistence: SQLite + EF Core

**Supersedes §10 (Configuration & Settings), which stated that nothing is persisted between sessions.**

Scan results **are** persisted.

- **SQLite via Entity Framework Core.** No server, no connection string, no install step — the database is a file that ships with the app.
- **Code-first with migrations.** The model is C#; the schema derives from it. Every schema change gets a checked-in migration. The database is never hand-edited.
- The `DbContext` and every EF type live in `DataSentry.Data`. `DataSentry.Core` never sees EF — it talks to `IScanResultStore` and knows nothing about how results are stored.

**What is stored:** file path, size, timestamps, the recommendation, and the *types and counts* of findings ("3 IBANs, 12 email addresses").

**What is never stored:** the matched value itself. This is the one rule from v1.0 that tightens rather than relaxes. The database must not become a copy of the data it was built to police.

**Redacted snippets** (v1.0 §6's `48*********12`) are session-only: they are held in memory for the detail pane and for an export the user explicitly asks for, and they are **not** written to the database. `CLAUDE.md` permits types and counts in storage; a snippet is neither, so it stays out.

`settings.json` at `%AppData%/DataSentry/settings.json` still exists exactly as v1.0 §10 describes it, for scan options only.

### 14.6 Retention of scan results

**New. Follows from §14.5.**

GDPR Art. 5(1)(e) — storage limitation — requires data be kept no longer than the purpose needs. The purpose here is "let the user act on a scan", which is short.

- **Reports are purged 30 days after the scan.**
- Purging is **automatic**, on startup. Not a button the user has to remember to press — retention that depends on someone remembering is not retention.
- The window is a single named constant in `DataSentry.Core`. It is not a user setting.
- Purging **deletes the row.** It does not flag it.

### 14.7 Project layout

**Supersedes §5 (Architecture) project list.**

| Layer | Project | Contains | May reference |
|---|---|---|---|
| UI | `DataSentry.UI` | WPF, MVVM, presentation, result display | Core |
| Business logic | `DataSentry.Core` | Domain models, scan engine, detectors, rules, recommendation logic | *nothing* |
| Data | `DataSentry.Data` | File system access, text extraction, metadata reading, EF persistence | Core |
| Tests | `DataSentry.Tests` | NUnit suite | all |

Dependencies point **inward only**. v1.0's separate `DataSentry.Extraction` project folds into `DataSentry.Data` — file-format parsing is data access, and giving it its own project buys an abstraction with one caller.

v1.0's **key architectural rule is unchanged and non-negotiable:** `Core` has zero dependency on file IO or any format-parsing library. It operates purely on `string content` handed to it by the data layer. This is what keeps the detectors trivially unit-testable and reusable outside the desktop app.

The optional `DataSentry.Cli` from v1.0 remains optional and out of scope for v1.

### 14.8 Testing framework

**Supersedes §8's `[Theory]`/`[InlineData]` examples.**

**NUnit.** `[TestCase]` does the table-driven work v1.0 assigned to `[InlineData]`; the testing *approach* in §8 is otherwise unchanged and still binding — ≥3 known-good and ≥3 known-bad cases per detector, checksum edge cases, table-driven coverage of every category/count combination in the risk and recommendation logic, fixture files for extractors, and an integration test of the scan engine against a temp directory including clean mid-scan cancellation.

Tests use fakes and in-memory implementations of the `Core` abstractions rather than touching a real disk. Name them so a failure reads like a sentence: `Classify_FileNotModifiedInTwoYears_RecommendsDelete`.

The junk, staleness, and duplicate rules of §14.4 and the priority order of §14.2 each get the same treatment — every rule gets tests, including the edge cases (empty file, no extension, unreadable file, file locked by another process).

---

## 15. REVISED — Unchanged from v1.0, restated for emphasis

These survive the revision untouched and should not be re-litigated:

- **§7 Detector specifications** — PESEL (weighted checksum + plausible birthdate), IBAN (mod-97), credit card (Luhn, minus sequential/all-same-digit false positives), email, PL/EU phone, IPv4/IPv6, keyword term list. Detection is confidence-scored, never binary; validation is used wherever the format allows it, to cut false positives.
- **§3 Privacy** — no network calls anywhere in the app. No raw matched value written to disk, in a log, in an export, or in app state.
- **§11 Full-drive scan robustness** — in its entirety. The manual stack-based walker (never `SearchOption.AllDirectories`), the visible and editable exclusion list, no symlink/junction following, long-path support, locked files recorded as errors rather than crashes, the bounded `Channel<string>` with backpressure, two-phase progress reporting, per-file cancellation, Pause distinct from Cancel, and the drive-root confirmation dialog. A single bad file or denied folder must never abort a scan.
- **§4 Data model** — with `Recommendation` added alongside `RiskLevel` on `FileScanResult`, per §14.1.
- **§12 Distribution** — self-contained single-file `win-x64` publish.
- **§13 Roadmap** — still post-v1, still out of scope.
