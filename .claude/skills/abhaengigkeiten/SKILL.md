---
name: abhaengigkeiten
description: Audit dependencies — licences, vulnerabilities, staleness — and judge what a build gate cannot. Use when adding or updating a package, when a licence or vulnerability gate fails, or when asked what the project depends on.
---

# Dependencies

A gate answers red or green. This skill answers **why**, and decides what a gate
cannot: a licence shipped only as a text file, a transitive package nobody
chose, a vulnerability with no fix.

## The rule

Only permissive licences: **MIT, BSD-2/3-Clause, Apache-2.0, PostgreSQL**.
Anything else needs a dated exception with a reason — never silence. A licence
declared only as a file is neither pass nor fail: read the text, classify it,
record the verdict. Watch for licences that look permissive but carry
commercial obligations — revenue thresholds, maintenance fees, split licences.
Those hide behind a normal-looking SPDX string, or none at all.

## Auditing

Restore first, then list **transitive** packages too — most surprises arrive
through a dependency of a dependency. Check licences against the rule (skip the
platform vendor's own framework packages), vulnerabilities at the lowest
severity the toolchain reports, and how far behind each package is. Read the
project's own gate script before writing another.

Report per finding: package, version, direct or transitive, what the licence or
advisory actually says, and a recommendation — replace, pin, except, accept.
Name the obligation in plain language ("fee above USD 10,000 revenue"), not the
licence name alone. An exception is real only with its trigger: what must
become true for it to need revisiting.

## Replacing a dependency

Existing tests are the contract; where one used the old library to build
fixtures or check results, rewrite it against the format, not the new library.
If calling code catches the old library's exception types, give the module its
own exception type first — then the next swap stops at the module.
