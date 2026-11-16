# Changelog

All notable changes to kniip are listed in this file. The newest version is at the top.

## 2.5.5 - 2026-11-16

### Changed

- **Settings save automatically.**\
  Every change in **Settings** is saved and takes effect right away, without closing the window: the color mode changes immediately and a new shortcut works at once. The **Save** and **Cancel** buttons are gone; **Close** (or Esc) closes the window. At the bottom, the window shows that changes are saved automatically, and briefly says **Saved** after each change. When you type in a text box, kniip waits a moment before saving.
  - **Defaults** now asks for confirmation first, because the defaults are saved right away.
  - When you choose another language, kniip asks right away whether to restart, not when you close the window.
  - The options under **Settings > System** for starting kniip with Windows, **Open with** and **Send to** are set as soon as you tick or untick them. If that fails, you see a message and the checkbox shows the actual state again.
  - **Create backup** first saves any pending change, so the backup is complete.

### Fixed

- The example workflows **Snip, save, upload** and **Snip, edit, share** now follow the language of kniip. Before, they kept the language they were created in. A workflow you renamed keeps your name.

## 2.5.4 - 2026-11-15

### Added

- **Workflows.**\
  Under **Settings > Workflows** you make your own workflows, each with a name, an icon and an optional shortcut. A workflow starts with a step that captures something (**Snip rectangle**, **Snip window**, **Screencast (MP4)**, **Screencast (GIF)** or **Image from clipboard**) and then runs its steps in order: **Edit**, **Save**, **Copy**, **Upload**, **Copy link**, **Open with...** and **Open folder**. For example: snip, save, upload, or snip, edit, save, upload, copy link. Add steps with **+** and change the order with the arrows. Problems, such as **Copy link** without an **Upload** step before it, show below the steps and next to the workflow in the list.
- **Edit in a workflow.**\
  The **Edit** step opens the snip in the editor, or the screencast in the screencast window, with a bar at the bottom that shows the next steps. Click **Continue workflow** to run them with the result as it is then. If the next step is **Save**, the normal **Save** button continues the workflow as well. **Stop** ends the workflow and keeps the snip or screencast open; closing the tab or window stops it too.
- **Open with a program.**\
  **Open with...** starts a program of your choice with the file. In the arguments, `%f` is the file and `%u` the link of an earlier upload. Without an earlier **Save** step, kniip uses a temporary file, also for **Open folder**.
- **Start a workflow from the tray, a shortcut or the workflow bar.**\
  The system tray menu has a **Workflows** submenu. **Show workflow bar** there opens a small floating bar with a button per workflow. You can also turn the bar on or off under **Settings > Workflows**, or in the editor by right-clicking a toolbar or the floating buttons; drag it by the grip, and it remembers its place. Per workflow you choose whether it shows in the menu and in the bar. The bar doesn't show up in snips and screencasts.
- When a workflow with an **Upload** step is done, the notification shows the workflow name and the link; click the notification to open the link in your browser. **Upload** in the editor and the screencast window now shows the same notification. If a step fails, a notification with an error icon says which step failed and why.
- The first time, kniip adds two example workflows: **Snip, save, upload** and **Snip, edit, share**. Workflows are part of the backup.

- **Rotate rectangles, ellipses and text.**\
  Rectangles, ellipses and text now have the round handle above them too, just like shapes and emoji. Text rotates with its shape: frame, balloon, oval, cloud or no shape. The tail of a balloon or cloud rotates along; drag its tip to point it elsewhere.
  - While you type in rotated text, the text box is rotated as well. The text grows from its top left corner without moving, also when you change the font size.
  - The buttons to rotate 90° under **Align** and in the right-click menu now work for rectangles, ellipses and text as well.

### Changed

- The text tool has larger font sizes: 80, 96, 128, 160 and 200.

### Fixed

- While you type or edit text, the border around the letters and the shadow now show as well.

## 2.5.3 - 2026-11-15

### Added

- **Border around the letters.**\
  The text tool has a new button that puts a border around the letters themselves, with its own color and thickness. It works with every shape, also with **No shape** for text that stays readable on any background.
