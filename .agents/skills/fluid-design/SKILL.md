---
name: fluid-design
description: Use this skill whenever building interfaces that should feel physically responsive, alive, and polished. Trigger for spring animations, gesture-driven UI, interruptible animations, physics-based motion, drag interactions, momentum scrolling, layout animations, shared element transitions, spatial UI, micro-interactions, haptic feel, or any request to make an interface feel alive, snappy, smooth, natural, polished, or less static. Also trigger when building interactive components (drawers, sheets, carousels, modals, accordions, reorderable lists) without specified animation, or any interactive prototype, component library, or design system.
license: MIT
---

# 10 Principles for Fluid Interfaces

A fluid interface feels like it has physical weight, momentum, and awareness. It responds to your input as if it were a real object — not a series of state changes rendered to screen. These 10 principles define the difference between an interface that *works* and one that *feels right*.

Each principle includes the underlying concept, why it matters, and how to implement it with specific tools.

---

## Principle 1: Motion Should Be Physics-Based, Not Time-Based

Traditional CSS transitions use fixed durations and easing curves. The result is motion that feels mechanical — every animation takes exactly 300ms regardless of how far an element travels or how fast the user was moving when they released it. Physics-based motion uses spring dynamics instead: tension, friction, and mass determine how an element moves. This means a small nudge produces a gentle settle, while a fast flick produces an energetic overshoot. The motion *adapts* to the context that produced it.

**Why it matters:** Spring animations feel natural because they model how real objects behave. They have no fixed duration — they resolve when the energy dissipates. This eliminates the uncanny disconnect between user input velocity and animation response.

**Implementation:**

Framer Motion's `spring` type is the most accessible entry point. For lower-level control, use Popmotion or build on `requestAnimationFrame` with spring physics.

```jsx
// Framer Motion — spring is the default for physical properties
<motion.div
  animate={{ x: targetX }}
  transition={{
    type: "spring",
    stiffness: 300,  // How tight the spring pulls
    damping: 25,     // How quickly oscillation settles
    mass: 0.8,       // How heavy the element feels
  }}
/>
```

```javascript
// Vanilla JS — spring physics on rAF
function springAnimation(current, target, velocity, { stiffness = 300, damping = 25, mass = 1 }) {
  const force = -stiffness * (current - target);
  const dampingForce = -damping * velocity;
  const acceleration = (force + dampingForce) / mass;
  const newVelocity = velocity + acceleration * (1 / 60);
  const newPosition = current + newVelocity * (1 / 60);
  return { position: newPosition, velocity: newVelocity };
}
```

**Key tuning values:** Stiffness 200–400 for responsive UI elements. Damping 20–30 for a natural settle with minimal overshoot. Reduce mass below 1.0 for elements that should feel lightweight and nimble (toggles, chips), increase above 1.0 for elements that should feel substantial (modals, sheets).

**Tools:** Framer Motion, React Spring, Popmotion, Motion One, SwiftUI's `.spring()`.

---

## Principle 2: Every Animation Must Be Interruptible

If a user taps a button while a modal is still animating open, the modal should reverse smoothly from its current position — not finish opening, then close. If a user starts dragging a card mid-bounce, the card should immediately respond to their finger. Non-interruptible animations create a fundamental disconnect: the interface is doing something the user didn't ask for, and they have to wait.

**Why it matters:** Interruptibility is the single biggest factor in whether an interface feels responsive or sluggish. A 400ms animation that can be interrupted at any point feels faster than a 200ms animation that locks out input.

**Implementation:**

Spring animations are inherently interruptible — you simply change the target and the spring recalculates from its current position and velocity. CSS animations and keyframes are not interruptible by default; you need to read the computed style and restart from there.

```jsx
// Framer Motion — interruptible by default
// Changing `isOpen` mid-animation reverses smoothly
<motion.div
  animate={{ height: isOpen ? "auto" : 0 }}
  transition={{ type: "spring", stiffness: 350, damping: 30 }}
/>
```

```css
/* CSS approach — use transitions, not keyframes, for interruptibility */
.panel {
  transition: transform 250ms ease-out;
  /* Changing the class mid-transition reverses from current position */
}
.panel.open { transform: translateY(0); }
.panel.closed { transform: translateY(100%); }
```

