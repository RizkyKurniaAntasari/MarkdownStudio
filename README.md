# Markdown Studio

A complete offline Markdown editor with live preview. One HTML file, no build step,
no dependencies, no internet connection required.

## Running it

**Double-click `MarkdownStudio.exe`.** This is the best way to use it: you get a real
app window with no address bar, its own taskbar icon, and — because it serves the page
over `http://127.0.0.1` instead of `file://` — **Open Folder and saving to disk work
fully, with no laragon needed**.

**`MarkdownStudio.exe` is the whole product — one file.** The entire `index.html` is
baked into it, so you can copy just that exe to the Desktop, a USB stick, a colleague's
machine, anywhere. Nothing else has to travel with it: the other files in this
repository are how the exe is built, not part of it.

One thing to know if you ship both: an `index.html` sitting next to the exe **wins**
over the copy inside it. That is on purpose — it is how you can change the app without
rebuilding — but it also means a stale `index.html` beside the exe would silently
override a newer one baked in. For handing the app to someone, send the exe alone.

### Installing

The exe is its own installer, and **setup is not optional**. Double-click a copy that
has not been installed and you get setup — install, or nothing opens. Cancel and the
program simply exits: no window, no server, nothing left behind.

That is deliberate. Left to run from wherever it was downloaded to, the Start menu
entry and the file associations it registers would point into your Downloads folder and
break the first time you tidied it up. Setup puts it somewhere that stays:

- Copies itself to a folder you choose. Setup suggests
  `%LOCALAPPDATA%\Programs\Markdown Studio`, which always works without administrator
  rights; type another path or press **Browse…** for anywhere else your account can
  write — another drive, a tools folder, a USB stick
- Points the Start menu entry and the "Open with" registration at *that* copy,
  wherever it went, not at wherever you happened to double-click
- Lists itself in **Settings → Apps → Installed apps**, with an uninstaller

The folder you picked is recorded as the install location in that Apps entry, which is
where Windows keeps one anyway — so it is read back from there, and it goes when the
uninstall does. Pick a folder that needs administrator rights, such as
`C:\Program Files`, and setup says so and asks again rather than failing halfway.

Once installed, that copy opens straight into the editor and never shows setup again.

`--install` and `--uninstall` do the same from the command line without the window, and
`--install "D:\Apps\Markdown Studio"` installs to a folder you name.

**`--portable` runs without installing.** That exists for working on the app itself,
where the whole point is to run the copy you just built (and to pick up an `index.html`
sitting beside it). It is not the way to use the program day to day.

Uninstalling removes the program, the Start menu entry and the file associations —
**and nothing else**. Your documents stay in their folder, and your settings, open
documents and chosen library folder stay in `%LOCALAPPDATA%\MarkdownStudio`, so
installing again picks up exactly where you left off.

It serves on a fixed loopback port, `127.0.0.1:41873`, opens Edge (or Chrome) in app
mode with its own profile in `%LOCALAPPDATA%\MarkdownStudio`, and shuts the server down
when you close the last window. Nothing listens on the network — only `127.0.0.1`.

The port is fixed on purpose: the browser files everything it stores (your Docs,
settings, the remembered folder) under the page's origin, and the origin includes the
port. A random port per launch meant every session quietly started empty. If some
other program already holds 41873 the app still starts on a free port, just without
that memory for the session.

The window is sized to your screen the first time it runs; after that it reopens at
whatever size you last left it.

### Open with

Right-click any `.md` file → **Open with** → **Markdown Studio**, and it opens that
file straight away — no Open Folder step. The **folder that file lives in becomes the
workspace**, so the Explorer tree is filled in, links to sibling files work, and
`Ctrl+S` writes back to the real file on disk.

Run the exe once and it registers itself for **Open with** on `.md`, `.markdown`,
`.mdown`, `.mkd` and `.mdwn` files, so it stays in that menu for good. Picking an exe
through "Choose another app" only lands it in a short recently-used list, and Windows
drops it again once other apps push it out, which is why it used to vanish. This goes
in your own user's registry only (no admin), it does not make itself the default app,
and it follows the exe if you move it — just run it once from the new place.
`MarkdownStudio.exe --unregister` removes it; `--register` re-adds it without opening
a window. To have `.md` files open in it on a double-click, pick it once in
**Open with → Choose another app** and tick **Always**.

You can hand it a folder the same way, or drag a file or folder onto the exe.

### In the Windows search box

The same first run also puts **Markdown Studio** in your Start menu, so pressing the
Windows key and typing `markdown` finds it the way typing `chrome` finds Chrome. That
shortcut is what the search box actually looks at — an "Open with" registration alone
never shows up there. It goes in your own Start menu folder only, follows the exe if
you move it (run it once from the new place), and `--unregister` takes it away again.

Your documents turn up there too. By default they live in
**`Documents\Markdown Studio`**, which Windows indexes out of the box, so a note's
*name* is searchable straight away. The
first run also asks the indexer to read *inside* `.md` files, using the plain-text
reader Windows already ships with, so a phrase from the middle of a note finds it as
well. That is one value per extension in your own registry; it does not change which
app opens `.md` files. Windows may take a few minutes to index a folder the first
time it sees it.

Doing this again while the app is already open gives you a second window rather than
replacing the one you are working in. The new launch hands its file to the copy that
is already running and exits, so both windows share one server and one origin.

