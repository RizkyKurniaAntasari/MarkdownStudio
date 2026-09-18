# Markdown Studio

A complete offline Markdown editor with live preview. One HTML file, no build step,
no dependencies, no internet connection required.

## Running it

**Double-click `MarkdownStudio.exe`.** This is the best way to use it: you get a real
app window with no address bar, its own taskbar icon, and — because it serves the page
over `http://127.0.0.1` instead of `file://` — **Open Folder and saving to disk work
fully, with no laragon needed**.

The exe is self-contained (the whole app is baked into it), so you can copy it to the
Desktop, a USB stick, anywhere. If an `index.html` happens to sit next to it, that one
is used instead, so you can tweak the app without rebuilding.

It picks a free loopback port, opens Edge (or Chrome) in app mode with its own profile
in `%LOCALAPPDATA%\MarkdownStudio`, and shuts the server down when you close the window.
Nothing is installed and nothing listens on the network — only `127.0.0.1`.

To rebuild it after editing `index.html`, run `build-exe.cmd`. It uses the C# compiler
that already ships with Windows, so there is nothing to install.

### Or just open the HTML

**Double-click `index.html`.** It opens in your default browser and works fully
offline, but browsers refuse disk access to `file://` pages, so Open Folder is
read-only and `Ctrl+S` downloads instead of saving in place.

But if you want to **edit files on your disk in place**, start laragon and open it at
`http://localhost/MDpreviewer/` instead. Browsers only hand out write access to a real
folder on an `http(s)` origin — never on a `file://` page. Both modes are offline; the
difference is only what saving does:

| Opened via | Open Folder | First `Ctrl+S` | Later `Ctrl+S` |
| ---------- | ----------- | -------------- | -------------- |
| **`MarkdownStudio.exe`** | Full read/write tree | Asks where to save | Writes to that file, no dialog |
| `http://localhost/MDpreviewer/` (laragon) | Full read/write tree | Asks where to save | Writes to that file, no dialog |
| Double-clicked `index.html` | Read-only tree | Downloads a copy | Downloads a copy |

The app tells you which mode you are in — a read-only folder is labelled as such in
the Explorer, and the status bar always names the current state.

**Saving anywhere you like.** Served over http, the first `Ctrl+S` on a new document
opens the system save dialog, so you can put the file on the Desktop or anywhere else.
After that the document is *bound* to that file: every later `Ctrl+S` writes straight
to it with no dialog — it just syncs. Editing again marks it unsaved until you do.

On a `file://` page browsers refuse the save dialog outright, so saving falls back to a
download. Chrome's **"Ask where to save each file before downloading"** setting is the
closest you get to choosing a folder there; serving through laragon is the real fix.

The whole app is one file, `index.html` (~157 KB). Copy it to a USB stick, email it,
drop it on any machine with a browser — it keeps working.

## What it does

**Workspace (VS Code style)**
- **Open Folder** (`Ctrl+Shift+O`) loads a whole folder into an Explorer tree
- Nested folders expand on click; heavy folders (`node_modules`, `.git`, `vendor`,
  `dist`, ...) are skipped, and only Markdown and text files are listed
- Open as many files as you like — each gets a **tab** across the top
- Unsaved tabs show a dot and italic name; closing one asks first
- `Ctrl+S` saves the current file, `Ctrl+Alt+S` saves every changed file
- `Alt+1`–`9` jumps to a tab, `Ctrl+Alt+←/→` cycles, `Ctrl+Alt+W` closes
- The folder is remembered, so you can reopen it in one click after a reload
- **New file** and **Refresh** buttons at the top of the tree

**Editing**
- Live preview that updates as you type, with scroll synced between the two panes
- **Select text in the editor and the preview highlights the matching block** and
  scrolls to it if it is off screen — select a whole list and it highlights as one
  block, select one item and only that item lights up. Toggle it with
  **Menu → Sync scroll & selection**
- Formatting toolbar plus keyboard shortcuts (`Ctrl+B`, `Ctrl+I`, `Ctrl+K`, ...)
- Smart Enter: continues bullet, numbered, task and quote lists; empty item ends the list
- `Tab` / `Shift+Tab` to indent, `Alt+Up/Down` to move lines, `Ctrl+D` to duplicate
- Find and replace (`Ctrl+F`) with case-sensitive and regex modes. Every hit is
  highlighted, the current one is outlined, and the view scrolls to it — focus stays
  in the search box so you can keep typing and press `Enter` to step through matches
- **Search works in Preview mode too**, against the rendered text rather than the
  Markdown source, so a phrase still matches when it runs across bold or a link.
  Preview is read-only, so replace is switched off there and the bar says so.
  Switching views hands the search over to whichever pane is on screen
- Three view modes: editor only, split, preview only (`Ctrl+1/2/3`)
- **Unsaved changes are hard to miss**: an orange dot and italic name on the tab, a dot
  in the file tree, a badge on the Save button, an orange "Unsaved changes" in the
  status bar, and a ● in the browser tab title. Hover the status bar to see exactly
  which file it is in sync with

**Markdown support**
- Headings (ATX and setext), bold, italic, strikethrough, `==highlight==`, sub/sup
- Lists: bullet, numbered, nested, and clickable task lists that write back to the source
- Tables with column alignment and escaped pipes
- Fenced code blocks with syntax highlighting for ~25 languages
- Blockquotes and GitHub-style callouts: `> [!NOTE]`, `[!TIP]`, `[!WARNING]`, ...
- Links, reference links, images, autolinks, and footnotes
- A safe subset of inline HTML (`<details>`, `<kbd>`, `<mark>`, ...) — scripts and
  event handlers are stripped

**On phones and tablets**
- Below 780px the layout switches to one pane at a time — the Edit/Preview toggle in
  the top bar swaps between them (split view is two useless slivers on a phone)
- The sidebar becomes a slide-over drawer: tap the dimmed backdrop to dismiss it, and
  it closes itself once you pick a file
- Toolbar scrolls sideways in a single row, buttons grow to 36px touch targets
- Editor stays at 16px so iOS does not zoom the page when you tap into it
- Respects notch/home-indicator safe areas, and nothing overflows sideways at 375px
- Find and dark mode move into the ⋮ menu, since phones have no `Ctrl+F`

**Files**
- Three sidebar panes: **Explorer** (your folder), **Docs** (scratch documents kept in
  the browser, with search), **Outline** (headings of the current file)
- Export to standalone HTML, or print / save as PDF
- Drag any `.md` file onto the window to open it
- JSON backup of every scratch document, and import to restore

Press `F1` inside the app for the full shortcut list.

## Where your work is stored

There are two kinds of document, and they are saved differently.

**Files from an opened folder** live on your disk. They are *not* autosaved — an edited
tab is marked dirty until you press `Ctrl+S`, and closing the tab or the browser warns
you first. This matches how VS Code behaves.

**Docs** (the scratch documents in the Docs pane) autosave to your browser's local
storage, per browser and per machine. They are not synced anywhere, and clearing your
browser's site data will erase them. For anything you want to keep, press `Ctrl+S` to
save a real `.md` file, or use **Menu → Download backup** to export them all as JSON.