**Rule of thumb:** If the user can trigger a state change while an animation is playing, that animation must be interruptible. If you're using `@keyframes` for interactive elements, reconsider.

**Tools:** Framer Motion, React Spring (both handle this natively). For CSS, prefer `transition` over `@keyframes` for any user-triggered state change.

---

## Principle 3: Direct Manipulation Over Indirect Control

Wherever possible, let users move, resize, reorder, and dismiss elements by directly manipulating them — dragging, swiping, pinching — rather than pressing buttons that trigger those actions. A drag-to-dismiss sheet feels fundamentally different from tapping a close button. Direct manipulation creates a sense of ownership and physical connection to the interface.

**Why it matters:** Direct manipulation collapses the gap between intention and outcome. The user doesn't *tell* the interface what to do; they *do it themselves*. This makes interactions feel immediate and intuitive, especially on touch devices.

**Implementation:**

Track pointer position and velocity. Map pointer movement directly to element position (1:1 tracking during the gesture). On release, use the pointer's velocity to determine the outcome — a fast swipe dismisses, a slow release snaps back.

```jsx
// Framer Motion — drag with velocity-based snap/dismiss
<motion.div
  drag="y"
  dragConstraints={{ top: 0, bottom: 0 }}
  dragElastic={0.2}
  onDragEnd={(_, info) => {
    // Velocity-based decision: fast swipe = dismiss
    if (info.velocity.y > 500 || info.offset.y > 200) {
      onDismiss();
    }
  }}
/>
```

```javascript
// Vanilla JS — pointer tracking with velocity
let lastY = 0, lastTime = 0, velocity = 0;

element.addEventListener('pointermove', (e) => {
  const now = performance.now();
  velocity = (e.clientY - lastY) / (now - lastTime);
  lastY = e.clientY;
  lastTime = now;

  // 1:1 tracking — element follows the pointer exactly
  element.style.transform = `translateY(${e.clientY - startY}px)`;
});

element.addEventListener('pointerup', () => {
  if (Math.abs(velocity) > 0.5) dismiss();
  else snapBack();
});
```

**Where to apply:** Bottom sheets, drawers, cards in a stack, reorderable lists, image galleries, dismissable notifications, sliders, and any element the user might instinctively try to grab.

**Tools:** Framer Motion (`drag`), `@use-gesture/react` for complex gesture recognition, Pragmatic Drag and Drop for reordering, native CSS `touch-action` for gesture control.

---

## Principle 4: Preserve Velocity Across Gesture Boundaries

When a user releases a dragged element, the element should continue moving at the velocity it had at the moment of release — not stop dead and then animate to its destination. This is momentum. Similarly, when an element snaps to a position, the snap animation should inherit the gesture's velocity as its initial velocity.

**Why it matters:** Velocity preservation is what makes the difference between an interface that feels like you're manipulating objects and one that feels like you're toggling states. It's the bridge between the gesture (direct manipulation) and the animation (system response).

**Implementation:**

Capture the pointer velocity at the moment of release. Pass it as the `initialVelocity` to your spring animation. The spring then starts with that energy and dissipates it naturally.

```jsx
// Framer Motion — velocity is preserved automatically during drag
// For manual control:
const y = useMotionValue(0);

function onPointerUp(velocity) {
  animate(y, snapPoint, {
    type: "spring",
    velocity: velocity,  // Inherit gesture velocity
    stiffness: 300,
    damping: 30,
  });
}
```

**The test:** Flick an element quickly toward its target. It should overshoot slightly and settle back — because it arrived with excess energy. Drag it slowly and release near the target. It should glide in gently with no overshoot. If both feel the same, velocity isn't being preserved.

**Tools:** Framer Motion and React Spring both support `velocity` as an animation parameter. For vanilla implementations, track `dx/dt` during the gesture and feed it into your spring function.

---

## Principle 5: Use Shared Element Transitions to Maintain Spatial Context

When navigating between views, elements that exist in both views should transition continuously rather than disappearing and reappearing. A thumbnail that expands into a full image, a list item that morphs into a detail view, a button that becomes a modal — these shared element transitions maintain spatial context and help users understand where they are.

