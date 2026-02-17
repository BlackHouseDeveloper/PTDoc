# PR Checklist

## Core Requirements
- [ ] Web is DB-stateless: no DbContext/EF packages and no Infrastructure reference
- [ ] Devices (MAUI) include EF Core + SQLite and publish successfully
- [ ] CHANGELOG updated in this PR (manual until CI automation is established)
- [ ] ✅ All changes emphasized on success in documentation

## Code Quality & Testing
- [ ] All StyleCop formatting checks pass (`dotnet format --verify-no-changes`)
- [ ] All Roslynator analysis passes (severity-level: info)
- [ ] Unit tests pass for affected components
- [ ] Cross-platform compatibility verified (Web, Android, iOS, macOS)

## Artifact Testing & Validation
- [ ] **Build Artifacts**: All target frameworks build successfully
  - [ ] net8.0 (Web/API projects)
  - [ ] net8.0-android (Android MAUI)
  - [ ] net8.0-ios (iOS MAUI) 
  - [ ] net8.0-maccatalyst (macOS MAUI)
  - [ ] Blazor Web App (hybrid Server/WASM deployment)

- [ ] **Functional Testing**: Core features work as expected
  - [ ] Patient management operations (if applicable)
  - [ ] Assessment creation and editing (if applicable)
  - [ ] Database operations (if applicable)
  - [ ] Cross-platform UI consistency

- [ ] **Performance Impact**: No significant performance degradation
  - [ ] Application startup time acceptable
  - [ ] UI responsiveness maintained
  - [ ] Memory usage within expected bounds

## Platform-Specific Validation
- [ ] **Android**: APK builds and installs successfully
- [ ] **iOS**: IPA builds (unsigned) successfully  
- [ ] **Web**: Blazor Web App loads and functions correctly
- [ ] **macOS**: App builds via Catalyst successfully

## Documentation & Communication
- [ ] Changes documented in appropriate files (ARCHITECTURE.md, CHANGELOG.md, etc.)
- [ ] Breaking changes clearly identified
- [ ] User-facing changes explained
- [ ] Developer impact documented (new services, APIs, components)

## Summary

### Title
**UI Implementation: Patients List & Profile Pages (Phase 1 of 2)**

### Description
This PR implements the **Patients List** (`/patients`) and **Patient Profile** (`/patient/{id}`) pages as Phase 1 of a two-phase implementation. All changes are **UI-only** in the `PTDoc.UI` shared component library, with no backend integration yet.

**What Changed:**
- 26 new files: 12 Blazor components (`.razor` + `.css`), 2 view models (`.cs`)
- Patients list page with search filtering, responsive grid layout (3/2/1 columns), and Add Patient integration
- Patient profile page with editable demographics, tabbed clinical info (Timeline/Notes/Docs), and primary action button
- Full Figma Make Prototype v5 design system compliance using design tokens from `tokens.css`
- WCAG 2.1 AA accessibility compliance (keyboard navigation, ARIA labels, semantic HTML)
- Light/dark theme support via CSS custom properties

**Why Phase 1 Approach:**
- Allows UX/design review and iteration before backend complexity
- Enables parallel development: UI team can refine while backend team implements `IPatientService`
- Reduces PR size for focused review on component structure and design fidelity

**Phase 2 Scope** (separate PR):
- Backend service integration: Create `IPatientService` interface, implementation, and API endpoints
- Replace sample data with real data fetching from PTDoc.Api via HTTP
- Implement save functionality for editable fields
- Add LoadingSkeleton component for async states
- Complete Notes and Documents tab implementations

