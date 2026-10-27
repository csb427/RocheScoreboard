---
version: alpha
name: Roche Scoreboard native operator interactions
description: Native WPF operator controls with stable hit targets, clear outcomes and readable broadcast content.
colors:
  primary: "#1A4062"
  on-primary: "#F0F6FC"
  surface: "#0E1722"
  on-surface: "#F0F6FC"
  muted: "#B5C6D8"
  selected: "#1D7B54"
  on-selected: "#FFFFFF"
  danger: "#531B23"
typography:
  body:
    fontFamily: Segoe UI
    fontSize: "14px"
    fontWeight: 400
    lineHeight: 1.4
  control:
    fontFamily: Segoe UI
    fontSize: "15px"
    fontWeight: 700
    lineHeight: 1.2
  broadcast:
    fontFamily: Arial
    fontWeight: 700
rounded:
  small: "3px"
  control: "6px"
spacing:
  small: "8px"
  base: "12px"
  section: "16px"
  large: "24px"
components:
  primary-action:
    backgroundColor: "{colors.primary}"
    textColor: "{colors.on-primary}"
    typography: "{typography.control}"
    rounded: "{rounded.small}"
    padding: "12px"
  operator-surface:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.on-surface}"
    padding: "16px"
  supporting-text:
    backgroundColor: "{colors.surface}"
    textColor: "{colors.muted}"
    typography: "{typography.body}"
  selected-preset:
    backgroundColor: "{colors.selected}"
    textColor: "{colors.on-selected}"
  destructive-action:
    backgroundColor: "{colors.danger}"
    textColor: "{colors.on-primary}"
---

## Overview

This is a native Windows WPF application. Apply a shared interaction language across sports selection, both setup wizards, AFL and cricket control panels, training, settings, libraries and prompts. Treat operator feedback and broadcast output as separate motion systems. Controls retain native input and accessibility semantics; no React, CSS runtime or web component dependency is added. Visual refinement must not change scoring rules, timer behavior or broadcast effect pacing.

## Colors

Use team colours for match identity, a clearly filled selection state for choices, and restrained action colours for hierarchy. Measure contrast against the complete panel, including crests and gradients. Failure messages remain visible until corrected; success messages confirm actual completed actions.

## Typography

Operator labels use the existing control typeface. Cricket broadcast figures use Arial Bold with stable digit geometry. Animate changed digits, not text width. Keep essential scores and timers readable while overlays are active.

## Layout

Measure content rather than forcing fixed form heights. Paired setup cards reflow together; navigation stays outside the scroller. Never enlarge a hit target on hover or shift a neighbouring command. Resize snaps selection indicators to their new positions before subsequent animations.

## Elevation & Depth

Use a focused in-app sheet for a decision. A backdrop separates the dialog without falsely making a live player look dismissed. Keep decorative movement behind, rather than on, the information.

## Shapes

Simple controls with small radii, explicit selected fills and a conventional drawn confirmation check. No unexplained corner marks or floating underlines as the only selection signal.

## Components

### Operator motion contract

The normative native timings live in `Services/OperatorMotion.cs`: hover in 150 ms, return 250 ms, press 80 ms, surface open 250 ms, surface close 150 ms, panel open 400 ms, panel close 350 ms, page switch 250 ms, stagger 40 ms, switch travel 350 ms, confirmation check draw 350 ms. Match by purpose, not by nearest number.

Animate inner template transforms and opacity. Replace an interrupted animation from its current state. Unload/hide resets temporary hover and press clocks. Selection remains correct without animation. Checkboxes retain native Toggle semantics and radio choices retain native selection semantics. Respect Windows reduced motion.

Cricket broadcast overlays keep their separately validated 620 ms sine transition; do not shorten goal, score or overlay pacing to these operator timings. Free-hit alarm graphics move behind a fixed title and stop when hidden.

Input and menu guards must remain active until a closing sheet is removed; async completions run after cleanup. Escape, keyboard focus and cancellation are functional behavior, not visual decoration.

### Shared feedback and navigation

`OperatorFeedback` adds event-driven inner highlight, press confirmation and keyboard-focus feedback to operator controls. Its adorners never receive input or change the target's layout. Hidden, disabled and unloaded controls clear temporary clocks; idle controls have no continuously running feedback timer. Broadcast windows are excluded.

Each control has one feedback owner. Templates with their own action/switch motion bypass generic adorners. Inherited hand cursors on template borders and icon boxes must never turn them into independent buttons. Generic fallback feedback draws a wash and a focus underline, never an inset rectangular outline. Choice washes cover the whole shaped tile, not only the padded label. Toss choices use a fixed confirmation marker with an interruption-safe drawn check.

`OperatorTabMotion` animates the active underline between fixed tab headers. Initial selection and resizing snap to the correct geometry. Native buttons and `OperatorActionBorder` own Enter and Space so keyboard activation cannot also trigger a match shortcut. Sports selection rejects stale delayed selections after cancellation or reset.

`OperatorInputs.xaml` supplies shared defaults for buttons, selectable tiles, checks, editable drop-downs, sliders, expandable panels, context menus and tooltips. Local sport styles retain precedence. `DisclosureMotion` reverses measured panel reveals safely, while `SharedSurfaceMotion` handles settings categories and prompt entry without delaying their actions.

Setup navigation uses fixed command targets and short interruptible page transitions. Preset and library selection changes inner surfaces, not border geometry. Training reveals newly added queue entries without replaying the whole queue. Color controls retain native slider and keyboard behavior.

## Do's and Don'ts

Apply catalog patterns when they clarify a real operation: switch travel, selection confirmation, focus response, anchored menus, reversible reveals and status feedback. Review the full catalog mapping in the interaction audit. Do not transplant marketing effects, moving cursors, magnetic hit targets, arbitrary spinning controls, AI streams, gratuitous letter effects, or always-running motion into frequent scoring commands. Do not add motion libraries merely to imitate a web demo. This contract covers all operator screens; it is not a claim that every legacy control has been individually tested.
