# SnipNation MCP

An MCP client can use SnipNation to capture Windows screenshots, annotate them, and export finished images for a guide. The server uses the same screenshot folder, presets, and image editor as the tray app.

## Connect your client

In the local MCP client, add a stdio server with the full path to your installed **SnipNation.exe** as the command and `--mcp` as the argument. Run the client in the Windows desktop session you want to capture. The client starts the server on demand.

For a client that accepts an `mcpServers` JSON configuration, replace the example path with your installation path.

```json
{
  "mcpServers": {
    "snipnation": {
      "command": "C:\\Users\\Alex\\AppData\\Local\\SnipNation\\current\\SnipNation.exe",
      "args": ["--mcp"]
    }
  }
}
```

Capture and preview calls can return image data to your MCP client.

## Create a guide

Use the **create_guide** prompt with a goal and an output folder, or ask your client to capture the relevant window, mark the controls with numbered callouts, export each step as a PNG, and write instructions with the exported images.

1. Use `list_capture_targets` to get the ID of a visible window or display. Bring the window into view before capturing it. A window capture uses the visible desktop pixels inside its bounds.
2. Call `capture_screenshot` and inspect the preview. To select the area yourself, use the `interactive` target and select a region in the normal capture overlay.
3. Use the response's screenshot ID and revision with `edit_screenshot`. Add callouts, customize their styles, and crop the image as needed. Set `save` to `false` to review a batch before saving it.
4. Export to a full Windows path such as `C:\Guides\Save a document\01-save.png`. Use `open_editor` for manual review while the MCP client is connected. Save changes in the editor before disconnecting.

## Tools

| Tool | Use |
| --- | --- |
| `list_capture_targets` | Discover displays and visible windows with bounds in desktop pixels. |
| `capture_screenshot` | Capture a window, display, desktop region, entire desktop, or interactive selection. Preview and clipboard copying are configurable. |
| `list_screenshots` | List saved screenshots newest first. Pass `nextCursor` as `before` to fetch the next page. |
| `get_screenshot` | Read image dimensions, saved annotations, crop, and revision. Request the edited or original PNG preview. |
| `edit_screenshot` | Add, update, and remove annotations, crop, or reset to the original. Apply changes in a single batch. |
| `copy_screenshot` | Copy the edited screenshot or original to the Windows clipboard. |
| `export_screenshot` | Save the edited image or original at full resolution as PNG or JPEG. |
| `list_presets` | Read the six style presets for each annotation tool. |
| `save_preset` | Save changes to a preset for later use in MCP and the editor. |
| `get_workspace` | Read the current and default screenshot folders. |
| `set_capture_folder` | Change the screenshot folder shared with the tray gallery. |
| `open_editor` | Open an existing screenshot in the Windows editor. |

## Coordinates and edits

Capture positions use desktop pixels. Displays to the left of or above the primary display can have negative coordinates. Edit positions use pixels from the top-left corner of the current image. Preview metadata includes both preview and source dimensions for translating positions after downscaling.

Add a number with one point for the badge or two points for a badge and connector target. Text uses one point. Arrows, lines, highlights, and frames use two points. Freehand accepts two or more. Presets use numbers 1 through 6. Updates and removals use the zero-based annotation indexes returned by the server.

An edit batch uses the revision of the saved screenshot. If it changed elsewhere, call `get_screenshot` and review the new edits before retrying. Each operation uses the state produced by the preceding operations. The full-resolution original is kept when edits or a crop are saved.