**References:**
- Implementation details: [docs/PATIENTS_PAGE_IMPLEMENTATION.md](/docs/PATIENTS_PAGE_IMPLEMENTATION.md)
- Design source: [docs/context/ptdoc-figma-make-prototype-v5-context.md](/docs/context/ptdoc-figma-make-prototype-v5-context.md)
- Architecture adherence: [docs/ARCHITECTURE.md](/docs/ARCHITECTURE.md)
- Design tokens: [docs/style-system.md](/docs/style-system.md)
- Accessibility: [docs/ACCESSIBILITY_USAGE.md](/docs/ACCESSIBILITY_USAGE.md)
- Figma Design: [PTDoc Prototype v5](https://www.figma.com/make/1Fd3pzaGzvHboxFKuCz4dY/PTDoc-Prototype-v5)

### Type of Change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [x] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Performance improvement
- [ ] Code refactoring

### Impact Areas
- [x] User interface (PTDoc.UI shared components)
- [ ] Core business logic
- [ ] Database schema/migrations
- [ ] Build system
- [ ] CI/CD workflows

## Testing Instructions

Please test the following scenarios:

### 1. Basic Functionality
```bash
# Clone and setup
git checkout UI-Implementation-Page-Patients-\(\"/patients\"\)
./PTDoc-Foundry.sh

# Test builds
dotnet build PTDoc.sln -c Release
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Release
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-android
```

### 2. UI Testing - Patients List Page
```bash
# Run web application
dotnet run --project src/PTDoc.Web

# Navigate to http://localhost:5001/patients (or https://localhost:5002/patients)
```

**Expected Behavior:**
- ✅ Page displays "Patients" header with navy background
- ✅ Subtitle shows "6 total patients"
- ✅ "Add Patient" button opens AddPatientModal
- ✅ Search input filters patients by name (client-side)
- ✅ 6 patient cards displayed in responsive grid:
  - Desktop (≥1200px): 3 columns
  - Tablet (768-1199px): 2 columns
  - Mobile (<768px): 1 column
- ✅ Patient cards show: Name, DOB, MRN, last visit, status badge
- ✅ Cards have hover effects and focus outlines
- ✅ Clicking card navigates to patient profile

**Sample Patients:**
1. Sarah Johnson (Active) - DOB: 03/15/1985, MRN: MRN-001
2. Michael Chen (Active) - DOB: 07/22/1990, MRN: MRN-002
3. Emily Rodriguez (Pending) - DOB: 11/30/1978, MRN: MRN-003
4. James Williams (Active) - DOB: 05/10/1982, MRN: MRN-004
5. Lisa Anderson (Active) - DOB: 09/18/1995, MRN: MRN-005
6. Robert Martinez (Inactive) - DOB: 02/25/1970, MRN: MRN-006

### 3. UI Testing - Patient Profile Page
```bash
# From patients list, click any patient card
# Or navigate directly to: http://localhost:5001/patient/1
```

**Expected Behavior:**
- ✅ Profile header displays patient name with back button
- ✅ Back button returns to `/patients`
- ✅ Demographics card shows: Name, DOB, MRN, Phone, Email, Address
- ✅ Edit mode toggles for demographics (icon button in header)
- ✅ Clinical info card has 3 tabs: Timeline, Notes, Documents
- ✅ Timeline tab displays 5 sample entries with dates/descriptions
- ✅ "Start New Note" primary action button visible
- ✅ Responsive layout adapts to screen size

### 4. Accessibility Testing
```bash
# Keyboard Navigation Test
Tab through all interactive elements
Press Enter/Space on patient cards → should navigate to profile
Press Escape on modals → should close

# Screen Reader Test
Use VoiceOver (macOS): Cmd+F5
Verify all components have proper ARIA labels
Verify status badges are announced correctly
```

### 5. Theme Toggle Testing
```bash
# If theme toggle exists in app
Toggle between light and dark modes
Verify all components use design tokens (no broken colors)
Check contrast ratios meet WCAG 2.1 AA
```

### 6. Cross-Platform Testing (MAUI)
```bash
# Android
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-android
# Deploy to emulator or device, navigate to Patients page

# iOS (requires Mac)
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-ios
# Deploy to simulator, navigate to Patients page

# macOS Catalyst
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-maccatalyst
# Run app, navigate to Patients page
```

## Verification Commands

Run these commands locally to verify the changes:

```bash
# Code quality checks
dotnet format PTDoc.sln --verify-no-changes --verbosity diagnostic
roslynator analyze PTDoc.sln --severity-level info

# Build verification - All projects
dotnet build PTDoc.sln -c Release

# Build verification - Web (net8.0)
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Release

# Build verification - API (net8.0)
dotnet build src/PTDoc.Api/PTDoc.Api.csproj -c Release

# Build verification - MAUI targets
dotnet publish src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-android
dotnet publish src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-ios
dotnet publish src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-maccatalyst

# Test execution (all existing tests should still pass)
dotnet test PTDoc.sln -c Release
dotnet test --filter "Category=Unit"
```

## Review Feedback

**For Reviewers**: Please comment on:
- [ ] **Design Fidelity**: Do components match Figma v5 specifications?
- [ ] **Accessibility**: Test keyboard navigation and screen reader support
- [ ] **Responsive Design**: Test at 3 breakpoints (desktop/tablet/mobile)
- [ ] **Theme Support**: Verify light/dark mode appearance
- [ ] **Component Structure**: Are components properly decomposed and reusable?
- [ ] **Code Quality**: Clean Architecture adherence (UI-only, no business logic)
- [ ] **Performance**: Page load and interaction responsiveness
- [ ] **Cross-Platform**: Test on at least 2 platforms (Web + Android/iOS)

**Post-Review**: Once approved, Phase 2 backend integration work can begin in parallel or follow-up PR.

## Changelog Context

This PR includes a new [docs/CHANGELOG.md](/docs/CHANGELOG.md) file following the [Keep a Changelog](https://keepachangelog.com/en/1.0.0/) format. Future PRs should update the `[Unreleased]` section with their changes.

**Future Enhancement:** Once CI/CD workflows are established, CHANGELOG updates will be automated to include build outcomes and deployment status.

## Future Work - CI/CD & MCP Automation

This PR does not include CI/CD workflows, but the following are planned for future implementation:

### Planned CI/CD Workflows
- `ptdoc-build-validation.yml` - Multi-platform build validation (Web, Android, iOS, macOS)
- `ptdoc-code-quality.yml` - StyleCop, Roslynator, and test execution
- `ptdoc-database-diagnostics.yml` - EF Core migration validation (for Phase 2)
- `ptdoc-accessibility-audit.yml` - Automated WCAG 2.1 AA compliance checks

### Planned MCP Workflows
- Automated CHANGELOG updates with CI outcomes
- Copilot auto-fix workflows for common CI failures
- Artifact validation and platform-specific testing

**Note:** Until CI is established, all quality checks should be run manually using the verification commands above.

## Phase 2 Roadmap - Backend Integration

The following work is **out of scope** for this PR and will be addressed in Phase 2:

### Service Layer (PTDoc.Application)
- [ ] Create `IPatientService` interface with methods:
  - `Task<IEnumerable<PatientListItemDto>> GetAllAsync()`
  - `Task<PatientProfileDto> GetByIdAsync(int id)`
  - `Task<IEnumerable<PatientListItemDto>> SearchAsync(string query)`
  - `Task<PatientProfileDto> UpdateAsync(int id, UpdatePatientDto dto)`

### Infrastructure Layer (PTDoc.Infrastructure)
- [ ] Implement `PatientService` with EF Core data access
- [ ] Add Patient entity to DbContext
- [ ] Create database migration for Patient table
- [ ] Add repository pattern (if needed)

### Domain Layer (PTDoc.Core)
- [ ] Create Patient entity in `PTDoc.Core.Models`
- [ ] Add DTOs: `PatientListItemDto`, `PatientProfileDto`, `UpdatePatientDto`
- [ ] Define validation rules (FluentValidation or DataAnnotations)

### API Layer (PTDoc.Api)
- [ ] Add `PatientsController` with endpoints:
  - `GET /api/patients` - List all patients
  - `GET /api/patients/{id}` - Get patient by ID
  - `GET /api/patients/search?q={query}` - Search patients
  - `PUT /api/patients/{id}` - Update patient
  - `POST /api/patients` - Create patient (integrate with AddPatientModal)
- [ ] Add authentication/authorization (JWT bearer)
- [ ] Add audit logging for HIPAA compliance

### UI Enhancements (PTDoc.UI)
- [ ] Replace sample data in `Patients.razor` with `IPatientService` calls
- [ ] Replace sample data in `PatientProfile.razor` with `IPatientService.GetByIdAsync`
- [ ] Implement debounced search with backend API
- [ ] Add LoadingSkeleton component for async states
- [ ] Implement save functionality for editable fields
- [ ] Complete Notes tab implementation
- [ ] Complete Documents tab implementation
- [ ] Implement "Start New Note" workflow routing

### Testing
- [ ] Unit tests for `PatientService`
- [ ] Integration tests for API endpoints
- [ ] Blazor component tests for patient pages
- [ ] E2E tests for patient workflows

---

**Branch:** `UI-Implementation-Page-Patients-("/patients")`  
**Base:** `main`  
**Reviewer Focus:** Design fidelity, accessibility, responsive behavior, component architecture  
**Healthcare Context:** HIPAA-conscious design maintained (audit trails, secure session management for future auth integration)

