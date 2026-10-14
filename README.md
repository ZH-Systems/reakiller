# ReaKiller

Personal Windows command-line image converter for IMG, DICOM, JPEG, and TIFF to PNG.

## Important sample finding
All 18 IMG files provided in test.7z are **standard JPEG files with an IMG extension**. The program recognizes JPEG headers inside IMG files. Other IMG encodings are not guaranteed to work.

## Build (Windows with .NET 10 SDK)

```powershell
dotnet restore .\ReaKiller\ReaKiller.csproj
dotnet publish .\ReaKiller\ReaKiller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

## Interactive console

Run `ReaKiller.exe` without arguments to enter the guided console. It asks for the source folder, whether to replace originals in place, the output folder when applicable, recursion, worker count, maximum width, and overwrite policy. A text progress bar displays conversion progress.

**In-place replacement**: `--in-place` saves verified PNG output beside each source and deletes the original only when all pages converted successfully. Existing output PNGs are not overwritten unless `--overwrite` is specified. Make a backup first: deleting originals can remove source metadata and embedded DICOM information.

## Usage

```cmd
ReaKiller.exe convert "D:\Migrations\Thornton\romexis_images" --output "D:\Migrations\Thornton\Converted" --recursive --threads 4
ReaKiller.exe convert "D:\Images\I11624.img" --output "D:\Converted"
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --recursive --max-width 2048
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --dry-run
```

Supports extensions: .img, .dcm, .dicom, .jpg, .jpeg, .tif, .tiff. Options include --recursive, --threads N, --overwrite, --dry-run, --max-width, --max-height. Output names include the source extension to avoid collisions (e.g. I11624_img.png). Multipage output has page-number suffixes. Results are logged to reakiller-report.csv. Originals are preserved by default. With `--in-place`, the source file is deleted after validated PNG conversion. Output must be outside the input directory tree.

## Limitations

PNG is lossless. Converting a compressed JPEG to PNG commonly makes it larger and cannot restore discarded JPEG detail. Resizing is optional and reduces resolution. The --compression lossless profile uses PNG zlib level 6 and adaptive filtering; --compression max uses level 9 with adaptive filtering. Both preserve pixel data without quality loss.

DICOM files are first parsed by fo-dicom to confirm valid datasets with PixelData. Image rendering still uses ImageMagick's supported transfer syntaxes. DICOM VOI/windowing, inversion, multi-frame behavior, and pixel-depth preservation must be compared with known-good references before clinical use. Unrecognized IMG formats are rejected. No live .NET build or conversion tests were possible in this execution environment. No patient images are committed.

## Automated tests and CI

`dotnet test ReaKiller.Tests/ReaKiller.Tests.csproj -c Release` runs regression cases for JPEG-signature IMG conversion, default input preservation, in-place removal on success, collision skipping, and corrupted inputs. `.github/workflows/build.yml` runs restore/build/tests and publishes a Windows x64 self-contained executable as a workflow artifact if successful. This repository has not yet been verified with a successful Windows build in this session.

## Timestamp preservation

Every generated PNG inherits its source file's filesystem creation time, last-modified time, and last-access time (UTC-based operations), where supported by the filesystem. Timestamps are captured before image decoding and assigned before in-place originals are removed. On filesystems with coarse or unsupported timestamp metadata, values may differ due to OS/filesystem behavior. These are filesystem timestamps, not embedded DICOM acquisition metadata. Existing converted files need re-conversion with `--overwrite` to update their timestamps. Regression tests cover both output-folder and in-place conversion.

## Windows desktop interface

A new WPF interface is in `ReaKiller.Desktop`, styled in an Akitu-inspired blue palette. It includes source-folder browsing, destination selection, optional in-place replacement, recursive scanning, overwrite control, compression profile, thread count, optional maximum width, progress bar, logs, and cancellation. The interface launches the existing CLI converter so unattended CMD workflows remain available.

Build both projects via the Windows GitHub Actions workflow and download the `ReaKiller-Desktop-win-x64` artifact. Extract the **whole bundle** and launch `ReaKiller.Desktop.exe`. Keep `ReaKiller.exe` in the same folder. You can continue to run `ReaKiller.exe` from CMD or PowerShell separately.

The desktop design uses a suggested blue palette rather than a verified official Akitu brand specification. The Windows build and runtime behavior have not yet been verified in this session.

### Desktop UI refresh

The desktop layout now has an Akitu-inspired navy-to-blue header, three clear sections (paths, settings, activity), a persistent action bar, image counting via **Scan images**, a live progress indicator, output logs, an **Open report** action and cancellation. Controls are locked while a conversion runs to prevent changing active settings. In-place replacement retains the existing explicit confirmation. The converter CLI remains unchanged and still handles all image processing and timestamp preservation. Windows compilation and end-to-end testing are required before relying on this updated desktop release.

## Optional image enhancement (desktop and CLI)

The desktop interface now has optional **Auto contrast**, **Contrast** (-50 to +50), and **Sharpening** (0.0 to 2.0) controls. All are off by default. The CLI allows a wider contrast range (-100 to +100).

```cmd
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --recursive --contrast 15 --sharpen 0.6
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --auto-contrast
```

Contrast uses Magick.NET brightness/contrast with unchanged brightness. Sharpening uses unsharp masking. Auto contrast uses automatic level stretching. Enhancements affect the PNG pixels and can exaggerate artifacts in medical radiographs. Do not enable these by default for clinical migrations; retain source images and inspect representative outputs. The current implementation has no before/after preview or reusable enhancement presets. These remain potential usability improvements.

The enhancement implementation and associated unit test are committed but are not confirmed by a successful Windows CI build in this session.

## Live desktop progress and file results

The desktop now displays a native WPF progress bar and a live, scrolling results grid. The conversion engine emits one structured `REAKILLER_EVENT:` JSON line per completed source file. Each result is shown as green **Succeeded**, red **Failed**, or gray **Skipped**, with failure details where available. The grid keeps the latest 2,000 results for responsiveness; the full CSV report is retained. The raw console stream is available under **Technical output** for debugging. This also works when multiple worker threads finish in a different order.

The Windows build and new preview UI still require an end-to-end smoke test; the prior working CLI remains accessible.

## Output filename patterns

The desktop UI has an **Output file naming** field with a live example, and patterns are included in saved JSON presets. The CLI accepts `--name-pattern`.

| Token | Meaning |
| --- | --- |
| `{name}` | Original filename without extension |
| `{ext}` | Original extension (without dot) |
| `{index}` | Sequential five-digit number, ordered by source path |
| `{date}` | Source file last-modified date formatted yyyyMMdd |

Default: `{name}_{ext}`, preserving existing behavior. The output extension `.png` is appended automatically. Multipage inputs still receive page suffixes.

Examples:

```cmd
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --recursive --name-pattern "Dental_{index}_{name}"
ReaKiller.exe convert "D:\Images" --in-place --recursive --name-pattern "{date}_{name}"
```

The converter checks the resolved names for duplicates before processing, and refuses conflicts rather than silently overwriting converted files. Existing files continue to respect `--overwrite`. As before, backups are recommended with in-place mode. Build and integration validation remain pending.

## Stop conversion (desktop)

The desktop footer provides a red **Stop conversion** button while a batch is running. It writes a per-job temporary stop marker and asks the converter to finish or abandon in-flight image processing without starting additional output commits. Completed PNGs remain; source files that have not completed in-place conversion remain untouched. A stopped file is reported as STOPPED with exit code 3 when applicable. The same CLI also accepts `--stop-file <path>`. This is cooperative cancellation rather than immediate process termination, so an active decode may continue for some time before the batch stops. Windows build/runtime verification is still required.

## Windows publisher metadata and executable signing

Both Windows binaries now declare **ZH-Systems** as their Company and Authors in the .NET project metadata. This controls product/company details but is **not** a trusted Windows digital signature.

To Authenticode-sign the actual executables, obtain a publicly trusted code-signing certificate issued to the applicable legal identity (with the desired ZH-Systems publisher name, subject to CA identity verification). For a PFX certificate that supports export and CI usage, configure these GitHub Actions **repository secrets**, never commit the PFX or password:

- `ZH_CODESIGN_PFX_BASE64`: Base64-encoded PFX (PowerShell on Windows: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("C:\\secure\\codesign.pfx"))`).
- `ZH_CODESIGN_PFX_PASSWORD`: Password for that PFX.

