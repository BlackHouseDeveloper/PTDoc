# PTDoc - AI Coding Assistant Instructions

## Working Agreement

**Before starting any task:**
1. **Restate the task** in 1-2 lines to confirm understanding
2. **Identify 0-3 relevant docs** from the Doc Map below (only if needed for the task)
3. **Use existing patterns** - prefer reusing over inventing new approaches
4. **Small commits** - implement changes incrementally
5. **Don't refactor unrelated code** unless explicitly required

**Reference Docs Only When Relevant**
- Do NOT open/cite every doc for every task
- Consult docs only when they directly affect your current work
- Trust your knowledge of .NET/Blazor for standard patterns

---

## Doc Map - When to Consult What

### Setup & Running
- **[README.md](../README.md)** - First-time setup, running platforms, prerequisites
  - **Use when:** Setup issues, build errors, platform-specific launch problems
  - **Skip when:** Writing code, implementing features

### Architecture & Boundaries  
- **[docs/ARCHITECTURE.md](../docs/ARCHITECTURE.md)** - Clean Architecture layers, dependency rules
  - **Use when:** Adding services, crossing layer boundaries, data access changes
  - **Skip when:** UI-only changes, styling, component props

### Blazor Specifics
- **[docs/Blazor-Context.md](../docs/Blazor-Context.md)** - Lifecycle, parameters, pitfalls, MAUI Hybrid
  - **Use when:** Component not rendering, lifecycle issues, parameter binding problems
  - **Skip when:** Standard component creation, basic event handling

### UI Implementation
- **[docs/context/ptdoc-figma-make-prototype-v5-context.md](../docs/context/ptdoc-figma-make-prototype-v5-context.md)** - Design system, component specs
  - **Use when:** Implementing UI from Figma, converting React→Blazor, design tokens
  - **Skip when:** Backend logic, API changes, database migrations
  - **Figma Link:** https://www.figma.com/make/1Fd3pzaGzvHboxFKuCz4dY/PTDoc-Prototype-v5

### Styling
- **[docs/style-system.md](../docs/style-system.md)** - Design tokens (`tokens.css`), theming
  - **Use when:** Styling components, adding colors/spacing, theme support
  - **Skip when:** Logic changes, not touching CSS

### Database Operations
- **[docs/EF_MIGRATIONS.md](../docs/EF_MIGRATIONS.md)** - EF Core migrations, database setup
  - **Use when:** Schema changes, adding migrations, database errors
  - **Skip when:** UI work, no data model changes

### Platform Differences
- **[docs/RUNTIME_TARGETS.md](../docs/RUNTIME_TARGETS.md)** - Web vs MAUI differences
  - **Use when:** Platform-specific behavior (auth, storage, API URLs)
  - **Skip when:** Shared component logic

### Development Workflows
- **[docs/DEVELOPMENT.md](../docs/DEVELOPMENT.md)** - Scripts, testing, workflows
  - **Use when:** Running tests, using helper scripts, debugging setup
  - **Skip when:** Implementation work

---

## Quick Reference - Critical Rules Only

### Project Structure (Clean Architecture)
```
PTDoc.Core         → Domain entities (ZERO dependencies)
PTDoc.Application  → Interfaces/contracts (depends only on Core)
PTDoc.Infrastructure → Implementations (depends on Application)
PTDoc.Api/Web/Maui → Presentation (wires up DI)
PTDoc.UI           → Shared Blazor components
```

**Never:** Reference Infrastructure from Application or Core

### Blazor Components (Most Common Rules)

**Component Naming:**
- **MUST** use PascalCase: `PTDocMetricCard.razor` (never lowercase)
- Update `_Imports.razor` when adding new namespaces

**Parameters:**
- Mark with `[Parameter]` attribute
- **NEVER mutate parameters after initialization** (treat as read-only)
- Use `[EditorRequired]` for required params
- Two-way binding: `[Parameter] T Value` + `[Parameter] EventCallback<T> ValueChanged`

**Wrapper Components:**
- **MUST** define `[Parameter] RenderFragment? ChildContent`  
- **MUST** render `@ChildContent` in markup

**Async Loading:**
```csharp
@if (isLoading) {
    <p>Loading...</p>
} else if (data != null) {
    <!-- Render data -->
}
```
**Always show loading state** - never leave blank during async operations

**Lifecycle:**
- `OnInitializedAsync` - data fetching
- `OnAfterRenderAsync(bool firstRender)` - JS interop (check `firstRender`)
- Rarely call `StateHasChanged()` - only for external events (timers, non-Blazor callbacks)

