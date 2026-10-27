---
name: microkit-ui
description: Use MicroKit's copy-ready microinteraction catalog when designing or implementing small UI interactions. Applies to MicroKit components, interactive polish, and adapting those patterns to a project's native UI framework.
---

# MicroKit UI

Use the cloned MicroKit catalog at `microkit-ui/` as the source of component patterns. Start with `microkit-ui/README.md` for registry usage and project context. Browse `microkit-ui/content/interactions/` or `microkit-ui/registry/microkit/` for a specific interaction; use the registry source as the implementation reference.

For a React/Tailwind project, install only the requested component with the shadcn CLI, for example `npx shadcn@latest add @microkit/<component-name>`, from that React project directory. Check the component's imports and adapt it to the project's conventions.

Roche_Scoreboard is a native WPF/.NET application. Do not run the shadcn CLI against its root or add React/Tailwind dependencies to the scoreboard. When working on Roche_Scoreboard, translate the interaction idea into WPF/XAML and its existing C# patterns, preserving keyboard behavior, reduced-motion/accessibility needs, and existing visual conventions.
