# PTDoc Global Style System (v5)

**Purpose:** Provide a maintainable, token-driven styling foundation aligned to the PTDoc Figma Make Prototype v5.

## Canonical Sources
- Consolidated context: [docs/context/ptdoc-figma-make-prototype-v5-context.md](context/ptdoc-figma-make-prototype-v5-context.md)
- React prototype references (provided externally during implementation):
  - P T D O C design system reference
  - Design token reference

## Where Tokens Live
- Tokens file: [src/PTDoc.UI/wwwroot/css/tokens.css](../src/PTDoc.UI/wwwroot/css/tokens.css)
- Global base styles: [src/PTDoc.UI/wwwroot/css/app.css](../src/PTDoc.UI/wwwroot/css/app.css)

Tokens are applied via CSS custom properties and are consumed by global styles and component styles.

## Naming Conventions
The system uses existing semantic (role-based) token names from the prototype:
- Color roles: `--background`, `--foreground`, `--primary`, `--secondary`, `--muted`, `--border`, `--success`, `--warning`, `--destructive`, `--info`, etc.
- Typography: `--font-family-base`, `--text-sm`, `--text-base`, `--text-2xl`, `--font-weight-medium`, `--line-height-normal`
- Spacing: `--spacing-1`…`--spacing-16`
- Radii: `--radius-sm`, `--radius-md`, `--radius-lg`, `--radius-xl`
- Shadows: `--shadow-sm`, `--shadow-md`, `--shadow-lg`, `--shadow-xl`
- Motion: `--transition-fast`, `--transition-normal`, `--transition-slow`

**Token semantics constraint**
- Tokens are semantic roles, not hue- or brand-specific names.
- Do not introduce ad-hoc or hue-based tokens (e.g., `--blue-dark`, `--secondary-2`).
- Existing names like `--primary` and `--background` represent roles, not fixed colors.

## How to Add New Tokens
1. Add the token to [src/PTDoc.UI/wwwroot/css/tokens.css](../src/PTDoc.UI/wwwroot/css/tokens.css) for both `:root` and `.dark` (if theme-specific).
2. Prefer semantic role names (e.g., `--success`, `--muted-foreground`), not raw colors or hue-based names.
3. Update this doc with the new token and its usage.

## Do / Don’t
**Do**
- Use tokens via `var(--token-name)` for colors, spacing, and typography.
- Use semantic roles (e.g., `--primary`, `--foreground`) instead of raw hex values.
- Keep base styles in [src/PTDoc.UI/wwwroot/css/app.css](../src/PTDoc.UI/wwwroot/css/app.css).

**Bootstrap coexistence rules**
- Bootstrap is treated as a layout/utility baseline only.
- Design tokens must not reference Bootstrap variables.
- Custom components should not depend on Bootstrap component styling.

**Don’t**
- Hardcode hex colors or pixel values inside components without a token.
- Introduce a new styling paradigm (CSS-in-JS, Tailwind) unless project-wide decisions change.
- Override global base styles inside components unless necessary.

## Z-Index & Elevation
The z-index scale is part of the global token system and lives in [src/PTDoc.UI/wwwroot/css/tokens.css](../src/PTDoc.UI/wwwroot/css/tokens.css). Use these tokens for layering to avoid ad-hoc z-index values (e.g., `--z-modal`, `--z-tooltip`, `--z-toast`).

## Breakpoints (Canonical)
Breakpoints follow the consolidated context and are defined in tokens:
- Mobile: ≤767px
- Tablet: 768–1199px
- Desktop: ≥1200px

## Reusable Classes (Deferred)
Reusable motion/utility classes are intentionally deferred for now. Use tokens directly until a vetted set of classes is approved.

## Cross-Platform Interaction Constraints (Web + MAUI)
Global styles must avoid hover-only affordances and ensure focus-visible and accessibility parity across Web and MAUI. Interactive states should be keyboard- and touch-friendly in both environments.

## Example: Applying Tokens in .tsx (Prototype Reference)
```tsx
export function PrimaryButton({ children }: { children: React.ReactNode }) {
  return (
    <button
      style={{
        background: "var(--primary)",
        color: "var(--primary-foreground)",
        borderRadius: "var(--radius-md)",
        padding: "var(--spacing-2) var(--spacing-4)",
      }}
    >
      {children}
    </button>
  );
}
```

## Example: Applying Tokens in Blazor (Implementation)
```razor
<button class="ptdoc-button" style="background: var(--primary); color: var(--primary-foreground);">
  Save
</button>
```

## Conflicts & Clarifications Needed
None at this time.
