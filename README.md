# CDR Photo Match Pro

Windows 7 compatible C# .NET Framework 4.6.1 desktop application for finding matching CorelDRAW CDR designs from customer JPG/PNG/WhatsApp screenshots.

## What is implemented

- WinForms desktop UI with Search, Scan, Index, Settings tabs
- Recursive D:\ scanning and custom folder scanning
- Incremental indexing and full rescan
- SQLite local database
- CorelDRAW COM automation using `CorelDRAW.Application`
- Opens CDR files, reads pages/shapes, exports PNG previews, closes documents
- Design-level records: file path, folder path, page number, object number, thumbnail, descriptors
- OpenCV ORB descriptor extraction and Hamming matcher using OpenCvSharp
- Match percentage, top matches grid, thumbnail preview
- Configurable threshold with `NEW DESIGN REQUIRED`
- Double-click result to open CDR or folder

## Requirements

- Windows 7 Professional 64-bit
- .NET Framework 4.6.1 installed
- CorelDRAW X4 or newer installed and COM registered
- Visual Studio 2013 or newer
- NuGet package restore enabled

## Build

1. Open `CDRPhotoMatchPro.sln` in Visual Studio.
2. Restore NuGet packages.
3. Select `Release | Any CPU`.
4. Build solution.
5. Output EXE: `CDRPhotoMatchPro\bin\Release\CDRPhotoMatchPro.exe`.

For a portable folder, copy the Release folder including EXE, DLL files, and OpenCV native runtime DLLs. SQLite database and thumbnails are stored in `%LOCALAPPDATA%\CDRPhotoMatchPro`.

## CorelDRAW note

CorelDRAW COM names can differ slightly across versions. This project uses late-binding to maximize compatibility from X4 onward. If a specific CorelDRAW installation exposes different export constants, adjust the export filter value in `Core\CorelDrawService.cs` after testing on that machine.

## First run

1. Open Scan tab.
2. Confirm root path is `D:\`.
3. Click `Start Incremental Scan`.
4. After indexing, open Search tab and drag/drop a JPG/PNG image.
5. Click Search.
