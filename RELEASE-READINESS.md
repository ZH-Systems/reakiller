# Transflux release readiness

Status: **HOLD**. This is not a release authorization. Production tag builds fail before signing/publishing until a reviewed change removes the hold and restores signing in an isolated protected release job.

## Evidence available at M10 start

- M09 Windows CI run 45848999734 (#220): 52 tests passed, no failed tests; synthetic XML benchmark succeeded at 1, 2 and 4 workers (14,436, 39,185 and 48,430 rows/sec respectively). This is evidence for that revision only.
- M10 changes Magick.NET from 14.17.2 to 15.1.1 (upstream security fixes dated November 25, 2026). Re-run image/RVG/DICOM regression and representative fidelity validation before release.
- PdfPig exact patched alpha 0.1.17-alpha-202611230028-4a6b5 retained. Prior source ancestry verification is not package attestation.
- fo-dicom 5.2.6 and fo-dicom.Codecs 5.16.7 retained, exactly constrained. Native codec inventory and vulnerability assessment remain mandatory.

## Required evidence before release

| Gate | Status |
| --- | --- |
| CI with moderate/high/critical direct and transitive advisories blocked | Implemented, pending M10 run |
| Immutable Actions references and read-only routine build token | Implemented in build workflow |
| Review and commit lock files for all applicable target frameworks and runtime IDs; enforce locked restore | Pending |
| Complete SBOM including native embedded libraries, toolchain and self-contained .NET runtime | Pending |
| Independently verify native executable and language-data provenance; audit Ghostscript and Tesseract distributions | Pending |
| Atomic process creation/assignment and restricted filesystem/network execution for all untrusted input | Pending |
| Clean sensitive abandoned temporary files without touching live or unrelated files | Pending |
| End-to-end OCR accuracy, malformed input and process containment tests | Pending |
| Regression and image fidelity tests on authorized representative inputs | Pending |
| Clean Transflux installation and subsequent Transflux upgrade/rollback tests | Pending |
| Signed updater/MSI compatibility and signing key rotation policy | Pending |
| Reviewed signing job with least privilege, protected environment, signed artifacts and checksum | Pending |

Do not process sensitive production data with an unapproved candidate. A passing build is not a security clearance.

## Compatibility and intentional changes

M11 introduces a new MSI UpgradeCode, install directory, registry marker and executable/asset names. Uninstall ReaKiller first. Legacy PDF compression now requires reviewed Ghostscript 10.08.0 under Program Files, refuses administrator elevation and uses bounded child processes. The updater rejects a changed signing key; if the certificate service rotates keys, distribute a manually verified update until a trusted rotation mechanism is implemented. Same-user compromise is not addressed by update staging or DPAPI. Windows job containment does not enforce filesystem/network isolation. Routine builds produce unsigned CI artifacts, which must not be treated as releases.

## Release procedure (after clearance)

Review dependency graphs and lock files, confirm all gates above, approve a dedicated signing/release job with only the permissions it requires, validate an actual signed candidate on an isolated test machine, then publish the reviewed tag with the new Transflux-win-x64.msi asset name and its SHA-256 checksum. Never bypass the hold or suppress advisory errors merely to produce a release.