**Why it matters:** Without spatial transitions, every navigation feels like a hard cut. The user loses their sense of place. Shared element transitions communicate that the new view is an *expansion* of what they were looking at, not a replacement.

**Implementation:**

The View Transitions API is the modern standard for cross-view transitions. For component-level transitions within a single page, use `layoutId` in Framer Motion or FLIP (First, Last, Invert, Play) techniques.

```jsx
// Framer Motion — shared layout animation via layoutId
// List view
<motion.div layoutId={`card-${item.id}`}>
  <Thumbnail />
</motion.div>

// Detail view — same layoutId, Framer animates between them
<motion.div layoutId={`card-${item.id}`}>
  <FullImage />
</motion.div>
```

```javascript
// View Transitions API — cross-page or cross-route transitions
// Assign matching view-transition-name values to shared elements
// CSS:
// .thumbnail { view-transition-name: hero-image; }
// .full-image { view-transition-name: hero-image; }

document.startViewTransition(() => {
  updateDOM(); // Swap the views
});
```

**Design rule:** Identify elements that persist across views and give them continuous transitions. Everything else can fade or slide as a group. The persistent elements anchor the user's spatial understanding.

**Tools:** View Transitions API (native), Framer Motion (`layoutId`, `AnimatePresence`), FLIP technique (manual), Navigation API for MPA transitions.

---

## Principle 6: Respond to Input Method, Not Just Screen Size

Fluid interfaces adapt not only to viewport dimensions but to how the user is interacting. A hover-driven tooltip system should transform into a long-press system on touch. Scroll-linked animations should respect scroll velocity. Pointer precision should influence target sizes. The interface should feel native to whatever input method is currently active.

**Why it matters:** An interface that shows hover tooltips on a touch device isn't responsive — it's broken. True input responsiveness means the interaction model shifts based on the active input device, not just the screen width.

**Implementation:**

Use `@media (hover: hover)` and `@media (pointer: fine | coarse)` to adapt interaction patterns. Track active input type dynamically for hybrid devices.

```css
/* Hover-dependent interactions only when hover is available */
@media (hover: hover) and (pointer: fine) {
  .tooltip-trigger:hover .tooltip { opacity: 1; }
  .card:hover { transform: translateY(-2px); }
}

/* Larger targets for coarse pointers */
@media (pointer: coarse) {
  .interactive { min-height: 48px; min-width: 48px; }
  .list-item { padding-block: 14px; }
}
```

```javascript
// Scroll velocity detection for scroll-linked animations
let lastScroll = 0, scrollVelocity = 0;

window.addEventListener('scroll', () => {
  scrollVelocity = window.scrollY - lastScroll;
  lastScroll = window.scrollY;

  // Faster scroll = more dramatic parallax / header collapse
  header.style.transform = `translateY(${Math.min(0, -scrollVelocity * 0.5)}px)`;
}, { passive: true });
```

**Tools:** CSS Media Queries Level 4 (`hover`, `pointer`, `any-hover`, `any-pointer`), `@use-gesture/react` for normalised cross-input gesture handling, CSS Scroll-Driven Animations for declarative scroll-linked effects.

---

## Principle 7: Animate Layout Changes, Don't Teleport

When elements are added, removed, or reordered in a layout, the surrounding elements should animate to their new positions rather than teleporting. A deleted list item should collapse smoothly while siblings slide up to fill the gap. A new element should expand into existence while pushing its neighbours aside.

**Why it matters:** Layout jumps are one of the most common sources of visual jank. They break the user's spatial model of the page — elements they were looking at suddenly move without explanation. Animated layout changes maintain continuity and help users track what changed.

**Implementation:**

Use Framer Motion's `layout` prop for automatic layout animation. For vanilla approaches, use the FLIP technique: record element positions before the DOM change, apply the change, then animate from old positions to new.