Under the hood: a browser cannot read a path just because it was named on a command
line, so the launcher serves that folder over the same loopback connection and the app
reads and writes through it. Those endpoints are guarded by a random key generated
when the app starts and passed on the app's own URL, they refuse any path outside the
folder, and they only ever touch what the Explorer tree shows: Markdown and text files,
and the contents of `assets` folders. Pictures are read and saved as pictures only
(`.png`, `.jpg`, `.gif`, `.webp`, `.svg`, ...), an SVG is never let out as anything but a
picture, a rename stays in the same folder and never lands on another file, and a
delete goes to the Recycle Bin. The one endpoint that touches windows, `/__float`,
opens the floating window on the app's own page with the app's own browser profile,
and only ever moves, styles, closes or brings forward windows of that browser. A later launch finds that key in
`%LOCALAPPDATA%\MarkdownStudio\instance`, inside your own profile.

To rebuild it after editing `index.html`, run `build-exe.cmd` (from PowerShell:
`.\build-exe.cmd`). It uses the C# compiler that already ships with Windows, so there
is nothing to install. It is fine to build while Markdown Studio is open: the running
copy is moved aside as `MarkdownStudio.old.exe` and keeps working, the build tells you
to restart it, and the next build deletes the old copy. A failed build never touches
the working exe.

### The icon

The icon is the gradient **M** with a downward arrow — the Markdown mark — on a dark
rounded square. It lives in two places, and both have to change together:

- **`icon.ico`** is baked into the exe by `build-exe.cmd`. It is what Windows shows for
  the exe itself, the Start menu entry, the desktop shortcut, Installed apps and `.md`
  files opened with it. It holds nine sizes — 16, 20, 24, 32, 40, 48, 64, 128 and 256
  px — so Windows picks the one drawn for the taskbar, Alt+Tab, the desktop or the
  display scale in use, instead of resizing a single picture for all of them
- **`index.html`** carries the same picture as PNGs written into the page: the page's
  icon (32 and 256 px) and the logo at the top left of the app (64 px, so it is still
  sharp on a high-DPI screen). The app window and the floating window take their icon
  from the page, not from the exe, which is why the page needs its own copy

`icon-v2.jpg` is the 2048×2048 source. The mark sits small in the middle of it, so the
icon is cut from it rather than shrunk whole: the **sharp point of the M's V** is put
exactly on the vertical centre line of the square, and the mark is centred top to
bottom. The arrow then reaches further left than the M does right — on purpose: it is
the M that the eye reads as the middle. The mark fills enough of the square to stay
readable at 16 px, with room left around the arrow.

After changing the icon, rebuild the exe, then close Markdown Studio and run the new
exe's setup again so the installed copy and its shortcuts pick it up. If a shortcut
still shows the old picture, that is Windows' icon cache; it refreshes after signing
out and back in.

### Or just open the HTML

**Double-click `index.html`.** It opens in your default browser and works fully
offline, but browsers refuse disk access to `file://` pages, so Open Folder is
read-only and `Ctrl+S` downloads instead of saving in place.

But if you want to **edit files on your disk in place**, start laragon and open it at
`http://localhost/MDpreviewer/` instead. Browsers only hand out write access to a real
folder on an `http(s)` origin — never on a `file://` page. Both modes are offline; the
difference is only what saving does:

| Opened via | Open Folder | Docs | First `Ctrl+S` on a folder file |
| ---------- | ----------- | ---- | ------------------------------- |
| **`MarkdownStudio.exe`, Open with** | Folder is already loaded | Real files in `Documents\Markdown Studio` | Writes to that file, no dialog |
| **`MarkdownStudio.exe`** | Full read/write tree | Real files in `Documents\Markdown Studio` | Writes to that file, no dialog |
| `http://localhost/MDpreviewer/` (laragon) | Full read/write tree | Browser storage only | Writes to that file, no dialog |
| Double-clicked `index.html` | Read-only tree | Browser storage only | Downloads a copy |

The app tells you which mode you are in — a read-only folder is labelled as such in
the Explorer, and the status bar always names the current state.

Features that only the exe can provide are **left out** of the page opened on its own
rather than shown and not working: Settings has no Documents section (there is no
documents folder to show, change or open), and the Welcome guide leaves out Docs as
files, the library's `assets`, Open with and the documents folder, describing what the
page does instead — Docs kept in this browser, and read-only folders on `file://`.

**Saving anywhere you like.** A file opened from a folder is written back where it
came from. For anything else — a page served by laragon, or a browser-only document —
the first `Ctrl+S` opens the system save dialog so you can put the file on the Desktop
or anywhere else. After that the document is *bound* to that file: every later
`Ctrl+S` writes straight to it with no dialog. Run from the exe, Docs never ask at
all; they already have a file of their own (see **Where your work is stored**).

On a `file://` page browsers refuse the save dialog outright, so saving falls back to a
download. Chrome's **"Ask where to save each file before downloading"** setting is the
closest you get to choosing a folder there; serving through laragon is the real fix.

The whole app is one file, `index.html` (~520 KB, icons included). Copy it to a USB stick, email it,
drop it on any machine with a browser — it keeps working.

## What it does

**Workspace (VS Code style)**
- **Open Folder** (`Ctrl+K Ctrl+O`) loads a whole folder into an Explorer tree
- Nested folders expand on click; heavy folders (`node_modules`, `.git`, `vendor`,
  `dist`, ...) are skipped, and only Markdown and text files are listed
