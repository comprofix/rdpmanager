# Changelog

All notable changes to RDPManager are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and versions follow
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.2.0](https://github.com/comprofix/rdpmanager/compare/v0.1.0...v0.2.0) - 2026-11-06

First published release. Version 0.1.0 was built but not published; its features, listed under 0.1.0 below, are included.

### Added

- An application icon, shown on the window, the taskbar, the Start menu and the installer.

### Changed

- The installer is now a standard Windows Installer (MSI) wizard with welcome, licence and finish pages, and lets you install for yourself or for everyone on the computer. It replaces the one-click Setup.exe, which opened RDPManager when it finished.

## [0.1.0](https://github.com/comprofix/rdpmanager/releases/tag/v0.1.0) - 2026-11-06

First preview release for Windows.

### Added

- A connection tree of folders and RDP connections. Folders come first, then everything in alphabetical order, with numbers sorted by value.
- Rename (F2), duplicate and delete items, and move them by drag-and-drop with the destination folder highlighted.
- Settings tabs for connections and folders, with Save and Cancel. Folders pass their settings down to the connections inside them, and each field shows where an inherited value comes from.
- RDP settings for the host and port, display, security, RD Gateway, redirection (clipboard, drives, printers, sound) and the session.
- Sessions open in tabs and keep running while you switch tabs. Several can run at once.
- Full screen from the tab menu, View › Full screen, double-clicking the tab or Ctrl+Alt+Break, with a drop-down bar at the top of the screen.
- Undock a session into its own window and dock it back.
- Clear connection errors with Retry, and tabs that never connected close themselves after 10 seconds.
- Passwords stored in Windows Credential Manager, never in the connection file. Folder passwords are inherited, and the gateway can share the server's credentials.
- File, Edit, View and Help menus, and a prompt before exiting when unsaved changes or open sessions would be lost.
- A daily log file and a Messages panel listing this run's warnings and errors.
- Program settings for the theme (Light, Dark or the system setting) and an optional update check when RDPManager starts (off by default).
- Help › Check for updates, an installer that installs per user, and a portable zip that keeps its data in a `Data` folder next to it.
