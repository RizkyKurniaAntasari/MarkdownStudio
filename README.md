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
folder, and they only ever touch Markdown and text files. A later launch finds that key
in `%LOCALAPPDATA%\MarkdownStudio\instance`, inside your own profile.

To rebuild it after editing `index.html`, run `build-exe.cmd` (from PowerShell:
`.\build-exe.cmd`). It uses the C# compiler that already ships with Windows, so there
is nothing to install. It is fine to build while Markdown Studio is open: the running
copy is moved aside as `MarkdownStudio.old.exe` and keeps working, the build tells you
to restart it, and the next build deletes the old copy. A failed build never touches
the working exe.

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

The whole app is one file, `index.html` (~340 KB). Copy it to a USB stick, email it,
drop it on any machine with a browser — it keeps working.

## What it does

**Workspace (VS Code style)**
- **Open Folder** (`Ctrl+Shift+O`) loads a whole folder into an Explorer tree
- Nested folders expand on click; heavy folders (`node_modules`, `.git`, `vendor`,
  `dist`, ...) are skipped, and only Markdown and text files are listed
- Open as many files as you like — each gets a **tab** across the top, and the tab
  strip scrolls with a plain mouse wheel once there are more tabs than fit
- Unsaved tabs show a dot and italic name; closing one asks first
- `Ctrl+S` saves the current file, `Ctrl+Alt+S` saves every changed file
- `Alt+1`–`9` jumps to a tab, `Ctrl+Alt+←/→` cycles, `Ctrl+Alt+W` closes
- The folder is remembered, so you can reopen it in one click after a reload
- **New file** and **Refresh** buttons at the top of the tree
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
- Formatting toolbar plus keyboard shortcuts (`Ctrl+B`, `Ctrl+I`, `Ctrl+K`, ...)
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
- `Tab` / `Shift+Tab` to indent, `Alt+Up/Down` to move lines, `Ctrl+D` to duplicate
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
- **Settings** (`Ctrl ,`) holds the documents folder, soft wrap, sync scroll, wide
  layout, both text sizes, dark mode and the language — one place per setting, rather
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
- Hard line breaks with a trailing `\` or two trailing spaces; escape any special
  character with a backslash, and `<!-- comments -->` never render

**The Welcome document is a tour of the app and a guide to writing a README.** It
opens on first run — into an empty library only: a fresh browser profile pointed at a
documents folder that already has notes (after a reinstall, or clearing site data)
picks those up instead of adding another Welcome beside them. Part 0 walks through what the app does — Docs and folders, tabs,
saving, pasting pictures and the `assets` folder, finding things, views, sharing and
settings. Since it is only written once, **⋮ menu → Welcome guide** opens the latest
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
  tabs**, or `Ctrl+Alt+Shift+W` — it asks once if any open file has unsaved changes
- Export to standalone HTML, or print / save as PDF
- Drag any `.md` file onto the window to open it
- JSON backup of every scratch document, and import to restore

Press `F1` inside the app for the full shortcut list.

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
  the app starts. Rename a document and its file is renamed with it. Delete one and the
  file goes to the **Recycle Bin**, so a misclick is undoable
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
**Settings** (also `Ctrl ,`, or the ⋮ menu), which names the folder in full and has the
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
  longer describes exe-only features in that mode
- An error during startup used to stop everything after it silently, leaving buttons
  that did nothing. Startup steps now fail one at a time and errors are shown (see
  *When something goes wrong*)
- The first picture pasted into a document creates its `assets` folder; the Explorer
  now opens that folder and flashes the new file instead of showing it collapsed