- **Window shadow.**\
  When you snip a window by clicking it, kniip now makes the rounded Windows 11 corners transparent and adds a soft shadow on a transparent edge. Save as PNG (or copy) to keep the transparency. Turn this off under **Settings > Shortcuts > Selecting an area**. In the editor, with the select tool and nothing selected, the **Window shadow** button adds or removes the shadow on any image; undo works as usual. Behind the shadow you can still choose a background color, gradient or image.
- **Upload the clipboard.**\
  In the system tray menu, **Upload > Upload clipboard** uploads whatever is on the clipboard and copies the link: files you copied in Explorer, an image, or text. Text is uploaded as a `.txt` file. Under **Settings > Shortcuts** you can give **Upload clipboard** its own shortcut; there is none by default.
- **Upload files.**\
  **Upload > Upload files...** in the system tray menu lets you pick one or more files. With several files, every link goes on its own line on the clipboard.
- **Send to > kniip.**\
  Right-click one or more files in Explorer and choose **Send to > kniip** (on Windows 11 under **Show more options**). kniip uploads them and copies the link(s), also if it wasn't running yet. Folders are skipped. The installer adds this shortcut by default; you can turn it on or off under **Settings > System**. Uninstalling kniip removes the shortcut.

  The test build (`KniipTest-<version>.exe`) isn't installed, so it never adds the shortcut by itself. Turn it on under **Settings > System** in the test build; it then shows as **Send to > kniip test**. The shortcut points to that exe, so after a new build with a different version, turn the option off and on again.
- **Destination per type.**\
  Under **Settings > Upload**, choose a separate destination for **Images and videos**, **Text** and **Other files**. **Default** follows the destination with the star. The **Upload** button in the editor and for screencasts uses the destination for images and videos. If you remove a destination that a type uses, that type goes back to **Default**.
- A notification shows when the upload starts and when it's done. If some files fail, the links that did work are still copied and the notification says how many failed. One quick upload runs at a time; if you start another one, kniip says an upload is already in progress.
- The history shows uploaded images, videos, text and files with their own type and icon. For a file you uploaded from your PC, **Open** opens the local file and **Copy link** copies the link.

## 2.5.2 - 2026-11-14

### Added

- **Multiple upload destinations.**\
  Under **Settings > Upload** you now keep a list of destinations instead of a single Zipline server. Add one with **Import...** or create one with **FTP**, **SFTP** or **SCP**. Click the star to make a destination the default; the **Upload** button (Ctrl+U) in the editor and for screencasts uploads there. With more than one destination, the arrow next to **Upload** lets you choose another one. Each destination has the pencil to edit it and **More actions** for **Test**, **Duplicate**, **Export...** and **Remove**.
- **Upload with FTP, SFTP and SCP.**\
  Enter the server, port, user name and password, the remote folder and the web address where the files can be seen; `{filename}` in that address becomes the file name. FTP can use explicit or implicit FTPS, or no encryption. SFTP and SCP can sign in with an OpenSSH or PuTTY key. On the first connection kniip shows the server's fingerprint and asks if you trust it; if it ever changes, kniip refuses the server until you choose **Forget**.
- **Any ShareX custom uploader.**\
  **Import...** now reads every ShareX custom uploader (`.sxcu`), including the format from before ShareX 13, not only the one from Zipline. Zipline's Shell Script (`.sh`) still works too; the script is never run. In the editor for a custom uploader you set the method, address, parameters, headers, body and how the link is read from the response, with the ShareX syntax such as `{json:files[0].url}`, `{regex:...}`, `{select:...}` and `{inputbox:...}`. **Export...** saves it as a `.sxcu` file again.
- **Easier file name patterns.**\
  Under **Settings > Save**, snips and screencasts each get two fields: **Subfolder** and **File name**. Next to each field, the **Insert** button opens a menu per category (Window, Time, Incremental, Computer). Each code shows an example and an explanation; click it to insert it at the cursor. The codes are the same in Dutch and English:
  - Window (file name only): `%t` title of the window, `%pn` process name
  - Time: `%yy` 2026, `%y` 26, `%m` 11, `%mm` november, `%wy` week number, `%d` day, `%h` 08am, `%hh` 20, `%mi` minutes, `%s` seconds, `%ms` milliseconds
  - Incremental: `%n{3}` sequence number (001), `%rn` random digit, `%ra` random letter or digit, `%rna` random character that can't be confused (no 0/o, 1/l/i), `%rx` random hexadecimal character, `%guid`. Add `{n}` for more characters, for example `%rx{8}`.
  - Computer: `%un` user name, `%uln` domain name, `%cn` computer name

  For example, subfolder `%yy/%m-%d` with file name `%pn_%n{3}` becomes `2026/11-14/chrome_001.png`.