```jsx
// Framer Motion — automatic layout animation
<AnimatePresence>
  {items.map(item => (
    <motion.li
      key={item.id}
      layout                          // Animate position changes
      initial={{ opacity: 0, y: 20 }} // Enter animation
      animate={{ opacity: 1, y: 0 }}
      exit={{ opacity: 0, x: -100 }}  // Exit animation
      transition={{ type: "spring", stiffness: 300, damping: 30 }}
    >
      {item.label}
    </motion.li>
  ))}
</AnimatePresence>
```

```javascript
// FLIP technique — vanilla JS
function animateLayoutChange(elements, domUpdate) {
  // FIRST: record current positions
  const positions = elements.map(el => el.getBoundingClientRect());

  // Apply DOM change
  domUpdate();

  // LAST: record new positions
  elements.forEach((el, i) => {
    const newPos = el.getBoundingClientRect();
    const dx = positions[i].left - newPos.left;
    const dy = positions[i].top - newPos.top;

    // INVERT: offset to old position
    el.style.transform = `translate(${dx}px, ${dy}px)`;

    // PLAY: animate to new position
    requestAnimationFrame(() => {
      el.style.transition = 'transform 300ms ease-out';
      el.style.transform = '';
    });
  });
}
```

**Tools:** Framer Motion (`layout`, `AnimatePresence`), AutoAnimate (drop-in, zero-config), FLIP technique (manual), `View Transitions API` for document-level layout shifts.

---

## Principle 8: Apply Progressive Resistance at Boundaries

When a user drags or scrolls past the boundary of a container, the interface should resist progressively — not stop dead or scroll freely into empty space. This is the rubber-band effect. Pull a little and the element follows at a reduced rate. Pull further and the resistance increases. Release and it snaps back with a spring. This communicates the boundary without blocking the gesture.

**Why it matters:** Hard stops feel like hitting a wall. Unrestricted overflow feels broken. Progressive resistance communicates "you've reached the edge" in a way that feels physical and informative. It's the difference between an interface that has *edges* and one that has *boundaries*.

**Implementation:**

Apply a logarithmic or square-root dampening function to the overscroll distance. The further past the boundary, the less the element moves per pixel of pointer travel.

```jsx
// Framer Motion — elastic drag constraints
<motion.div
  drag="y"
  dragConstraints={{ top: -300, bottom: 0 }}
  dragElastic={0.3}  // 30% tracking beyond boundaries
/>
```

```javascript
// Vanilla — rubber band formula
function rubberBand(offset, limit, elasticity = 0.55) {
  // Returns dampened offset that approaches limit asymptotically
  const clamped = Math.abs(offset);
  return Math.sign(offset) * (limit * (1 - Math.exp(-clamped / limit / elasticity)));
}

// Usage during drag past boundary
const overscroll = currentY - boundaryY;
const dampened = rubberBand(overscroll, 120);
element.style.transform = `translateY(${boundaryY + dampened}px)`;
```

**Where to apply:** Scroll containers at their limits, draggable elements at their constraints, pull-to-refresh interactions, bottom sheets at minimum/maximum height, carousel edges.

**Tools:** Framer Motion (`dragElastic`, `dragConstraints`), CSS `overscroll-behavior` for basic scroll containment, custom rubber-band functions for fine control.

---

## Principle 9: Choreograph Sequences, Don't Reveal Everything at Once

When multiple elements enter or transition together, they should arrive in a staggered sequence — not all at once. A list of cards should cascade in from top to bottom. A dashboard should build up section by section. Simultaneous animation of many elements reads as a single blob; staggered animation gives each element a moment of individual attention and creates a sense of intentional orchestration.

**Why it matters:** Stagger creates rhythm, directs attention, and makes the interface feel crafted rather than dumped onto the screen. It also improves perceived performance — the first element appears sooner than if you waited for all data before animating everything together.

**Implementation:**

Apply an incremental delay to each element in a group. Keep individual animation durations consistent; only the *start time* varies. Total stagger sequences should complete within 400–600ms to avoid feeling slow.

```jsx
// Framer Motion — stagger children
const container = {
  animate: {
    transition: {
      staggerChildren: 0.06,      // 60ms between each child
      delayChildren: 0.1,         // 100ms before first child
    }
  }
};

const child = {
  initial: { opacity: 0, y: 12 },
  animate: { opacity: 1, y: 0, transition: { type: "spring", stiffness: 300, damping: 24 } },
};

<motion.ul variants={container} initial="initial" animate="animate">
  {items.map(item => (
    <motion.li key={item.id} variants={child}>{item.label}</motion.li>
  ))}
</motion.ul>
```