- Open as many files as you like — each gets a **tab** across the top, and the tab
  strip scrolls with a plain mouse wheel once there are more tabs than fit
- Unsaved tabs show a dot and italic name; closing one asks first
- `Ctrl+S` saves the current file, `Ctrl+K S` saves every changed file
- `Alt+1`–`9` jumps to a tab, `Ctrl+PageDown/PageUp` cycles, `Ctrl+W` closes,
  `Ctrl+Shift+T` reopens the last one closed
- **Move tabs as in Chrome**: drag a tab along the bar and drop it where you want it;
  the others make room as it passes their middle, and the tab you carried is the one
  you see. `Ctrl+Shift+PageUp/PageDown` moves the current tab a place left or right
  (VS Code's keys), and `Esc` while dragging puts everything back. On a touch screen
  a finger on the bar scrolls it instead
- **Drag a tab to move where it is stored**: carried off the bar, a tab can be dropped
  on a **folder in the Explorer** — its file moves into that folder (dropped on a file,
  into that file's folder) — or on the **Docs** pane or button, where the file becomes
  a Doc. A Doc dropped on a folder leaves Docs and becomes a file there. The folder
  under the pointer lights up and a label names it; holding over a closed folder opens
  it, holding over the Explorer or Docs button switches to that pane, and letting go
  anywhere else does nothing. The tab keeps its place on the bar and shows the file in
  its new home. It is a move: the file is written in the new place with its line
  endings and byte-order mark kept, then the old one goes — to the Recycle Bin from
  the exe. Pictures keep showing: links to pictures in the same folder tree are
  rewritten to reach them from the new place, pictures from another folder (a file
  moved into Docs, or a Doc into a folder) are copied into an `assets` folder beside
  it, and documents that linked to the moved file are offered the same link update as
  after a rename. A file that cannot be written to (a read-only folder) is copied
  instead, and the toast says so
- **Copy a tab**: right-click it → **Copy Tab**, then right-click an empty part of the
  tab bar → **Paste Tab**. The paste opens a new tab with the same content, named
  "… (copy)" — but it is a **new document in a new place**, never the same file, so
  editing the copy leaves the original alone. From the exe the copy is a Doc with its
  own `.md` file in the documents folder; in a plain browser it is a Doc in that
  browser's storage. Pictures the original shows from its own `assets` folder would
  not be beside the copy, so they are copied into the documents folder's `assets`
  (or, with no folder to put them in, into the text) and the copy's links point at
  them. Copying a Doc to a Doc needs none of that — both live in the same folder
- The tab bar is always there, even with one document open, so its empty part can
  be right-clicked: **Paste Tab**, **New document** and **Reopen closed editor**
- The folder is remembered, so you can reopen it in one click after a reload
- **New file** and **Refresh** buttons at the top of the tree. A new file never
  replaces one that is already there under that name
- **Right-click, as in VS Code.** Files, folders, the contents of `assets`, Docs and
  editor tabs each have a menu: **Open**, **Reveal in File Explorer**, **Copy Path**,
  **Copy Relative Path**, **Rename…** and **Delete**, plus **New File…** on a folder,
  **Insert into document** on an asset, and **Close Others** and **Copy Tab** on a tab
- **Rename in place**: the row becomes a box with the name selected up to its
  extension; `Enter` keeps it, `Esc` puts it back. Click a file once and `F2` renames
  it, `Delete` deletes it. Typing a name without `.md` keeps the extension, a name
  already in the folder is refused, and a document cannot be renamed into something
  that is not a document. When other documents link to the file — pictures included —
  you are asked whether to **update those links**: open files are changed in the
  editor and left for you to save, closed ones are written with their own line endings
  and BOM. A delete goes to the **Recycle Bin**; in a folder opened in the browser,
  where there is no Recycle Bin, it says the file is gone for good before it does it
- **Links between files work.** Click `[notes](notes.md)` in the preview and it opens
  that file as a tab instead of navigating away. Relative paths resolve from the file
  you are in, so `../notes.md` and `sub/deep.md` both work, an extension-less
  `[notes](notes)` still finds `notes.md`, and `notes.md#a-heading` opens the file and
  jumps to that heading. Links to the open web still open in a new browser tab.

**Editing**
- Live preview that updates as you type, with scroll synced between the two panes
- **Select text in the editor and the preview highlights the matching block** and
  scrolls to it if it is off screen — select a whole list and it highlights as one
  block, select one item and only that item lights up. Toggle it with
  **Menu → Sync scroll & selection**
- Formatting toolbar plus keyboard shortcuts (`Ctrl+B`, `Ctrl+I`, `Alt+S`, ...) — see
  *Keyboard shortcuts* below
- **Paste a screenshot straight in**, the way Word takes one: `Win+Shift+S`, then
  `Ctrl+V` in the editor. The picture is saved as a real file in an `assets` folder
  next to the document — `assets/notes-20261011-153012.png` — and
  `![image](assets/notes-20261011-153012.png)` goes in on its own line where the caret
  was. Dragging image files onto the window does the same, keeping their own names.
  It works for files from an opened folder and for Docs (their pictures go in the
  library's `assets` folder, and move with it when you change folders). A name that is
  already taken is never overwritten. Copying cells from Excel or text from Word still
  pastes the text, even though those also put a picture on the clipboard.
  With no folder to write to — a browser-only document, or a read-only folder — the
  picture is embedded in the document instead, as a reference at the very end, so the
  text where you pasted it stays a short `![image][img-1]`.
  **Export HTML** puts every picture inside the file, so it opens anywhere
- **You can see what is in `assets`.** Outside an `assets` folder the Explorer still
  lists only Markdown files, but an `assets` folder (at any depth) shows everything in
  it — pictures, PDFs, subfolders. The Docs pane lists the library's `assets` under
  the documents, each picture with the document that uses it (or
  *not used in any document*, in orange). Click one to see it with its size and full
  path, copy the path, or **Insert into document**, which writes the right relative
  link from the document you are in (`../assets/...` from a subfolder). A picture you
  have just pasted shows up straight away: the Explorer switches to it, opens the
  `assets` folder — even one the paste has only just created — and flashes the new file
- Smart Enter: continues bullet, numbered, task and quote lists; empty item ends the list
- `Tab` / `Shift+Tab` to indent, `Alt+Up/Down` to move lines, `Shift+Alt+Down` to
  duplicate, `Ctrl+D` to select the word and then its next match
- Find and replace (`Ctrl+F`) with case-sensitive and regex modes. Every hit is
  highlighted, the current one is outlined, and the view scrolls to it — focus stays
  in the search box so you can keep typing and press `Enter` to step through matches
- **Search works in Preview mode too**, against the rendered text rather than the
  Markdown source, so a phrase still matches when it runs across bold or a link.
  Preview is read-only, so replace is switched off there and the bar says so.
  Switching views hands the search over to whichever pane is on screen
- Three view modes: editor only, split, preview only (`Ctrl+1/2/3`). **Switching
  keeps your place** — each pane goes back to the line it was showing, recomputed for
  its new width, instead of jumping to the top, and the pane that appears fades in
- Both panes centre their text in a readable column instead of stretching across a
  maximised window; **Menu → Wide layout** turns that off when you want the full width
- Files keep their own line endings and encoding: a CRLF file is written back as CRLF
  and a byte-order mark is preserved, so opening a file never counts as changing it.
  The file is read as raw bytes for this, because the browser's own text reading
  quietly drops the mark
- The ⋮ menu sizes itself to the window and scrolls when it does not fit, so nothing
  hides off the bottom edge on a short screen
- **Picture-in-picture, like Google Meet.** A small window that stays on top of every
  other app, showing the document you are in. Turn it on in **Settings → Window**, then
  open it with the button that appears in the top bar, `Ctrl+Alt+P` or the ⋮ menu.
  **Preview** follows what you type (and ticking a task in it ticks the document);
  **Edit** turns it into a small editor, so you can keep taking notes while another app
  has the screen — it writes into the same document, so saving, autosave and the unsaved
  marks behave as usual, and `Ctrl+S` works inside it. Switching between the two keeps
  your place: the paragraph at the top of one is at the top of the other. It follows
  when you switch documents and themes, and ↩ brings the main window to the front and
  closes it
- **Its title bar says "Markdown Studio"**, with the app's icon. The browser's own
  picture-in-picture window prints the page's address there — `ⓘ 127.0.0.1:41873` — and no
  page can change that, so run from the exe the floating window is not the browser's:
  it is one of the app's own windows, opened on the same page in a floating mode, which
  the exe pins on top, sizes to 420×560 in the bottom-right corner of the screen the app
  is on, and keeps in step with the main window
- **One app in Alt+Tab, not two.** The floating window has no Alt+Tab entry and no
  taskbar button of its own (the exe gives it the tool-window style floating palettes
  have), it stays on screen when you minimize the main window, and it closes with the
  main window. Since it would be out of reach minimized, its minimize button closes it;
  the picture-in-picture button brings it back
- Opened as a plain browser page there is no exe to pin a window, so there the floating
  window is the browser's Document Picture-in-Picture (Edge or Chrome 116+), with the
  address in its title bar and an Alt+Tab entry of its own. Opening it takes a click or
  a key, because a browser only lets a video call open one by itself
- **Settings** (`Ctrl+,`) holds the documents folder, soft wrap, sync scroll, wide
  layout, both text sizes, dark mode, the language and picture-in-picture — one place per setting, rather
  than the same switch in a menu and a panel
- **English and Bahasa Indonesia.** The whole interface — menus, panels, tooltips,
  the status bar, the shortcut sheet, messages and questions — switches between them
  from Settings, and **Follow the system** picks the one the machine is already set to.
  English is the source text and Indonesian is a translation table keyed by it, so a
  string nobody has translated yet reads as English rather than as a missing label.
  Only the interface: your documents are your own words and are never touched. The
  Welcome guide is written out in full in both languages, and you get the one your
  interface is in — on the first run, and from **⋮ menu → Welcome guide**
- **Unsaved changes are hard to miss**: an orange dot and italic name on the tab, a dot
  in the file tree, a badge on the Save button, an orange "Unsaved changes" in the
  status bar, and a ● in the browser tab title. Hover the status bar to see exactly
  which file it is in sync with

**Markdown support**
- Headings (ATX and setext), bold, italic, strikethrough, `==highlight==`, sub/sup
- Lists: bullet, numbered, nested, and clickable task lists that write back to the source
- Tables with column alignment and escaped pipes
- Fenced code blocks with syntax highlighting for ~25 languages
- **Mermaid flowcharts are drawn, not printed.** A block marked `mermaid` becomes a
  diagram; the bar under it has **Code** to read the source that made it, **Copy**,
  and **PNG** / **SVG** to save the picture. See *Diagrams* below
- Blockquotes and GitHub-style callouts: `> [!NOTE]`, `[!TIP]`, `[!WARNING]`, ...
- Links, reference links, images, autolinks, and footnotes. An image's alt text is
  its label as plain words — `![the *login* page](...)` reads "the login page" to a
  screen reader, not the asterisks
- A safe subset of inline HTML (`<details>`, `<kbd>`, `<mark>`, ...) — scripts and
  event handlers are stripped. **Collapsible sections work with Markdown inside**:
  everything between `<details>` and `</details>` folds away, lists and code included,
  and folds can be nested
- **Ids from the document get a `md-` prefix in the preview** — `## Quick start` becomes
  `id="md-quick-start"`, `<span id="note">` becomes `id="md-note"` — the way GitHub
  prefixes them with `user-content-`, so a heading or id in your text can never take the
  name of a part of the app. Links are written with the plain name as usual
  (`[start](#quick-start)`, `#note`) and still find their target, and **Export HTML** and
  **Copy HTML** give the plain ids back, since an exported page has nothing to collide with
- Hard line breaks with a trailing `\` or two trailing spaces; escape any special
  character with a backslash, and `<!-- comments -->` never render

**The Welcome document is a tour of the app and a guide to writing a README.** It
opens on first run — into an empty library only: a fresh browser profile pointed at a
documents folder that already has notes (after a reinstall, or clearing site data)
picks those up instead of adding another Welcome beside them. It is written out in full
in English and in Bahasa Indonesia. Part 0 walks through what the app does — Docs and
folders, tabs, saving, pasting pictures and the `assets` folder, the right-click menu
with rename and delete, finding things with `Ctrl+P` and `Ctrl+Shift+P`, views,
picture-in-picture, sharing, settings, and a table of the VS Code keys. Since it is only written once, **⋮ menu → Welcome guide** opens the latest
version at any time: the existing copy if it is untouched, otherwise a fresh one, so
your own edits to an old copy are never overwritten. The rest is a
live reference in three parts — the basics (headings, emphasis, lists, links, code,
quotes, tables), going further (callouts, task lists, footnotes, collapsible sections,
reference links, anchors and a table of contents, inline HTML, escaping), and a README
skeleton you can copy straight out, with notes on what separates a good one. Every
example shows the syntax and the result side by side, so you can read it in Preview
and look at the editor to see how each piece was typed. Delete it whenever you like —
it is just a document.

**Diagrams**
- ` ```mermaid ` blocks are rendered as flowcharts: `flowchart` or `graph`, going
  `TD`/`TB` (down), `LR`, `RL` or `BT`
- Node shapes `[box]`, `(rounded)`, `([pill])`, `[[subroutine]]`, `[(database)]`,
  `((circle))`, `{decision}`, `{{hexagon}}`, `[/parallelogram/]`, `[\lean\]`, `>flag]`
- Links `-->`, `---`, `-.->`, `==>`, `--x`, `--o`, `<-->`, labelled either way
  (`A -- yes --> B` or `A -->|yes| B`), chained (`A --> B --> C`) and branched
  (`A --> B & C`)
- `subgraph Name` ... `end`, `%%` comments, `classDef` / `class` / `:::` / `style`,
  and `<br/>` inside a label
- Diagrams that loop back on themselves lay out fine, and a node may point at itself
- **Save it as a picture**: PNG at twice the size so it stays sharp when pasted, or
  SVG. Both come out with the theme you are looking at, and both are self-contained
- A diagram it cannot read shows the reason and its own source, never a broken picture

This is a flowchart renderer written for this app, not Mermaid itself. Mermaid is
around three megabytes once d3, dagre and cytoscape come with it — more than ten times
this whole program, and all of it would have to be carried inside the exe to keep
working with no connection. So flowcharts are drawn here and the other Mermaid diagram
types (sequence, class, state, gantt, pie) are not.

**On phones and tablets**
- Below 780px the layout switches to one pane at a time — the Edit/Preview toggle in
  the top bar swaps between them (split view is two useless slivers on a phone)
- The sidebar becomes a slide-over drawer: tap the dimmed backdrop to dismiss it, and
  it closes itself once you pick a file
- Toolbar and tab strip scroll sideways in a single row, buttons grow to 36px touch
  targets, and the ⋮ menu is capped to the screen so it never runs off the bottom
- Text keeps the same readable column on a tablet or a rotated phone as it does on a
  desktop, so the line length stays proportional instead of stretching to the edges
- Editor stays at 16px so iOS does not zoom the page when you tap into it
- Respects notch/home-indicator safe areas, and nothing overflows sideways at 375px
- Find and dark mode move into the ⋮ menu, since phones have no `Ctrl+F`

**Files**
- Four sidebar panes: **Explorer** (your folder), **Docs** (your own documents, each a
  real file, with search), **Outline** (headings of the current file), and **Search**
- **Search in folder** (`Ctrl+Shift+F`) looks through every Markdown file in the opened
  folder, subfolders included, with case-sensitive and regex modes. Results are grouped
  by file with line numbers; click one to open the file and land on that exact match,
  with the find bar ready to step through the rest. Open tabs are searched as they are
  in the editor, unsaved edits included; the refresh button re-reads files from disk
- **Close all tabs** with the button at the right end of the tab bar, **Menu → Close all
  tabs**, or `Ctrl+K Ctrl+W` — it asks once if any open file has unsaved changes
- Export to standalone HTML, or print / save as PDF
- Drag any `.md` file onto the window to open it
- JSON backup of every scratch document, and import to restore

## Keyboard shortcuts

**The keys are VS Code's.** The editing and navigation keys are VS Code's defaults, and
the formatting keys are those of its *Markdown All in One* extension, so hands that
know VS Code know this. One list drives the keyboard, the command palette and the
shortcut sheet, so the three never disagree.

- `Ctrl+P` — **Go to file**: every file in the folder, your Docs, open tabs and
  `assets`, matched by the letters you type in order. Start with `>` for commands, `@`
  for the headings of this document, `:` for a line number
- `Ctrl+Shift+P` or `F1` — **every command**, searchable
- `Ctrl+K Ctrl+S` — the **shortcut sheet**
- `Ctrl+K` starts a two-key chord, as in VS Code; the status bar says so while it waits

| Do | Press | Do | Press |
| -- | ----- | -- | ----- |
| Go to file | `Ctrl+P` | Bold / italic | `Ctrl+B` / `Ctrl+I` |
| All commands | `Ctrl+Shift+P`, `F1` | Strikethrough | `Alt+S` |
| Go to heading | `Ctrl+Shift+O` | Inline code | ``Ctrl+` `` |
| Go to line | `Ctrl+G` | Heading bigger / smaller | `Ctrl+Shift+]` / `Ctrl+Shift+[` |
| Find / replace | `Ctrl+F` / `Ctrl+H` | Heading 1–6 | `Ctrl+Alt+1`–`6` |
| Next / previous match | `F3` / `Shift+F3` | Toggle a task | `Alt+C` |
| Search in folder | `Ctrl+Shift+F` | Bullet / numbered list | `Ctrl+Shift+8` / `Ctrl+Shift+7` |
| Show Explorer | `Ctrl+Shift+E` | Blockquote | `Ctrl+Shift+.` |
| Toggle sidebar | `Ctrl+B` (outside the editor) | Comment out | `Ctrl+/` |
| Toggle preview | `Ctrl+Shift+V` | Select word, next match | `Ctrl+D` |
| Preview to the side | `Ctrl+K V`, `Ctrl+\` | Select line | `Ctrl+L` |
| Editor / split / preview | `Ctrl+1` / `2` / `3` | Delete line | `Ctrl+Shift+K` |
| Word wrap | `Alt+Z` | Move line | `Alt+Up` / `Alt+Down` |
| Dark mode | `Ctrl+K Ctrl+T` | Copy line up / down | `Shift+Alt+Up` / `Down` |
| Settings | `Ctrl+,` | New line below / above | `Ctrl+Enter` / `Ctrl+Shift+Enter` |
| New document | `Ctrl+N` | Indent / outdent | `Ctrl+]` / `Ctrl+[` |
| Open file / folder | `Ctrl+O` / `Ctrl+K Ctrl+O` | Cut / copy the line | `Ctrl+X` / `Ctrl+C`, nothing selected |
| Save / save as / save all | `Ctrl+S` / `Ctrl+Shift+S` / `Ctrl+K S` | Paste a URL over words | makes a link |
| Close / close all | `Ctrl+W` / `Ctrl+K Ctrl+W` | Rename file | `F2` |
| Reopen closed | `Ctrl+Shift+T` | Copy path / relative path | `Shift+Alt+C` / `Ctrl+Shift+Alt+C` |
| Next / previous editor | `Ctrl+PageDown` / `Ctrl+PageUp` | Reveal in File Explorer | `Shift+Alt+R` |
| Move tab left / right | `Ctrl+Shift+PageUp` / `Ctrl+Shift+PageDown` | Picture-in-picture | `Ctrl+Alt+P` (once on in Settings) |

`Ctrl+B` is bold while you type in the editor and toggles the sidebar anywhere else,
exactly as in VS Code with that extension. `Ctrl+Alt+P` opens and closes the
picture-in-picture window (VS Code has no such key, so this one is the app's own).

A few keys belong to the browser and may never reach the page — `Ctrl+N`, `Ctrl+W`,
`Ctrl+Tab`, `Ctrl+Shift+T` can open or close a window instead. Each has a second key
that always works: `Alt+N`, `Ctrl+Alt+W`, `Ctrl+Alt+Right/Left`, and the older
`Ctrl+Alt+S` (save all) and `Ctrl+Alt+Shift+W` (close all) still work too.

Changed from earlier versions, to match VS Code: `Ctrl+K` is now a chord prefix (insert
a link from the toolbar, the palette, or by pasting a URL over selected words);
`Ctrl+Shift+K` deletes a line; `Ctrl+D` selects the next match (`Shift+Alt+Down`
duplicates); `Ctrl+P` is Go to file (print is in the ⋮ menu); `F1` is the command
palette; `Ctrl+Shift+O` is Go to heading (open a folder with `Ctrl+K Ctrl+O`); dark mode
is `Ctrl+K Ctrl+T`; strikethrough is `Alt+S`.

## Where your work is stored

There are two kinds of document, and they are saved differently.

**Files from an opened folder** live on your disk. They are *not* autosaved — an edited
tab is marked dirty until you press `Ctrl+S`, and closing the tab or the browser warns
you first. This matches how VS Code behaves.

**Docs are real files too.** Run from `MarkdownStudio.exe`, everything in the Docs pane
is an ordinary `.md` file in a folder of your own, written as you type. No dialog ever
asks where to put it — it already has a place. Which means:

- Close the app and reopen it and your documents are there, because they are on disk
  rather than in a browser profile that site-data cleaning can wipe
- Windows Search finds them, by name and by what is written in them
- Right-click one in Explorer → **Open with** → **Markdown Studio** and it opens
- Copy, back up or sync that folder like any other; the app reads it as it finds it
- Drop a `.md` into the folder from anywhere and it appears in the Docs list next time
  the app starts. Rename a document — in the title box above the editor, or
  right-click it in the Docs list (or its tab) → **Rename…**, or `F2` — and its file is
  renamed with it. Delete one and the file goes to the **Recycle Bin**, so a misclick
  is undoable
- Right-click a document for **Reveal in File Explorer** and **Copy Path** too, which
  open or name its actual file in the library folder
- The Docs list names each document's file, and the status bar names the full path

`Ctrl+S` on a document still works; it just writes the file now instead of half a
second from now. **Menu → Download backup** still exports everything as JSON.

### Choosing where they live

Until you say otherwise the folder is **`Documents\Markdown Studio`** — Documents
because Windows indexes it out of the box. That path is not baked into the app: it is
resolved by asking Windows where *this* user's Documents folder is, so on another
machine, another account, a Documents folder redirected to OneDrive or moved to
another drive, it lands in the right place by itself.

The Docs pane says only *"Each of these is a file in here"*; clicking **in here** opens
**Settings** (also `Ctrl+,`, or the ⋮ menu), which names the folder in full and has the
buttons for it — **Change folder…** and **Open in Explorer**.

To keep them somewhere else, press **Change folder…**. Windows' own folder picker opens — the File Explorer window with
everything but folders switched off, so you get the address bar, search and Quick
access rather than the cramped old "Browse For Folder" tree. Pick anything: another
drive, a USB stick, a folder your own cloud client syncs. (On a Windows too old for
that picker the app falls back to the tree dialog, so the button always works.)

- Your notes **move with you**. Anything already in the folder you pick simply joins
  the library, so you can point the app at a folder of notes you already have
- A name that is taken in the new folder is never overwritten: that note arrives as
  `Notes 2.md` and the Docs list follows it, so nothing is lost either way
- The choice is remembered in `%LOCALAPPDATA%\MarkdownStudio\library`, next to the
  instance file. It belongs to the machine, not to the exe — so a copy carried on a
  USB stick still uses each machine's own folder
- If that folder is not there on some launch (an unplugged drive), the app falls back
  to Documents for that run and keeps your choice, rather than quietly starting a new
  empty library somewhere else. The Docs pane always names the folder actually in use

Editing the same file in two places at once is the one thing to avoid: if you open
`Documents\Markdown Studio` *as a folder* in the Explorer pane, that file tab takes over
and the document's autosave stands aside while the tab is open, so the two cannot
overwrite each other.

Without the exe — the page served by laragon, or `index.html` double-clicked — there is
no library folder, and Docs stay where they always were: in that browser's local
storage, per browser and per machine, erased if you clear its site data. Press `Ctrl+S`
to save a real `.md` file, or export them all as JSON.

## When something goes wrong

An error the app did not expect is **shown on screen** for a few seconds, as
*"Something went wrong: …"* with the step it happened in. Copy that text when you
report a problem — it names exactly what failed. A step that fails while the app is
starting is skipped rather than stopping everything after it, so the menu, Settings
and the rest of the buttons keep working either way.

## Fixes

**1.15.0**

- **Drag a tab onto a folder or onto Docs to move where it is stored** — see *Drag a
  tab to move where it is stored* under What it does
- **Dragging a tab highlighted its name as text**, which looked as if the drag had not
  taken. Tabs can no longer be text-selected, and the pointer shows a closed hand for
  the whole drag
- **A tab let go away from a folder could still be moved into it**: the drop used the
  folder the pointer had last passed over, not the place it was let go. It now looks at
  where the button is released, so letting go over the editor does nothing
- **The floating window could lose "always on top"** some time after it opened: the
  browser resets a window's z-order itself on some focus changes. The launcher now puts
  it back whenever it has gone, checking as often as it already checks for the window
  being minimized. (A first attempt that also rewrote the window's style did the
  opposite of what was wanted — the browser then minimized the floating window along
  with the main one, which closed it — so only the z-order is touched)
- **Installed apps showed an old version** (1.10.0 next to an installed 1.14.0). The
  version there is now read from the exe itself, and the entry is refreshed whenever
  the installed copy starts

**1.14.0**

- **Tabs can be moved, as in Chrome** — drag one along the tab bar, or
  `Ctrl+Shift+PageUp/PageDown`. Until now a tab stayed wherever it had been opened
- **Copy Tab and Paste Tab** — right-click a tab to copy it, right-click an empty part
  of the tab bar to paste it as a new document with the same content. The copy is
  saved in a new place (a Doc of its own), pictures included, so the original is never
  touched
- **The tab bar no longer disappears** when only one document is open, so there is
  always an empty part of it to paste into

**1.13.3**

- **The floating window lost your place when switching between Edit and Preview** —
  scrolled halfway down in one, the other opened at the top. Each now opens at the
  paragraph the other was showing: the preview's blocks know which source line they came
  from, and the editor's lines are measured with their wrapping, so the position carries
  over both ways. The caret goes to that line too, so focusing the editor no longer jumps
  back up, and an update from the main window no longer resets the editor's scroll
- **New icon** — the gradient Markdown M, replacing the old white "M" on a blue square
  everywhere: the exe, its shortcuts, the app window, the floating window and the logo
  at the top left of the app. The old `icon.ico` held a single 256 px picture that
  Windows had to shrink for every small size; the new one has nine, from 16 to 256 px,
  each drawn at its own size. See **The icon** under Running it

**1.13.2**

- **The floating window's title bar showed `ⓘ 127.0.0.1:41873`** — the browser's own
  picture-in-picture prints the page's address there and cannot be told otherwise. Run
  from the exe, the floating window is now one of the app's own windows, pinned on top
  by the launcher, and its title bar reads **Markdown Studio**. It still has no Alt+Tab
  entry or taskbar button, still stays up when the main window is minimized, and now
  closes when the main window does
- **Minimizing the floating window lost it** — with no Alt+Tab entry or taskbar button
  there was no way back. Minimizing it now closes it, and the picture-in-picture button
  opens it again
- **Edge offered to translate the page** ("Translate page from Indonesian?") whenever a
  document was written in another language. The page now tells the browser not to: what
  is on it is your own writing

**1.13.1**

- **Alt+Tab showed the app twice while picture-in-picture was open** — the main window
  and the floating one, each with its own taskbar button. The floating window is now a
  tool window of the app: on top, clickable, still there when the main window is
  minimized, but only one Markdown Studio in Alt+Tab and on the taskbar. This needs the
  exe; opened as a plain browser page the floating window stays a window of its own
- The Welcome guide (both languages) says so in its Picture-in-picture section, in the
  exe's version of the guide only

**1.13.0**

- **Settings would not open while the Welcome guide was on screen.** The guide has a
  heading called "Settings", which became `id="settings"` in the preview — the id of the
  app's own Settings window. The preview comes first in the page, so the Settings button
  opened the heading instead, silently. Any document with a heading named Help, Menu,
  Status, Editor or Preview could do the same to that part of the app and pick up its
  styling. Ids from a document's content now carry a `md-` prefix, as GitHub's carry
  `user-content-`; links such as `[x](#settings)` still find their heading, and Export
  HTML and Copy HTML put the plain ids back
- New: picture-in-picture (see *What it does*)

**1.12.0**

- **Right-click had no Rename.** Files can now be renamed, deleted, revealed in File
  Explorer and have their path copied from a right-click menu, with links to a renamed
  file updated on request (see *What it does*)
- **New file overwrote an existing file of the same name** with an empty document,
  without asking, in both the exe and a folder opened in the browser. It now refuses
  and says the name is taken
- Keyboard shortcuts now follow VS Code (see *Keyboard shortcuts*), with **Go to file**
  (`Ctrl+P`) and a **command palette** (`Ctrl+Shift+P` / `F1`); the shortcut sheet is
  drawn from the same list as the keys, so it can no longer show a key that does
  something else. Several older keys changed meaning — listed at the end of that section
- The Welcome guide (both languages) describes the right-click menu and the new keys,
  and no longer mentions the old ones

**1.11.1**

- **Find: Next / Enter did not move to the next match.** The counter stayed on
  `1 / 3`. A startup helper added in 1.11 had the same name as the function that
  steps through matches and replaced it. Renamed, and the app is now checked for any
  other name declared twice
- **A byte-order mark was lost on the first save.** A file that started with one was
  written back without it, so saving changed every such file. The browser's text
  reading dropped the mark before the app could see it; files are now read as raw bytes
- **Collapsible sections showed their contents outside the fold.** An HTML block was
  wrapped in its own container, whose closing tag shut `<details>` straight away.
  Blocks that open an element and leave it open are no longer wrapped. This also fixes
  the collapsible example in the Welcome guide
- **Image alt text kept the Markdown.** `![a *bold* word](...)` gave the alt text
  `a *bold* word`; it is now `a bold word`
- **A second Welcome appeared** each time a fresh browser profile met a documents
  folder that already had notes. The first-run guide is now only written into an empty
  library
- **A picture just pasted into a document was briefly marked "not used in any
  document"** in the Docs pane's `assets` list, because the list was drawn before the
  link went into the text. It is drawn again once the link is in

**1.11.0**

- **The whole app stopped working** after the `assets` update: a Windows path in the
  code had lost a backslash, which broke the script before anything ran
- **Settings in the page opened on its own** showed a Documents section whose
  **Change folder…** and **Open in Explorer** could never work there, so it looked
  broken. That section is now left out without the exe, and the Welcome guide no
  longer describes exe-only features in that mode. (The report of Settings not opening
  at all had a second cause that was only found later — a heading in the Welcome guide
  taking the Settings window's id — fixed in 1.13.0.)
- An error during startup used to stop everything after it silently, leaving buttons
  that did nothing. Startup steps now fail one at a time and errors are shown (see
  *When something goes wrong*)
- The first picture pasted into a document creates its `assets` folder; the Explorer
  now opens that folder and flashes the new file instead of showing it collapsed