- **Back up and restore settings.**\
  The new **Settings > Backup** tab saves all your saved settings (shortcuts, folders, file name patterns, upload destinations, tool styles and toolbar) to a `.json` file with **Create backup...**. **Restore...** replaces all current settings with those from a backup, after you confirm, and then restarts kniip. An invalid file is refused and leaves your settings unchanged.

  Upload tokens and passwords stay encrypted by default, so only the same Windows user on the same PC can restore them; elsewhere you enter them again or import the uploader again. Check **Include upload tokens and passwords unencrypted (for another PC)** to take them along to another PC; keep that file private. Autostart and 'Open with' are stored in Windows and aren't part of the backup.
- The test build (`KniipTest-<version>.exe`) has its own orange icon, so you can tell it apart from the regular kniip in the taskbar, the system tray and the 'Open with' menu.

### Changed

- Your existing Zipline uploader is moved to the new list of destinations automatically and becomes the default, so **Upload** keeps working as before.
- The old codes (`%J`, `%Y`, `%M`, `%D`, `%V`, `%R6`, `%N3`) keep working. An existing template with folders, such as `%J/%M-%D/%V_%N3`, is split automatically into the subfolder `%J/%M-%D` and the file name `%V_%N3`, so files end up in the same place. The new codes are case-sensitive; only a lowercase `%y` now gives the year in two digits (26) instead of four.

## 2.5.1 - 2026-11-13

### Added

- Under **Settings > Update**, the new dropdown next to the automatic check sets how often kniip looks for new versions in the background, from every hour to every 24 hours. The default stays every 24 hours. The first check still runs shortly after kniip starts.

### Fixed

- **Open kniip** in the system tray menu opened the image history when the editor had no tabs. It now always opens the editor, with an empty canvas if there are no tabs.

## 2.5.0 - 2026-11-13

### Added

- **kniip in English.**\
  kniip now follows the Windows display language: if it's Dutch, everything stays in Dutch; with any other language kniip is in English. This applies to all windows, menus and notifications, the default names of snips and screencasts, and the names of emoji. Dates and numbers keep the format you set in Windows.
- Under **Settings > System > Language** you choose the language yourself: **Automatic (Windows language)**, **Nederlands** or **English**. After choosing a different language, kniip offers to restart right away; the settings then open again, in the new language.
- The installer is now bilingual too and picks its language the same way.
- In the file name template for saving, `%Y` now also works for the year, alongside `%J`.

## 2.3.4 - 2026-11-13

### Changed

