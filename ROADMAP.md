# Transflux 2.0 roadmap

Convert. Compress. Transform.

Each milestone has one branch and one pull request. Branch from the preceding milestone head, target that branch until it merges, then retarget to main. Merge using regular merge commits; do not squash, rebase or force-push. Retain parent branches until dependent PRs are retargeted. Incorporate upstream changes via forward merges if necessary. Require passing CI before merging.

| Milestone | Branch | Acceptance criteria |
|---|---|---|
| 01 | milestone/01-rebrand | Transflux UI, CLI and MSI product branding; preserve upgrade code, legacy executable and release asset names, registry detection and event/report contracts; new icon; Windows CI and upgrade smoke test |
| 02 | milestone/02-conversion-core | Shared job model, conversion interfaces, discovery, validation, collision handling, cancellation and timestamp policy |
| 03 | milestone/03-xml-csv | Streaming XML, secure parser (no external entities), XPath, namespaces, nested entities, preview, UTF-8 CSV and fixtures |
| 04 | milestone/04-pdf-csv | Native table extraction, page selection, repeated headers, editable mapping, preview and fixtures |
| 05 | milestone/05-local-ocr | Local scanned PDF OCR, confidence reporting and explicit failure handling |
| 06 | milestone/06-desktop | Unified WPF workspace, drag/drop, dynamic options, accessible navigation and previews |
| 07 | milestone/07-job-management | Persistent local queue, pause/resume, retry and crash recovery |
| 08 | milestone/08-performance | Resource-aware concurrency, bounded memory, batch isolation and benchmarks |
| 09 | milestone/09-workflow-polish | Presets, history, notifications, reports and usability testing |
| 10 | milestone/10-release | Dependency/license audit, regression suite, signed MSI upgrade validation and release |

| 11 | milestone/11-complete-rebrand | New executable and MSI names, distinct product identity, emerald icon, clean-install verification |

## Compatibility

M11 supersedes the initial compatibility approach. Uninstall ReaKiller before installing Transflux. Future Transflux versions retain the new UpgradeCode. Repository slug remains unchanged until separately renamed. All document processing stays local; preserve sources by default.

## Release gates

Windows build/test/MSI CI, clean installation and subsequent Transflux upgrade testing, updater asset and checksum verification, CLI automation and timestamp regression checks. No release tag until these gates are met.