**Styling:**
- **Always use tokens**: `var(--primary)`, `var(--spacing-4)` from `tokens.css`
- **Never hardcode** colors, spacing, or typography
- Support light/dark themes via token switching

### Platform-Specific Patterns

**API Base URLs:**
- Android emulator: `http://10.0.2.2:5170`
- iOS/Mac: `http://localhost:5170`
- Override: `PTDoc_API_BASE_URL` environment variable

**Auth Patterns:**
- MAUI: `SecureStorageTokenStore` (JWT tokens)
- Web: Cookie-based (15min inactivity, 8hr absolute)
- API: JWT Bearer

### Database (EF Core)

**Quick Commands:**
```bash
./PTDoc-Foundry.sh --create-migration  # Create & apply migration
./PTDoc-Foundry.sh --seed              # Seed dev data
```

**Path Resolution:** `PFP_DB_PATH` env → `appsettings` → fallback `PTDoc.db`

### Healthcare & Accessibility

- **HIPAA**: Maintain audit trails for patient data
- **WCAG 2.1 AA**: Keyboard nav + screen reader support mandatory
- **Session limits**: 15min inactivity, 8hr absolute (web)

---

## When to Consult Docs - Decision Checklist

**Consult docs when:**
- [ ] Adding/changing services across architecture layers
- [ ] Implementing UI from Figma (need design specs)
- [ ] Component not rendering (lifecycle/parameter issues)
- [ ] Database schema changes (migrations needed)
- [ ] Platform-specific behavior (auth, storage, APIs)
- [ ] First-time setup or build issues
- [ ] Accessibility requirements unclear

**Don't consult docs for:**
- Standard .NET/C# patterns you already know
- Basic Blazor component creation
- Simple event handling or prop passing  
- Standard CRUD operations following existing patterns
- CSS styling using existing tokens

---

## File Organization Quick Reference

```
PTDoc.Core/        → Domain entities (zero deps)
PTDoc.Application/ → Interfaces, DTOs (depends on Core)
PTDoc.Infrastructure/ → EF Core, services (implements Application)
PTDoc.Api/         → REST API, JWT auth
PTDoc.Web/         → Blazor WebAssembly web app
PTDoc.Maui/        → .NET MAUI Blazor app (mobile/desktop)
PTDoc.UI/          → Shared Blazor components (reusable)
  ├── Components/  → UI components
  ├── Pages/       → Routable pages
  └── wwwroot/css/ → Design tokens, styles
```

**Location Rules:**
- Reusable components → `PTDoc.UI/Components/`
- Platform-specific → `PTDoc.Web/` or `PTDoc.Maui/`
- DTOs → `PTDoc.Application/DTOs/`
- Domain entities → `PTDoc.Core/Models/`
- Services interfaces → `PTDoc.Application/Services/`
- Services implementations → `PTDoc.Infrastructure/Services/`

---

## Common Pitfalls - Quick Fixes

| Problem | Likely Cause | Fix |
|---------|-------------|-----|
| Component invisible | Lowercase name / missing `_Imports.razor` | PascalCase + add `@using` |
| Blank during load | No loading indicator | Add `@if (isLoading)` block |
| Parameter overwritten | Mutating `[Parameter]` property | Use internal field instead |
| DI lifetime issue | Singleton with user state in Blazor Server | Use Scoped services |
| Build errors | Wrong project reference | Check layer boundaries in architecture |

---

## Quick Commands

```bash
# Setup & Build
./PTDoc-Foundry.sh              # Environment setup
./cleanbuild-ptdoc.sh           # Clean build
dotnet build PTDoc.sln          # Build all

# Run
./run-ptdoc.sh                  # Interactive launcher
dotnet run --project src/PTDoc.Api --urls http://localhost:5170   # API
dotnet run --project src/PTDoc.Web                                # Web

# Database
./PTDoc-Foundry.sh --create-migration --seed   # Setup DB
EF_PROVIDER=sqlite dotnet ef migrations add MigrationName --project src/PTDoc.Infrastructure --startup-project src/PTDoc.Api

# Test
dotnet test                     # All tests
dotnet test --filter "Category=Unit"   # Unit tests only
```

---

**Last Updated:** February 2026  
**Framework:** .NET 8.0 | **Platforms:** Web, iOS, Android, macOS  
**Healthcare:** HIPAA-conscious design required

---

## Task-Driven Implementation Pattern

For every task, follow this flow:

1. **Understand** - Restate task in 1-2 lines
2. **Identify** - Which 0-3 docs apply? (Use decision checklist above)
3. **Check** - Are there existing patterns to reuse?
4. **Implement** - Small, incremental changes
5. **Verify** - Build, test, check for errors
6. **Document** - Only if creating new patterns (not every change)
