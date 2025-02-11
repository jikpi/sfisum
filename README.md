# Program for Smart Generation and Verification of File Checksums

## Key features:

+ `Generate`: Generates DD files (Directory Digest - a checksum file for directories) for a specified directory
+ `Validate`: Verifies checksums for a directory using DD (with an option to save a new DD file if errors are found)
+ `Fast refresh`: Updates the DD file by generating checksums only for files that:
    - Are not in DD because they:
        - Are new
        - Were moved (found using checksum - their path in DD will be corrected)
    - Were modified (detected using modification date and file size)
+ `Full refresh`:  Like fast refresh but calculates checksums for all files.
+ `Find duplicates`

File Information Stored:

- Path
- Checksum
- Modification date
- File size

Usage: This program offers a console-based user interface. Config is saved in `sfisum.config.json`.