```css
/* CSS stagger using custom property */
.stagger-item {
  opacity: 0;
  transform: translateY(12px);
  animation: fadeUp 350ms ease-out forwards;
  animation-delay: calc(var(--i) * 60ms);
}

@keyframes fadeUp {
  to { opacity: 1; transform: translateY(0); }
}
```

**Timing guidelines:** Stagger delay of 40–80ms per element. Cap total sequence at ~600ms (so for 10 items at 60ms stagger, the last item starts at 540ms). For very long lists, only stagger the first 8–10 visible items and instant-render the rest.

**Tools:** Framer Motion (`staggerChildren`, `variants`), GSAP (`stagger`), CSS `animation-delay` with custom properties, AutoAnimate.

---

## Principle 10: Respect the User's Motion Preferences

All of the above principles are subordinate to this one: if the user has indicated they prefer reduced motion, honour that preference completely. This means replacing spring animations with instant or near-instant transitions, disabling parallax and auto-playing animations, and simplifying gesture interactions. Fluid doesn't mean motion-heavy — for users who experience motion sensitivity, a fluid interface is one that transitions cleanly without triggering discomfort.

**Why it matters:** Motion sensitivity affects a significant portion of users. Vestibular disorders, migraines, and other conditions can make animated interfaces physically uncomfortable. An interface that ignores `prefers-reduced-motion` isn't fluid — it's hostile.

**Implementation:**

Provide a reduced-motion mode that preserves spatial relationships and feedback (opacity, colour changes) while eliminating translation, scale, and rotation animations.

```css
@media (prefers-reduced-motion: reduce) {
  *, *::before, *::after {
    animation-duration: 0.01ms !important;
    animation-iteration-count: 1 !important;
    transition-duration: 0.01ms !important;
    scroll-behavior: auto !important;
  }
}
```

```jsx
// Framer Motion — respect motion preference
import { useReducedMotion } from "framer-motion";

function Card({ children }) {
  const reducedMotion = useReducedMotion();

  return (
    <motion.div
      layout={!reducedMotion}
      initial={reducedMotion ? false : { opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={reducedMotion
        ? { duration: 0 }
        : { type: "spring", stiffness: 300, damping: 24 }
      }
    >
      {children}
    </motion.div>
  );
}
```

**Rule:** Never disable `prefers-reduced-motion` behaviour for aesthetic reasons. Always provide a reduced-motion path. Test your interface with the preference enabled. The interface should still feel complete and usable — just quieter.

**Tools:** `prefers-reduced-motion` media query, Framer Motion's `useReducedMotion()`, `matchMedia('(prefers-reduced-motion: reduce)')` in JS.

---

## Quick Reference

| Principle                      | Core Idea                               | Key Tool                            |
| ------------------------------ | --------------------------------------- | ----------------------------------- |
| 1. Physics-Based Motion        | Springs, not durations                  | Framer Motion, React Spring         |
| 2. Every Animation Interruptible | Cancel and redirect mid-flight        | Springs (inherently interruptible)  |
| 3. Direct Manipulation           | Drag, swipe, pinch over buttons       | `@use-gesture/react`, Framer `drag` |
| 4. Preserve Velocity             | Gesture momentum carries into animation | `initialVelocity` on springs      |
| 5. Shared Element Transitions    | Elements persist across views         | `layoutId`, View Transitions API    |
| 6. Respond to Input Method       | Adapt to touch, mouse, scroll velocity | `hover`, `pointer` media queries   |
| 7. Animate Layout Changes        | Don't teleport when layout shifts     | `layout` prop, FLIP, AutoAnimate    |
| 8. Progressive Resistance        | Rubber-band at boundaries             | `dragElastic`, rubber-band math     |
| 9. Choreograph Sequences         | Stagger, don't reveal all at once     | `staggerChildren`, GSAP stagger     |
| 10. Respect Motion Preferences | Reduced motion is not no motion         | `prefers-reduced-motion`            |