On pushes to `main`, the workflow signs `ReaKiller.exe` and `ReaKiller.Desktop.exe`, timestamps the signatures, verifies them using Windows SignTool, and uploads the bundle. If the secrets are missing, the build is **unsigned**. PR builds remain unsigned. Modern CA-issued code-signing private keys may need hardware-backed custody or hosted signing rather than an exportable PFX; in that case, this workflow must be adapted to the certificate provider's signing service.

Confirm with Windows file Properties → Digital Signatures, or `Get-AuthenticodeSignature .\\ReaKiller.Desktop.exe`. A matching Company field alone does not suppress SmartScreen warnings. Code has been committed but the signed CI output has not been validated.

## Source selection: folder or individual files

Use the **Select individual files instead of a folder** toggle in the desktop UI. In individual-file mode, **Browse...** opens a multi-select file picker for IMG, DCM, DICOM, JPG, JPEG, TIF, and TIFF files. Dragging multiple images onto the window also works in this mode. Only those selected files are converted, and the preview uses the first selected image. Turning the toggle off restores whole-folder processing and the recursive scan control.

For automation, the CLI accepts a UTF-8 text file containing one absolute image path per line:

```cmd
ReaKiller.exe convert "D:\Images\first.img" --files-from "D:\Images\selection.txt" --output "D:\Converted" --name-pattern "{index}_{name}"
```

With selected files, non-in-place outputs go directly into the chosen output directory rather than inheriting each file's original directory. Duplicate resolved names are rejected before processing. In-place conversion creates PNGs beside their respective originals and preserves the current source-preservation rules.