- kniip now looks for new versions on GitHub ([github.com/R0GGER/kniip/releases](https://github.com/R0GGER/kniip/releases)) instead of git.hibbit.net. Under **Settings > Update**, the changelog and the installer now come from GitHub.

## 2.3.2 - 2026-11-12

### Changed

- The top of the system tray menu now always shows **Open kniip**. If there are tabs in the editor, it shows the editor; otherwise it opens the image history. This replaces **Show editor**, which only appeared when there were tabs.
- The items in the system tray menu now have an icon, in the same style as the editor. The icons follow the light or dark color mode.

## 2.3.1 - 2026-11-12

### Changed

- The dot on the kniip icon in the system tray that signals a new version is now orange instead of blue, so it stands out more.

## 2.3.0 - 2026-11-11

### Added

- **Eraser.**\
  Remove parts of a snip; those areas become transparent. There you see the checkerboard or the background you chose for the canvas, and a PNG stays see-through there. Choose the eraser with the button in the toolbar or press G. In the properties bar you choose how it works:
  - **Smart eraser** (default): click a color and all connected pixels of the same color disappear, for example a white background around a logo. **Tolerance** sets how far a color may differ; higher also takes along shades right next to it.
  - **Freehand**: drag over what needs to go. You choose the size of the eraser; a circle at the mouse shows how big it is.
  - If the snip has annotations, the eraser first merges them with the image. **Undo** (Ctrl+Z) restores everything in one step, including the separate annotations.
- **Align text in a box.**\
  Set text to left, centered or right with the three buttons next to **Bold** in the properties bar. This works for box, balloon, oval and cloud, also while typing. You see the difference with text over multiple lines, or when you've dragged the box wider than the text. The choice also applies to the next text box.
- **Text without a box.**\
  With the text tool, choose the shape **No box** to put loose text on the image, without a border or background. The settings for border, background, thickness, dashes and corners then disappear from the properties bar. **Shadow** gives the text itself a shadow, so it stays readable on a busy background.

### Changed

- Closing a tab (cross, middle mouse button or Ctrl+W) no longer asks whether you want to copy the annotations to the clipboard; the tab closes right away. kniip still asks when closing all tabs, exiting kniip and installing an update.

## 2.2.1 - 2026-11-11

### Added

- **Update notification in the system tray.**\
  If there's a newer version, the kniip icon in the system tray gets a blue dot at the bottom right. Hover over the icon and the tooltip tells you which version is ready. The dot disappears after updating, or when you turn off **Automatically check for new versions** in the **Update** tab.

### Changed

- The tabs are now in the title bar of the editor, just like in Chrome and Firefox. That saves a bar's worth of space. Drag the window by the empty space next to the tabs; double-click there to maximize or restore. With many tabs open, they get narrower instead of wrapping onto two rows.
- The kniip icon is at the top left, before the tabs. Click it for the window menu (move, size, minimize, maximize, close); double-clicking closes the window.
- The default toolbar is now **Three-way split** with floating buttons for screenshot, screencast and text recognition, on the left, vertically centered on the canvas. This applies to new installations, **Defaults** in the settings and **Reset to default** in the toolbar's right-click menu.

## 2.2.0 - 2026-11-11

### Added

- **Tabs in the editor.**\
  All snips now open as a tab in one editor window, instead of each in its own window. Switch by clicking the tab, with Ctrl+Tab and Ctrl+Shift+Tab, or with Ctrl+PageDown and Ctrl+PageUp. Close a tab with the cross, the middle mouse button or Ctrl+W. An asterisk after the name means the annotations haven't been copied, saved or uploaded yet.
  - When you close the window, kniip asks whether you want to close all tabs or keep them. Keeping hides the window; the tabs come back with the next snip, or via **Show editor** in the system tray menu. Check **Remember** to stop being asked.
  - You change the choice in the settings under **Appearance** > **Closing the editor window**.
  - Exiting kniip and installing an update always close all tabs. For a tab with unsaved annotations, kniip first asks whether you want to copy the result.

### Changed

- **New** (Ctrl+N), paste (Ctrl+V), opening an image (Ctrl+O) and dragging an image into the editor open a new tab. The current snip stays, so kniip no longer asks whether you want to discard it. Only a blank canvas you haven't done anything with yet makes way for the new snip.
- **Clear** still replaces the current tab with a blank canvas.

## 2.1.0 - 2026-11-11

### Added

- **Printing.**\
  Print a snip with your annotations. The button with the printer icon is to the left of the gear, or press Ctrl+P. You choose the printer and orientation in the Windows print dialog. The snip is centered on the page; if it's too large, it's scaled down.
- **Opening folders.**\
  To the left of the print button is a button with a folder icon. Click it and choose **Open screenshot folder** or **Open screencast folder**. You get the same folders as in the system tray menu: the save folder from the settings, otherwise the folder you last saved to.
  - When the rulers are on, there's also a folder button in the corner where the rulers meet. It opens the same menu. Right-clicking the corner still shows the menu to clear guides or hide the rulers.
  - If there's no screencast folder yet, kniip creates it (`Videos\kniip`). That means you can now also choose **Screencasts** in the system tray menu before you've saved your first screencast.
- **More save options under the Save button.**\
  On the right side of the Save button is a small arrow. Click it for a menu with the other way of saving and **Save as...**. If the default button in the settings is **Save and copy**, you choose **Save** without copying here; if it's **Save**, you choose **Save and copy** here. **Save as...** always asks where the file should go. The menu is also in the screencast window, with the chosen format, such as **Save as MP4**.

## 2.0.0 - 2026-11-10

### Added

- **OCR (recognize text).**\
  Read text from the screen and copy it. Next to the buttons for a screenshot and a screencast is now the **OCR** button, with the same icon as in the Snipping Tool. Click it, drag a rectangle, and the text in it opens in a separate window. There you edit it and copy everything, or just what you select. The editor steps aside while you choose and then comes back.
  - Press Ctrl+Shift+T in the editor to read the text of the open snip, including your annotations.
  - You can also find OCR in the menu of the system tray icon, under **OCR (recognize text)...**. Give it its own keyboard shortcut in the settings under **Keyboard shortcuts**.
  - kniip uses the Windows text recognition, in the languages of your Windows account. If Windows has no language for text recognition, the window explains how to add one.
- **Gradient in any direction.**\
  You now choose the direction of a gradient background with a dial instead of from four fixed directions. Click the button next to the colors and drag the knob around the circle. The gradient rotates along right away.
  - It snaps to the eight main directions. Hold Shift for steps of 15°, or Alt to not snap. You can also type the angle in degrees, or scroll over the dial.
  - **From the center** is now a button below the circle.
  - Each new direction is one step in **Undo**, even if you drag for a long time.

### Changed

- The checkerboard pattern behind transparent areas is lighter and therefore calmer to look at.

## 1.1.9 - 2026-11-10

### Changed

- Clicking the icon in the system tray now takes a screenshot right away: drag a rectangle to snip. The menu still opens with the right mouse button. In it, **Snip rectangle** is now bold as the default action, and **Edit from clipboard** is still there.

## 1.1.8 - 2026-11-10

### Added

- **Transparent canvas and background.**\
  A blank canvas is now transparent instead of white. In the editor you see this as a checkerboard pattern, and a saved PNG keeps the transparency. If you do want a background, choose the select tool without selecting anything: the properties bar shows **Background**. Choose **None (transparent)**, **Color**, **Gradient** (two colors and a direction) or **Image** (fills the whole canvas).
  - The background only goes under the transparent parts. That's why the option isn't there for a regular screen snip. It is for an opened PNG with transparency.
  - Blur and pixelate also work on the background. **Merge with image** keeps the background separate, so you can still change it afterwards.
  - A JPEG has no transparency. In a JPEG, and in the bitmap on the clipboard for programs that can't read PNG, transparent parts become white.
  - Some programs put an image on the clipboard that appears completely see-through. kniip now opens such an image as a regular, opaque image, so it doesn't become invisible.
  - Each change to the background is one step in **Undo**.
- **Three-way split view.**\
  A third layout for the editor: the tools are at the top, **Save**, **Upload**, **Copy** and the zoom buttons at the bottom, and **New** and **Screencast** separate. Choose the layout in the settings under **Appearance** > **Toolbar**, or right-click a toolbar and choose **Layout**.
  - The bar with **New** and **Screencast** sits to the left, right, top or bottom of the canvas, or **Floating** above the canvas. Choose the position in the settings or via right-click > **New and Screencast**.
  - Drag floating buttons by the grip or the edge to any spot above the canvas. Near the top or bottom edge they snap into place and lie horizontally; elsewhere they lie vertically. The position is remembered. Right-click and choose **Lock floating buttons** to avoid dragging them by accident.
  - The bar shows only the icons. Ctrl+H hides this bar too, and **Reset to default** puts it back on the left.
- **Rulers and guides.**\
  Above and to the left of the canvas are rulers with a scale in image pixels. They zoom and scroll along, and a blue mark shows where the mouse is. Turn them on or off with the **Rulers** button next to the zoom buttons, or with Ctrl+R.
  - Drag from a ruler onto the canvas for a guide: the top ruler gives a horizontal line, the left one a vertical line. Use the select tool to drag a guide. Drag it back to the ruler or outside the image to remove it. Right-click the corner between the rulers or the canvas and choose **Clear guides** to remove them all.
  - Objects snap to guides and to the edges and center of the image: when moving by the left, center or right side (and top, middle or bottom), when drawing, when dragging handles and when cropping. The line the object snaps to lights up pink. Hold Alt to not snap.
  - Guides are only an aid in the editor. They don't end up in the saved or copied snip.
- **Align, flip and order.**\
  As soon as you select an object, the properties bar shows the **Align** button. Click it and buttons fold out to place the object left, centered or right, and top, middle or bottom on the image. You can also flip it horizontally or vertically, and bring it forward or send it backward. Click **Align** again to fold the buttons back in. They also fold in when you no longer have anything selected. The same options are in an object's right-click menu, under **Align to the image**. That menu also has **Center of the image**.
  - Flipping works for lines, arrows, freehand highlights, cursors, emoji and shapes. For rectangles, ellipses, text, steps and blur the button is disabled.
  - Each click is one step in **Undo**.
- **Shapes.**\
  After **Ellipse** there's a new **Shapes** button (shortcut F). In the properties bar, choose which shape you draw: triangle, arrow, star, cloud, balloon, heart, moon, drop, egg, lightning bolt or spiral. The icon on the button shows the chosen shape.
  - Drag a frame to draw the shape. With Shift, width and height become equal. Use the handles to resize it later.
  - You choose border color, fill color, thickness, dashed line and shadow, just like with a rectangle. The spiral is an open line and has no fill.
  - Select a shape and pick another one in the list to replace it. Flip or rotate an arrow, moon or lightning bolt to make it point another way.
- **Rotating shapes and emoji.**\
  Above a selected shape or emoji is a round handle. Drag it around to rotate the object. It rotates around its center and snaps to 0°, 90°, 180° and 270°. Hold Shift for steps of 15°, or Alt to not snap. While rotating, the angle is shown at the bottom of the status bar.
  - Under **Align** are buttons to rotate 90° left or right. Those options are also in the right-click menu, together with **Reset rotation**. Double-click the round handle to reset the rotation as well.
  - You resize a rotated shape with the handles as usual. The shadow always falls to the bottom right.
  - Each rotation is one step in **Undo**.
- **Speech balloon and cloud.**\
  With **Text in a box**, choose the shape in the properties bar: **Box**, **Balloon**, **Oval balloon** or **Cloud**. Balloon and cloud have a tail to point at something.
  - Drag the tip of the tail to what you want to point at. If you move the whole object, the tip moves along.
  - For a cloud, the tail consists of smaller and smaller bubbles. Choose the number of bubbles, or leave it on **Auto**.
  - A selected text object has resize handles. Make the box wider or narrower and the text wraps to the next line. Double-click a handle to make the box fit the text automatically again.

## 1.1.7 - 2026-11-09

### Changed

- The icon in the system tray is larger and matches the other system tray icons.
- The text with the icon is now **kniip**. During a recording it says **kniip - recording**.

## 1.1.6 - 2026-11-09

### Added

- **Dark mode.**\
  In the settings under **Appearance** > **Color mode**, choose between **Light**, **Dark** and **System**. With **System**, kniip follows the Windows app mode (Settings > Personalization > Colors) and switches along as soon as you change it.
  - The color mode applies to all windows: the editor, the settings, the history, the screencast windows, the notifications and the system tray menu. The title bar turns dark too.
  - After **Save**, open windows switch along right away. Your snip itself doesn't change color.
  - **Light** is the default, so after the update kniip looks the same until you choose something else.

### Changed

- The **Editor** tab in the settings is now called **Appearance**. It now holds the color mode, the highlighter and the toolbar layout.
- The **Windows** tab is now called **System** and sits below **Appearance**. The **Snips** and **New snip** tabs have been merged into the **System** tab. At the top is the **Snipping Tool** section (watching the clipboard and the Screenshots folder), below it **On a new snip** and then **Windows integration**. The settings now open on the **Keyboard shortcuts** tab.
- Checkboxes and radio buttons in the settings have a new, calmer style in the kniip color.

### Fixed

- The text in a settings tab was all blue and bold, just like the tab name. Now only the tab name is formatted that way. The changelog in the **Update** tab follows the markdown again: regular text normal, headings and **bold** only where the text says so.

## 1.1.5 - 2026-11-09

### Added

- **Split view.**\
  The editor can now also show two toolbars. At the top are the tools in the center, with **Settings** on the right and the properties (color, thickness, line style) below. At the bottom are **New** and **Screencast** on the left, **Save**, **Upload** and **Copy** in the center and the zoom buttons on the right. Choose the layout in the settings under **Editor** > **Toolbar**, or right-click a toolbar and choose **Layout**.
  - In both toolbars you can drag buttons (after unlocking) and hide them, just like with one toolbar. Both layouts remember their own order.
  - Split view is the new default. If you want the familiar layout back, choose **One toolbar**.
- **Magnifier when selecting an area.**\
  When snipping a rectangle and when choosing a recording area for a screencast, a round magnifier follows the mouse pointer. You see the pixels around the pointer enlarged with a grid and a blue crosshair, and below that the screen coordinates (X and Y). That way you put a corner exactly on the right pixel. With **Snip window** the magnifier doesn't appear.
  - The magnifier is on by default. If you don't want it, uncheck **Show a magnifier when selecting an area** in the settings under **Keyboard shortcuts** > **Selecting an area**.
- **Capture delay.**\
  The system tray menu has **Capture delay: 0s**. Set 0 to 60 seconds there with the slider or the number field. **Snip rectangle** and **Snip window** then wait that long before the screen freezes, also via the keyboard shortcut. That lets you open a menu or make a tooltip appear first.
  - A small window at the mouse counts down. That window itself doesn't end up in the snip. Press Esc or click the window to cancel.
  - At 0 seconds you snip right away, as before. Screencasts and the **New** button in the editor don't wait.
- **History.**\
  kniip now keeps track of what you save and upload: snips and screencasts, with the file and the link. Snips you only copy or don't keep aren't included. The system tray menu has four new items:
  - **Open folder**, with the Screenshots folder, your snips folder and your screencasts folder.
  - **History...** opens a list with date, type, name, folder and link. You can search by name or link.
  - **Image history...** opens the same window with thumbnails. A snip you only uploaded also has a thumbnail.
  - **Recent items** shows the last ten. Click an item to open it, or go to the submenu for **Open in the editor**, **Copy link**, **Copy image**, **Copy path** and **Show in folder**.

  Double-click in the history window to open an item. Right-click for the same actions and for **Remove from history**. **Clear history** empties the list. Your files and uploads still exist. kniip keeps the last 500 items.

## 1.1.4 - 2026-11-09

### Added

- **Notification of a new version.**\
  kniip checks at startup and then every day whether there's a newer version. If there is, a blue dot appears on the **Settings** button in the editor and on the **Update** tab in the settings. If you don't want this, uncheck **Automatically check for new versions** in the **Update** tab.
- **Extended right-click menu in the editor.**\
  Right-click the canvas, also on an empty spot, for a menu with icons and keyboard shortcuts. Whatever isn't possible at that moment is grayed out.
  - **Undo** and **Redo**.
  - **Delete**, **Delete all** (Shift+Delete, the image stays) and **Merge with image** (Ctrl+Shift+F). Merging makes the annotations part of the image and can be undone.
  - **Cut** (Ctrl+X), **Copy** (Ctrl+Shift+C), **Paste** (Ctrl+V) and **Duplicate** (Ctrl+D). Paste via the menu puts the annotation where you right-clicked. Copying and pasting also works between two editor windows.
  - **Bring to front** (Home), **Bring forward** (PageUp), **Send backward** (PageDown) and **Send to back** (End).
  - **Hide toolbars** (Ctrl+H). Bring them back via the same menu or Ctrl+H. After a restart they're always back.
- **Bending lines and arrows.**\
  Select a line or arrow. A small circle then appears in the middle of each segment. Drag that circle and it becomes a bend point. The line then runs as a smooth curve through all points. You can add as many bend points as you like.
  - Double-click a bend point to remove it again.
  - On an arrow, the head turns along with the direction of the last segment.
  - With Shift, the start and end points jump in steps of 45°, measured from the neighboring bend point.
  - Adding, moving and removing bend points can be undone.

### Changed

- kniip now looks for updates in the background by default. Until now that only happened when you opened the **Update** tab or clicked **Check again**. A failed check, for example without internet, doesn't show an error.
- **Ctrl+V** now pastes a copied annotation first. If you've copied something else afterwards, Ctrl+V opens the image from the clipboard as before.
- The toolbar's right-click menu got the same new style.
- **Test build alongside kniip.**\
  `build.ps1` now also creates `KniipTest-<version>.exe`. This test build has its own settings and registry entries, so it can run alongside the installed kniip without touching it. The test build doesn't update itself. Use `-SkipTest` to skip it.

## 1.1.3 - 2026-11-09

### Added

- **Keyboard shortcut for the clipboard.**\
  In the **Keyboard shortcuts** tab you can assign a combination to **From clipboard**. It opens the image on the clipboard in the editor. There's no shortcut by default. The chosen combination is also shown next to **Edit from clipboard** in the system tray menu.
- **Dragging an image into the editor.**\
  Drag an image file, or an image from another program, onto the kniip window. kniip then asks whether you want to open that image, so an ongoing edit doesn't just disappear.

## 1.1.2 - 2026-11-09

### Fixed

- **Copying the link after uploading.**\
  After uploading, kniip sometimes couldn't put the link on the clipboard and reported that the clipboard was busy. This happened mostly when the Windows clipboard history (Win+V) or another clipboard program was active. kniip now puts the link on the clipboard in a more direct way and keeps retrying for longer. This also applies to uploading screencasts and the test upload in the settings.

## 1.1.1 - 2026-11-09

### Changed

- **Settings without a scrollbar.**\
  The settings window is a bit taller, so every tab, including **Save** with the new name fields, fits at once. The changelog in the **Update** tab also has more room as a result.

### Fixed

- The download links in the manual (README) pointed to the wrong tag and gave an error.

## 1.1.0 - 2026-11-09

### Added

- **Updating from within kniip.**\
  The settings have a new **Update** tab. kniip checks git.hibbit.net for a newer version and shows the changes of all newer versions, nicely formatted. With **Update**, kniip downloads the installer, closes itself, installs the new version in the background and then restarts. Your earlier choices, such as starting with Windows and 'Open with', stay in place.
  - After an update, kniip reports in the system tray which version it was updated to.
  - It only checks when you open the tab or click **Check again**; kniip doesn't look for updates in the background by itself.
  - If a screencast is still running, or you want to save an open snip first, the update waits until that's done.
- **File name template for saving.**\
  In the **Save** tab you set how files are named, separately for snips and screencasts. You can use these codes:
  - `%J` year, `%M` month and `%D` day (for example `2026`, `11`, `09`)
  - `%V` the name of the program you're snipping or recording, for example `chrome`
  - `%R6` six random letters and digits
  - `%N3` a sequence number with three digits (`001`, `002`, ...), counted per folder
  - `/` creates a subfolder; kniip creates those folders itself.

  For example, `%J/%M-%D/%V_%N3` becomes `2026/11-09/chrome_001.png`. Below the field you see an example right away. kniip adds the extension (`.png`, `.mp4` or `.gif`) itself. If you leave the field empty, the familiar name stays: `Knipsel` or `Screencast` with date and time.
- **Cleaning up the autostart entry.**\
  If you ever started kniip as a standalone exe, an entry may be left behind in **Settings > Apps > Startup**. `tools\remove-autostart.cmd` removes it. No administrator rights are needed.

### Changed

- The version number is now also in the name of the standalone exe (`Kniip-1.1.0.exe`), alongside the installer (`Kniip-Setup-1.1.0.exe`).
- The manual (README) now describes downloading, installing, uninstalling and publishing new versions, and has a screenshot of the editor.

### Note

- Coming from 1.0.0, you need to install this version yourself one more time with `Kniip-Setup-1.1.0.exe`. After that you can update to later versions via **Settings > Update**.

## 1.0.0 - 2026-11-07

First stable version.

- **Annotation editor** alongside the Windows Snipping Tool: add arrows, lines, rectangles and other shapes, text, highlights, numbered steps, emoji, mouse pointers and blur to a snip. With undo and redo, and a toolbar you can arrange yourself.
- **Picking up snips**: kniip notices new images from the Snipping Tool on the clipboard or in the Screenshots folder, and then shows a notification or opens the editor right away.
- **Custom keyboard shortcuts** for snipping a rectangle or a window, also with Print Screen.
- **Screencasts** of a chosen area, with system audio, microphone, mouse pointer and highlighted mouse clicks. Then trim and export as MP4 video or GIF.
- **Saving, copying and uploading**: save to a fixed folder or choose every time, optionally also to the clipboard, and upload to your own Zipline server. After uploading, the link is on the clipboard.
- **Windows integration**: runs in the background in the system tray, can start with Windows and appears in 'Open with' for images.
- **Installer** per user, without administrator rights and without a separate .NET installation.
