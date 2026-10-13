# ReaKiller

Personal Windows command-line image converter for IMG, DICOM, JPEG, and TIFF to PNG.

## Important sample finding
All 18 IMG files provided in test.7z are **standard JPEG files with an IMG extension**. The program recognizes JPEG headers inside IMG files. Other IMG encodings are not guaranteed to work.

## Build (Windows with .NET 10 SDK)

```powershell
dotnet restore .\ReaKiller\ReaKiller.csproj
dotnet publish .\ReaKiller\ReaKiller.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o .\publish
```

## Usage

```cmd
ReaKiller.exe convert "D:\Migrations\Thornton\romexis_images" --output "D:\Migrations\Thornton\Converted" --recursive --threads 4
ReaKiller.exe convert "D:\Images\I11624.img" --output "D:\Converted"
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --recursive --max-width 2048
ReaKiller.exe convert "D:\Images" --output "D:\Converted" --dry-run
```

Supports extensions: .img, .dcm, .dicom, .jpg, .jpeg, .tif, .tiff. Options include --recursive, --threads N, --overwrite, --dry-run, --max-width, --max-height. Output names include the source extension to avoid collisions (e.g. I11624_img.png). Multipage output has page-number suffixes. Results are logged to reakiller-report.csv. The original files are never modified or deleted. Output must be outside the input directory tree.

## Limitations

PNG is lossless. Converting a compressed JPEG to PNG commonly makes it larger and cannot restore discarded JPEG detail. Resizing is optional and reduces resolution. The --compression max and --compression lossless options currently use the same lossless PNG encoding, not different compression levels.

DICOM decoding depends on ImageMagick's supported transfer syntaxes, and image presentation/windowing and pixel-depth preservation need to be verified against reference results before any clinical use. Unrecognized IMG formats are rejected. No live .NET build or conversion tests were possible in this execution environment. No patient images are committed.
