# Markdown Studio

An offline Markdown editor for Windows with live preview, a file explorer, and a
picture-in-picture window. The whole application is a single HTML file, wrapped in a
small launcher (`MarkdownStudio.exe`) that gives it a real app window and access to the
file system. No build step, no dependencies, no internet connection.

## Contents

- [Features](#features)
- [Installation](#installation)
- [Usage](#usage)
- [Markdown support](#markdown-support)
- [Keyboard shortcuts](#keyboard-shortcuts)
- [Where documents are stored](#where-documents-are-stored)
- [How it works](#how-it-works)
- [Building](#building)
- [Troubleshooting](#troubleshooting)
- [Changelog](#changelog)

## Features

| Area | Highlights |
| ---- | ---------- |
| Editing | Live preview with synced scrolling, formatting toolbar, find and replace with regex, smart list continuation |
| Files | Explorer tree for any folder, tabs, create, rename, move and delete files and folders, Recycle Bin on delete |
| Documents | A personal library ("Docs") stored as real `.md` files in `Documents\Markdown Studio` |
| Pictures | Paste a screenshot with `Ctrl+V`; it is saved to an `assets` folder and linked automatically |
| Links | Relative links open as tabs; moving or renaming files offers to update links |
| Picture-in-picture | A small always-on-top window for reading or editing while another app has the screen |
| Diagrams | Built-in Mermaid flowchart renderer, exportable as PNG or SVG |
| Keyboard | VS Code key bindings, command palette, Go to file |
| Language | English and Bahasa Indonesia interface |
| Mobile | Responsive single-pane layout below 780 px |

## Installation

### Requirements

- Windows 10 or 11
- Microsoft Edge or Google Chrome (used as the app window)

### Installing

Run `MarkdownStudio.exe`. An uninstalled copy always opens setup first; cancelling exits
without leaving anything behind.

Setup does the following:

1. Copies the exe to a folder you choose. The default, `%LOCALAPPDATA%\Programs\Markdown Studio`,
   needs no administrator rights. A folder that requires them is rejected with an explanation.
2. Registers **Open with → Markdown Studio** for `.md`, `.markdown`, `.mdown`, `.mkd` and `.mdwn`.
   It does not make itself the default app.
3. Adds a Start menu entry, so the Windows search box finds it.
4. Adds an entry under **Settings → Apps → Installed apps** with an uninstaller.

All registration is per user. The installed copy opens directly into the editor.

### Command-line options

| Option | Effect |
| ------ | ------ |
| `--install [folder]` | Install without the setup window, optionally to the given folder |
| `--uninstall` | Remove the program, Start menu entry and file associations |
| `--register` / `--unregister` | Add or remove the Open with entry and Start menu shortcut |
| `--portable` | Run without installing (for development) |
| `<file or folder>` | Open that file, or that folder as the workspace |

Uninstalling keeps your documents and your settings (`%LOCALAPPDATA%\MarkdownStudio`), so a
later reinstall continues where you left off.

### Running without the exe

`index.html` also works on its own. What it can do depends on how it is opened:

| Opened via | Open Folder | Docs | `Ctrl+S` on a folder file |
| ---------- | ----------- | ---- | ------------------------- |
| `MarkdownStudio.exe` | Read and write | Files in `Documents\Markdown Studio` | Saves in place |
| `http://localhost/...` (local web server) | Read and write | Browser storage | Saves in place |
| Double-clicked `index.html` (`file://`) | Read-only | Browser storage | Downloads a copy |

Features that require the exe (the documents folder, Open with, the Recycle Bin) are hidden
in the other modes rather than shown as unavailable. An `index.html` placed next to the exe
takes precedence over the copy built into it; distribute the exe on its own.

## Usage

### Documents and folders

The sidebar has four panes:

| Pane | Contents |
| ---- | -------- |
| Explorer | The opened folder (`Ctrl+K Ctrl+O`). Lists Markdown and text files, everything inside `assets` folders, and all subfolders. `node_modules`, `.git`, `vendor`, `dist` and hidden folders are skipped |
| Docs | Your personal documents, with search |
| Outline | Headings of the current document |
| Search | Full-text search across the opened folder (`Ctrl+Shift+F`), with case and regex options |

Files from a folder are saved with `Ctrl+S` and marked as unsaved until then, as in VS Code.
Docs save automatically as you type. Line endings (CRLF or LF) and byte-order marks are
preserved on save.

### Working with files and folders

All operations are available from the right-click menu in the Explorer.

| Action | Files | Folders |
| ------ | ----- | ------- |
| Create | **New File…** or the **+** button | **New Folder…** or the folder button. Empty folders are shown |
| Rename | **Rename…** or `F2`, edited in place | **Rename…** or `F2`, edited in place |
| Move | Drag onto a folder, or onto the top bar to move it to the top level | Drag onto another folder; moves everything inside |
| Delete | **Delete** or the `Delete` key | **Delete** or the `Delete` key; the dialog lists what it contains |
| Other | Reveal in File Explorer, Copy Path, Copy Relative Path, Insert into document (assets) | Copy Path, Copy Relative Path |

Rules applied to every operation:

- An existing file or folder is never overwritten. The operation is refused instead.
- A document keeps a Markdown extension; typing a name without one adds `.md`.
- A folder cannot be moved into itself. Changing only the letter case of a name is supported.
- Files inside an `assets` folder can only move to another `assets` folder, and an `assets`
  folder cannot be renamed, because pictures are only listed inside folders with that name.
- During a drag, the target folder is highlighted, holding over a closed folder opens it,
  and `Esc` cancels.
- Deleted files and folders go to the Recycle Bin. If the Recycle Bin refuses, nothing is
  deleted and an error is shown. In a folder opened in a browser (no exe) deletion is permanent,
  and the confirmation says so.

### Links after moving or renaming

| Situation | What happens |
| --------- | ------------ |
| A file is moved | Its relative links are rewritten so they reach the same targets from the new location |
| A folder is moved | Links between files inside the folder are left unchanged; links to files outside are rewritten |
| A file or folder is renamed or moved | Other documents that link to it are found, and you are asked once whether to update them |

Links that still point to the right file are left exactly as written. Open files receive
the update in the editor and are left unsaved; closed files are written directly, keeping
their line endings. Anchors (`#heading`) and external URLs are never changed.

### Tabs

- Each open file gets a tab. Drag tabs to reorder them, or use `Ctrl+Shift+PageUp/PageDown`.
- Drag a tab onto a folder in the Explorer to move its file there, or onto the Docs pane to
  turn it into a Doc. A Doc dropped on a folder becomes a file in that folder.
- **Copy Tab** (right-click a tab) and **Paste Tab** (right-click an empty part of the tab bar)
  create a new Doc with the same content. The original is not affected.
- Pictures referenced by a moved or copied document are copied along when they would
  otherwise be out of reach, and the links are adjusted.

### Pictures and the assets folder

Take a screenshot (`Win+Shift+S`) and press `Ctrl+V` in the editor. The image is saved as
`assets/<document>-<date>-<time>.png` next to the document and a link is inserted on its own
line. Dropped image files keep their original names. Existing names are never overwritten.

Without a writable folder (a browser-only document or a read-only folder), the image is
embedded in the document as a reference at the end of the file.

`assets` folders show all of their contents in the Explorer. The Docs pane lists the
library's pictures and marks those not used by any document. **Export HTML** embeds all
pictures, so the exported file is self-contained.

### Picture-in-picture

Enable it under **Settings → Window**, then open it with the toolbar button, `Ctrl+Alt+P`,
or the menu.

- **Preview** mode follows the document; ticking a task updates the source.
- **Edit** mode is a small editor that writes to the same document, so autosave, `Ctrl+S` and
  unsaved markers work as usual. Edits made in either window appear in the other.
- Switching between Preview and Edit keeps the reading position.
- The window follows document and theme changes. The **↩** button returns to the main window.

Run from the exe, the floating window is an app window titled "Markdown Studio", pinned on
top in the bottom-right corner (420 × 560). It has no taskbar button or Alt+Tab entry, stays
visible when the main window is minimized, and closes with the main window. Minimizing the
floating window closes it. In a plain browser the browser's own Document Picture-in-Picture
is used instead (Edge or Chrome 116+), which shows the page address in its title bar.

### Settings and language

**Settings** (`Ctrl+,`) contains the documents folder, word wrap, scroll sync, wide layout,
text sizes, theme, language and picture-in-picture.

The interface is available in English and Bahasa Indonesia, or follows the system language.
Documents are never translated. The Welcome guide (**⋮ menu → Welcome guide**) is written in
both languages and covers the application and Markdown syntax with side-by-side examples.

### More than one window

Opening another file while the app is running opens a second window served by the same
process. Docs and settings changed in one window are applied in the others immediately.
Text being typed in a window is never replaced by another window's copy.

## Markdown support

| Element | Notes |
| ------- | ----- |
| Headings | ATX and setext |
| Emphasis | Bold, italic, strikethrough, `==highlight==`, subscript, superscript |
| Lists | Bullet, numbered, nested, task lists (clickable in the preview) |
| Tables | Column alignment, escaped pipes |
| Code | Fenced blocks with highlighting for about 25 languages |
| Callouts | `> [!NOTE]`, `[!TIP]`, `[!WARNING]` and other GitHub-style callouts |
| Links and images | Inline, reference, autolinks, footnotes; image alt text is plain text |
| HTML | A safe subset (`<details>`, `<kbd>`, `<mark>`, ...). Scripts and event handlers are removed. `<details>` may contain Markdown |
| Line breaks | Trailing `\` or two spaces; `<!-- comments -->` are not rendered |

Element ids generated from the document receive an `md-` prefix in the preview (as GitHub
uses `user-content-`), so document content cannot collide with the application's own ids.
Links such as `[x](#heading)` still work, and exported HTML uses the plain ids.

### Diagrams

Code blocks marked `mermaid` are rendered as flowcharts by a built-in renderer.

- Directions: `TD`/`TB`, `LR`, `RL`, `BT`
- Node shapes: box, rounded, pill, subroutine, database, circle, decision, hexagon,
  parallelogram, flag
- Edges: `-->`, `---`, `-.->`, `==>`, `--x`, `--o`, `<-->`, with labels, chains and branches
- `subgraph`, comments, `classDef`, `class`, `:::`, `style`, and `<br/>` in labels
- Export as PNG (2× resolution) or SVG in the current theme

Only flowcharts are supported. Full Mermaid would add about 3 MB, more than ten times the
size of the application. A diagram that cannot be parsed shows the error and its source.

## Keyboard shortcuts

Shortcuts follow VS Code and its *Markdown All in One* extension. `Ctrl+K` starts a two-key
chord. `Ctrl+K Ctrl+S` shows the full list.

| Action | Keys | Action | Keys |
| ------ | ---- | ------ | ---- |
| Go to file | `Ctrl+P` | Bold / italic | `Ctrl+B` / `Ctrl+I` |
| Command palette | `Ctrl+Shift+P`, `F1` | Strikethrough | `Alt+S` |
| Go to heading | `Ctrl+Shift+O` | Inline code | ``Ctrl+` `` |
| Go to line | `Ctrl+G` | Heading level up / down | `Ctrl+Shift+]` / `Ctrl+Shift+[` |
| Find / replace | `Ctrl+F` / `Ctrl+H` | Heading 1 to 6 | `Ctrl+Alt+1` to `6` |
| Next / previous match | `F3` / `Shift+F3` | Toggle task | `Alt+C` |
| Search in folder | `Ctrl+Shift+F` | Bullet / numbered list | `Ctrl+Shift+8` / `Ctrl+Shift+7` |
| Show Explorer | `Ctrl+Shift+E` | Blockquote | `Ctrl+Shift+.` |
| Toggle sidebar | `Ctrl+B` (outside the editor) | Toggle comment | `Ctrl+/` |
| Toggle preview | `Ctrl+Shift+V` | Select next match | `Ctrl+D` |
| Preview to the side | `Ctrl+K V`, `Ctrl+\` | Select line | `Ctrl+L` |
| Editor / split / preview | `Ctrl+1` / `2` / `3` | Delete line | `Ctrl+Shift+K` |
| Word wrap | `Alt+Z` | Move line | `Alt+Up` / `Alt+Down` |
| Dark mode | `Ctrl+K Ctrl+T` | Copy line up / down | `Shift+Alt+Up` / `Down` |
| Settings | `Ctrl+,` | Insert line below / above | `Ctrl+Enter` / `Ctrl+Shift+Enter` |
| New document | `Ctrl+N`, `Alt+N` | Indent / outdent | `Ctrl+]` / `Ctrl+[` |
| Open file / folder | `Ctrl+O` / `Ctrl+K Ctrl+O` | Cut / copy line | `Ctrl+X` / `Ctrl+C` (no selection) |
| Save / save as / save all | `Ctrl+S` / `Ctrl+Shift+S` / `Ctrl+K S` | Rename | `F2` |
| Close / close all | `Ctrl+W` / `Ctrl+K Ctrl+W` | Copy path / relative path | `Shift+Alt+C` / `Ctrl+Shift+Alt+C` |
| Reopen closed tab | `Ctrl+Shift+T` | Reveal in File Explorer | `Shift+Alt+R` |
| Next / previous tab | `Ctrl+PageDown` / `Ctrl+PageUp` | Picture-in-picture | `Ctrl+Alt+P` |
| Move tab left / right | `Ctrl+Shift+PageUp` / `PageDown` | Jump to tab | `Alt+1` to `9` |

The browser may intercept `Ctrl+N`, `Ctrl+W`, `Ctrl+Tab` and `Ctrl+Shift+T`. Alternatives
that always work: `Alt+N`, `Ctrl+Alt+W`, `Ctrl+Alt+Right/Left`, `Ctrl+Alt+S` (save all) and
`Ctrl+Alt+Shift+W` (close all).

## Where documents are stored

### The documents folder

When run from the exe, every Doc is a regular `.md` file in `Documents\Markdown Studio`,
written as you type. The location is resolved per user, so redirected or OneDrive-synced
Documents folders work. Change it under **Settings → Change folder…**; existing documents
move with it, and name conflicts are resolved by adding a number (`Notes 2.md`).

Because Docs are files:

- They survive clearing browser data, and Windows Search indexes their names and contents.
- A `.md` file added to the folder appears in Docs at the next start.
- Renaming a Doc renames its file. Deleting a Doc sends its file to the Recycle Bin.
- Deleting or renaming the file outside the Docs pane (in Windows Explorer, or in the
  Explorer pane with the documents folder open) updates the Docs list accordingly.
- If the folder is empty at startup (moved, disconnected or not yet synced), the documents
  are written back so that nothing is lost.
- If the chosen folder is unavailable, the app uses `Documents` for that session and keeps
  your setting.

Avoid editing the same document in two places at once. If the documents folder is opened in
the Explorer pane, the file tab takes precedence and the Doc's autosave pauses while it is open.

Without the exe, Docs are kept in browser storage for that browser only. Use `Ctrl+S` to save
a document as a file, or **Download backup** to export all Docs as JSON.

### Application data

| Location | Contents |
| -------- | -------- |
| `%LOCALAPPDATA%\MarkdownStudio\profile` | Browser profile used for the app window |
| `%LOCALAPPDATA%\MarkdownStudio\library` | The chosen documents folder |
| `%LOCALAPPDATA%\MarkdownStudio\instance` | Port and access key of the running instance |

## How it works

The launcher starts a local HTTP server on `127.0.0.1:41873` and opens Edge or Chrome in app
mode with a dedicated profile. The fixed port keeps the browser origin stable, so stored
settings persist between sessions; if the port is taken, a free one is used for that session.
The server stops when the last window closes and never listens on the network.

The page reaches the file system through the endpoints below. Each request must carry a
random key generated at startup, and paths outside the workspace are rejected.

| Endpoint | Purpose |
| -------- | ------- |
| `/__ws` | Workspace listing: documents, folders, assets |
| `/__file` | Read, write and delete documents |
| `/__all` | Read all documents at once (folder search) |
| `/__asset` | Read and save pictures. Existing names are never overwritten; SVG is served with a restrictive content policy |
| `/__entry` | File and folder operations: `rename`, `move`, `mkdir`, `renamedir`, `movedir`, `rmdir`, `reveal`, delete |
| `/__float` | Open, close and focus the picture-in-picture window (only windows of the app's own browser profile) |
| `/__launch`, `/__libdir`, `/__reveal` | Second windows, documents folder selection, opening Explorer |

Only Markdown and text files, and the contents of `assets` folders, can be read or changed.
Deletion uses the Windows Recycle Bin through `SHFileOperation`, with the structure layout
matching the process architecture.

## Building

```bash
.\build-exe.cmd
```

The script compiles `MarkdownStudio.cs` with the C# compiler included in Windows and embeds
the current `index.html` and `icon.ico`. Building while the app is running is supported: the
running exe is renamed and replaced, and removed by the next build. A failed build leaves the
existing exe untouched.

### Icon

| File | Use |
| ---- | --- |
| `icon-v2.jpg` | 2048 × 2048 source image |
| `icon.ico` | Embedded in the exe; nine sizes from 16 to 256 px |
| `index.html` | The same image as PNG for the window icon (32 and 256 px) and the in-app logo (64 px) |

The icon is cropped so that the point of the M's inner V sits on the vertical centre line.
After changing it, rebuild and reinstall. Windows may show the old icon until its icon cache
refreshes (sign out and back in).

## Troubleshooting

| Symptom | Cause and solution |
| ------- | ------------------ |
| "Something went wrong: ..." message | An unexpected error; the message names the failing step. Include it when reporting the problem. Other features keep working |
| Changes do not appear after rebuilding | An `index.html` next to the exe overrides the built-in copy, or the old version is still running. Close the app and reinstall |
| Open Folder is read-only | The page was opened from `file://`. Use the exe or a local web server |
| Old icon on shortcuts | Windows icon cache; sign out and back in |

## Changelog

### 1.16.0

**Fixed**
- Deleted files never reached the Recycle Bin on 64-bit Windows. `SHFILEOPSTRUCT` was declared
  with 32-bit packing, `SHFileOperation` rejected it (error 0x57), and the launcher fell back to
  permanent deletion. Both layouts are now declared and selected at runtime. If recycling fails,
  nothing is deleted. Files deleted with earlier versions are not in the Recycle Bin.
- A Doc whose file could not be deleted disappeared from the list and returned at the next start.
- Renaming a Doc no longer places a copy of the old file in the Recycle Bin.
- Picture-in-picture did not receive edits made in the main window, and typing in it then
  overwrote them.
- Moving files rewrote links that were already correct.

**Added**
- New folders (button, folder menu, empty-area menu); empty folders are listed.
- Drag and drop of files and folders between folders.
- Rename and delete for folders.

### 1.15.0

**Added**
- Drag a tab onto a folder or the Docs pane to change where its document is stored.

**Fixed**
- Deleted documents reappeared at startup when deleted outside the Docs pane.
- Two open windows overwrote each other's Docs and settings.
- Moving a file broke its links to other documents.
- The picture-in-picture window could lose its always-on-top state.
- A dragged tab could be dropped on a folder it had only passed over.
- Tab names were selected as text while dragging.
- Installed apps showed an outdated version number.
- Duplicate Indonesian translations and unused code removed after a full ESLint check.

### 1.14.0

- Tabs can be reordered by dragging or with `Ctrl+Shift+PageUp/PageDown`.
- Copy Tab and Paste Tab.
- The tab bar stays visible with a single document open.

### 1.13.3

- Picture-in-picture keeps the reading position when switching between Edit and Preview.
- New application icon with nine sizes.

### 1.13.2

- The picture-in-picture window is titled "Markdown Studio" instead of showing the page address.
- Minimizing the picture-in-picture window closes it instead of hiding it unreachably.
- Edge no longer offers to translate the page.

### 1.13.1

- The picture-in-picture window no longer appears as a second app in Alt+Tab and the taskbar.

### 1.13.0

- Added picture-in-picture.
- Fixed: Settings did not open while a document had a heading named "Settings". Document ids
  now carry an `md-` prefix.

### 1.12.0

- Added a right-click menu with rename, delete, reveal and copy path, and link updates on rename.
- Keyboard shortcuts now follow VS Code, with Go to file and a command palette.
- Fixed: New File overwrote an existing file with the same name.

### 1.11.1

- Fixed: Find did not advance to the next match.
- Fixed: byte-order marks were lost on save.
- Fixed: content of `<details>` blocks rendered outside the fold.
- Fixed: image alt text kept Markdown syntax.
- Fixed: a second Welcome document was created for existing libraries.

### 1.11.0

- Fixed: the application failed to start after the assets update.
- Exe-only settings are hidden when the page runs without the exe.
- Startup errors are shown and no longer stop the remaining startup steps.
- The Explorer reveals the `assets` folder created by the first pasted picture.
