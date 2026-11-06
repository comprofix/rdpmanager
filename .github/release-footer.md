---

**Downloads**

- `RDPManager-win-Setup.exe`: installs for the current user (no administrator rights needed) and updates through Help › Check for updates.
- `RDPManager-win-Portable.zip`: extract anywhere. It keeps its connections, settings and logs in a `Data` folder next to it.

The installed copy keeps its data in `%APPDATA%\RDPManager`. Passwords are stored in Windows Credential Manager.

Builds are not code-signed yet, so Windows SmartScreen may warn when you run them: choose **More info › Run anyway**. On PCs with Microsoft Defender attack surface reduction rules, you may need an exclusion for `%LOCALAPPDATA%\RDPManager` (installer) or the portable folder.
