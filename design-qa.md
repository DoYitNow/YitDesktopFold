# Design QA

## Comparison target

- Source visual truth: `C:\Users\10545\Desktop\YitDesktopFold\reference\selected-concept.png` (`1486 × 1058`). Later explicit product feedback overrides its outer white line, blue selection glow, fixed single-block count, and rounder radius.
- Final dark implementation: `C:\Users\10545\Desktop\YitDesktopFold\native-dark-final.png` (`1920 × 1200`).
- Final white-glass/settings implementation: `C:\Users\10545\Desktop\YitDesktopFold\native-light-glass-final.png` (`1920 × 1200`).
- Full comparison input: `C:\Users\10545\Desktop\YitDesktopFold\design-comparison-native-final.png` (`1920 × 720`). The source and implementation were proportionally reduced into equal 960 px columns; this view is for composition and hierarchy, not pixel measurement.
- Focused comparison input: `C:\Users\10545\Desktop\YitDesktopFold\design-comparison-native-organizer-final.png` (`1040 × 426`). The source organizer uses an undistorted `520 × 400` crop; the implemented organizer uses its native `491 × 426` crop. Both were inspected in the same image.
- Desktop viewport: `1920 × 1200`, Windows scale effectively `1×` for the captured WPF bounds.
- State: two persisted, user-resized desktop-layer organizers; first block contains two real shortcuts, second is intentionally blank. Global transparency is 55%. Dark is the committed tone; white is an unsaved live preview with the detached settings window open.

## Full-view comparison evidence

The native build retains the selected concept's compact icon-container anatomy, desktop placement, direct-launch icons, restrained smoke surface, and negative space. Multiple blocks, free resize, smaller corners, no border, and no blue focus glow are intentional results of later user direction. Both blocks remain visually below ordinary application windows and the empty block contains no hint copy.

## Focused comparison evidence

The focused image confirms a single uninterrupted folder surface, sharp direct icon assets, no header or external title, no white outline, no tile added around icons, and an internal-only resize affordance. The live desktop contains only two shortcuts, so it correctly does not imitate the source's filled 4-by-3 sample data.

The white-state screenshot was also inspected at full resolution because there is no white-mode source frame. At 55% transparency, both blocks reveal softened, spatially varying wallpaper through a light neutral wash. This satisfies the updated white-frosted-glass requirement and no longer reads as a flat white board.

## Required fidelity surfaces

- Fonts and typography: the organizer has no visible committed title or icon labels. When previewed, folder and icon names use quiet Windows UI typography, single-line truncation, and dark text in light mode. The detached settings window uses Segoe UI / Microsoft YaHei UI fallbacks with a compact hierarchy and no clipping.
- Spacing and layout rhythm: the settings window fits at `390 × 456`; global controls remain grouped above a divider and the two per-block controls sit in a distinct “当前整理块” section. Organizer padding, icon scale, corner treatment, and internal resize handle remain restrained at the user's persisted sizes.
- Colors and visual tokens: dark mode preserves neutral smoke glass. White mode uses native BlurBehind plus a thin white surface wash, so tint opacity no longer invokes Acrylic's near-solid light luminosity layer. There is no blue motion treatment and no white organizer outline.
- Image quality and asset fidelity: shortcuts use sharp Windows Shell icons with no synthetic frame, border, shadow, or placeholder. The wallpaper remains a real desktop raster and is visibly blurred rather than replaced by a flat fill.
- Copy and content: the context menu no longer contains “显示文件夹名称” or “显示图标名称”. The detached settings window labels the target as “当前整理块” and contains both switches. Its introductory copy correctly distinguishes global appearance from per-block display options.

## Interaction verification

- Opened “外观与吸附…” from the organizer context menu and confirmed one detached ownerless settings window.
- Switched the tone to white and confirmed the live preview updated both organizers in the same session.
- Enabled both name switches and confirmed only the targeted “工具” block showed its folder and icon names.
- Invoked Cancel and confirmed `state.json` remained at the committed dark tone with both per-block name flags still `false`; unsaved preview values did not leak through the 350 ms autosave path.
- Release build completed with 0 warnings and 0 errors. Native tests passed shortcut safety, multi-folder persistence/migration/recovery, combined settings-draft isolation, optional-name grid geometry, and magnetic snapping.

## Comparison history

### Iteration 1

- [P1] White Acrylic remained visually close to solid white even at 55% and 100% background transparency.
  - Fix: replaced the light Acrylic luminosity path with native `BlurBehind` for blur only, then applied one opacity-controlled white WPF wash. Dark Acrylic remains unchanged.
  - Post-fix evidence: `native-light-glass-final.png`; wallpaper ribbons and luminance visibly vary through both light blocks.

- [P1] Directly previewing name flags on `OrganizerFolderState` could have allowed unrelated autosave events to persist unconfirmed values.
  - Fix: added a session-scoped display-preview layer in `MainWindow`; Save atomically commits global appearance and the target block, while Cancel/Escape/close clears both previews. `CaptureState()` no longer copies preview UI flags into the persisted folder DTO.
  - Post-fix evidence: target-only toggle preview plus unchanged committed JSON after Cancel, and the combined-draft isolation test.

### Iteration 2

- No actionable P0, P1, or P2 findings remained after the fixes above.

## Findings

- No actionable P0, P1, or P2 differences remain for the current request.

## Open questions

- None blocking. The source's filled 12-icon grid and decorative blue selection treatment are intentionally absent from the user's current two-shortcut state and later visual direction.

## Implementation checklist

- [x] Make white mode visibly frosted and transparent without a blur-strength control.
- [x] Keep dark mode, full-opacity icons, lightweight native composition, and no outer outline.
- [x] Move both name controls from the context menu into a current-organizer settings section.
- [x] Preserve per-organizer persistence and transactional Cancel/Save behavior.
- [x] Build, test, package, restart, and inspect the final native application.

## Follow-up polish

- P3: None required for this pass.

final result: passed
