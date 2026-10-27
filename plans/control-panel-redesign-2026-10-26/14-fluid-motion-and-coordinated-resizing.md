# 14 — Fluid motion and coordinated resizing

Design discussion update, 26 October 2026. The user clarified that Apple's interaction behaviour is central: tactile responses, a thick liquid selection indicator, connected transitions and coordinated density changes. The visual glass material can remain selective. No production implementation is authorised by this discussion. Public output remains excluded. Do not spend this stage retrying the unavailable visualization delivery.

## Important gap in the current study

The browser study demonstrates a sliding rounded selection lens and immediate density changes. It does **not** yet demonstrate the richer liquid deformation or coordinated text/layout resizing described here. Its earlier functional assertions are not evidence that these new motion requirements are implemented or reviewed.

## 1. Liquid selection indicator

Treat the selected indicator as one continuous body. During movement, its leading edge advances and its trailing edge catches up, producing a bounded stretch and a soft return to the destination shape. Its height/curvature can change modestly to communicate movement. The tab labels and their hit targets remain stationary and crisp.

On a second selection during travel, retarget the current visible shape and velocity. Do not reset to an old tab, queue every intermediate destination or replay a complete animation. Distant tab jumps need a bounded stretch so the indicator does not flood the whole dock or obscure every label. Resizing/DPI changes establish correct layout directly rather than triggering a spurious selection journey.

This is a proposed Roche interpretation of the user's thick-water reference, not a claim that Apple publishes an identical spring/shape recipe for every control.

## 2. Press and hover response

Pointer-down should immediately change the inner control surface: a slight compression, appropriate fill/light response and a controlled release. The button's outer target and neighbouring controls remain fixed. A satin or opaque button can have this physical behaviour without becoming transparent.

Match hover, press, selected and focus states as one family. Avoid independent effects fighting each other, exaggerated bounce on repeated scoring, or temporary label-width changes. Command execution must not wait for the visual response to finish. Keyboard access and reduced motion retain clear feedback.

## 3. Connected windows, menus and sections

An integrated sheet or contextual menu has an origin and a return path. Its surface expands from or remains clearly connected to its source, becoming more substantial as more content appears. Text becomes readable at its intended size; it is not stretched with the container.

Closing, reopening or changing direction while the transition is underway should continue from the current presentation state. Nested steps share the shell and preserve entered values. Tab content, wizard steps and settings categories need distinct but related choreography; do not apply a full-page slide to every navigation action.

Native file pickers and actual separate OS windows retain platform behaviour. Source-connected transitions should be designed within the integrated operator host rather than promising arbitrary native-window morphing.

## 4. Compact/Comfortable transition

Treat density as a coordinated layout change: type hierarchy, control padding, row spacing, group bounds and available columns change together. The result should read as the same interface settling into a different size, with no flash or sequence of unrelated jumps.

The implementation study should measure old and new layouts once, retain a stable visual anchor and animate bounded geometry/position where appropriate. Text must remain sharp at the settled size. Do not squash glyphs, animate an entire console's layout every frame, restart all number animations or reset the user's scroll/caret/selection.

Some density systems change spacing without changing type. This Roche design explicitly includes coordinated operator typography where it improves usability, as requested by the user; it is not described as a universal Apple density feature. Critical score/clock data and public display typography do not change through this operator animation policy.

## 5. Symbols and small state changes

Match symbol weight, size and baseline to adjacent text. A check, chevron, play/pause state or selection marker should change within its established slot. Use compatible shape transitions where appropriate; use a carefully aligned simpler transition when geometry cannot morph reliably. Do not add a spin or bounce to every symbol.

## Research references

- [Apple — Adopting Liquid Glass](https://developer.apple.com/documentation/technologyoverviews/adopting-liquid-glass): current documentation describes interactive controls and fluid transitions into menus/popovers, including segmented selection examples.
- [Apple — Applying Liquid Glass to custom views](https://developer.apple.com/documentation/swiftui/applying-liquid-glass-to-custom-views): interactive custom controls, grouped material effects and connected shape transitions. These are reference behaviours; SwiftUI APIs do not run in the WPF application.
- [Apple — Designing Fluid Interfaces](https://developer.apple.com/videos/play/wwdc2018/803/): immediate response, interruption and redirection. This remains a foundational behaviour source rather than a claim about a new OS release.

Apple design, animation vocabulary and the relevant animation/Transitions guidance inform these requirements. The user's preference for expressive feedback takes precedence over generic advice to remove all frequently used animations. Responses must still stay brief, local and operationally immediate.

## First motion review when viewing becomes available

1. Fluid tab selection, including reversal and rapid changes between distant tabs.
2. Compact/Comfortable changes with text, fields, groups, scroll and focus preserved.
3. Button → menu/sheet → return, including cancellation and reopening during motion.
4. Repeated scoring presses with stable targets and no delay to the score command.

The layout direction is sufficiently established to guide these studies. Motion fidelity, native performance and responsive behaviour still need their own review before calling the full design implementation-ready. No application files, settings, public rendering or browser-study code were changed by this update.
