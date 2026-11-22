# Iconic

A frosted-glass desktop icon manager for Windows 11. One icon on your desktop; click it and a glass panel opens with all your apps, files and folders, arranged in labeled, colored groups.

## Install

Double-click **Build.cmd**. It builds Iconic, installs it to `%LOCALAPPDATA%\Programs\Iconic`, puts the Iconic icon on your desktop and opens it.

If it says the .NET 10 SDK is missing, run `winget install Microsoft.DotNet.SDK.10` once, then run Build.cmd again. Run Build.cmd again any time the code changes.

## Using it

| To do this | Do this |
|---|---|
| Open an app | Click its icon (or type to search, then press Enter) |
| Rearrange | Drag icons; drop on an icon to place it before/after, or on a group to add to the end |
| Rename an icon | Right-click → Rename |
| Change an icon | Right-click → Change icon… (pick a PNG/JPG/ICO, or a program to borrow its icon) |
| New group | **+ Group** in the top bar |
| Rename / color / sort / delete a group | Double-click its name, or right-click its header |
| Reorder groups | Drag a group by its title bar up or down |
| Delete an icon | Right-click → Delete… (files Iconic stores go to the Recycle Bin; app links are just removed) |
| Keep the window open | The pin button in the top bar |
| Back up / restore your layout | ••• → Save layout… / Restore layout… (automatic daily backups too) |
| Collapse a group | The chevron on the right of the group header |
| Add more apps | Drag files in from Explorer, or ••• → Add apps or files… |
| Close the panel | Click anywhere else, or press Esc |

## Your desktop

On first launch Iconic offers to move your desktop items into the panel. They're kept in **Documents\Iconic Library**, and **••• → Put everything back on desktop** returns them. If new shortcuts appear on the desktop later, a banner offers to tidy them up.

Items on the shared "all users" desktop need admin rights to move, so they're shown in Iconic but stay on the desktop.

## Where things are stored

- Layout, groups and labels: `%APPDATA%\Iconic\library.json`
- Custom icons: `%APPDATA%\Iconic\icons`
- Moved desktop items: `Documents\Iconic Library`
- Layout backups (daily, last 14 kept): `Documents\Iconic Library\Backups`

Iconic keeps running quietly after the first launch so the panel opens instantly. **••• → Quit Iconic** closes it fully; **••• → Start with Windows** makes it ready from boot.
