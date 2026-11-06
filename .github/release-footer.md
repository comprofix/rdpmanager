---

**Downloads**

- The `.msi` installer: a standard setup wizard. Choose to install for yourself (no administrator rights needed) or for everyone on the PC (Program Files, needs administrator rights). Updates come through Help › Check for updates.
- `RDPManager-win-Portable.zip`: extract anywhere. It keeps its connections, settings and logs in a `Data` folder next to it.

The installed copy keeps its data in `%APPDATA%\RDPManager`. Passwords are stored in Windows Credential Manager.

Builds are not code-signed yet, so Windows SmartScreen may warn when you run them: choose **More info › Run anyway**. On PCs with Microsoft Defender attack surface reduction rules, you may need an exclusion for the install folder (`%LOCALAPPDATA%\RDPManager` for yourself, or the Program Files folder for everyone) or the portable folder.
