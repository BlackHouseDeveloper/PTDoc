# Patients Page Implementation Summary

## Overview
Implemented the Patients page matching the Figma prototype (Light + Dark themes) using tokens and CSS variables only. All components are UI-parity ready with stubs for functionality.

## Files Created

### Models
- **PTDoc.UI/Components/Patients/Models/PatientListItemVm.cs**
  - UI-only view model with: Id, DisplayName, DateOfBirth, LastVisit, StatusLabel, StatusVariant

### Components

#### 1. PatientPageHeader
- **PTDoc.UI/Components/Patients/PatientPageHeader.razor**
- **PTDoc.UI/Components/Patients/PatientPageHeader.razor.css**
- Features:
  - Navy background (--secondary)
  - Title + subtitle display
  - Optional back button
  - Actions slot (RenderFragment)
  - data-testid="patients-header"
- Parameters: Title, Subtitle, Actions, ShowBackButton, OnBack

#### 2. PatientSearchInput
- **PTDoc.UI/Components/Patients/PatientSearchInput.razor**
- **PTDoc.UI/Components/Patients/PatientSearchInput.razor.css**
- Features:
  - Search icon (left-positioned)
  - Two-way binding (Value/ValueChanged)
  - Matches Figma specs: border-radius (10px), padding, colors
  - Debounced service query handled by Patients page (300ms cancellation-based debounce)
  - data-testid="patients-search"
- Parameters: Value, ValueChanged, Placeholder

#### 3. PatientCard
- **PTDoc.UI/Components/Patients/PatientCard.razor**
- **PTDoc.UI/Components/Patients/PatientCard.razor.css**
- Features:
  - White card with border (2px, --border)
  - Rounded corners (--radius-xl / 14px)
  - Name + StatusBadge header
  - Two-column details grid (DOB, Last Visit)
  - Clickable with keyboard support
  - Hover state (border color + shadow)
  - Navigation wired via parent callback
  - data-testid="patient-card-{id}"
- Parameters: Patient (PatientListItemVm), OnOpen

#### 4. PatientCardSection
- **PTDoc.UI/Components/Patients/PatientCardSection.razor**
- **PTDoc.UI/Components/Patients/PatientCardSection.razor.css**
- Features:
  - Responsive 3-column grid (desktop), 2-col (tablet), 1-col (mobile)
  - Empty state placeholder
  - Empty-state icon treatment
  - Optional ItemTemplate for custom rendering
  - data-testid="patients-card-section"
- Parameters: Items, ItemTemplate, OnCardClick

### Page
- **PTDoc.UI/Pages/Patients.razor**
- **PTDoc.UI/Pages/Patients.razor.css**
- Features:
  - Composes header -> search -> card section
  - Real patient list loading via IPatientService
  - Debounced server-side search filtering
  - "Add Patient" button (opens AddPatientModal)
  - Loading state rendered with LoadingSkeleton components
  - Create flow wired through IPatientService and toast feedback

## Files Modified
- **PTDoc.UI/_Imports.razor** - Added Patients namespace imports
- **PTDoc.UI/Components/StatusBadge.razor.css** - Added border to success variant to match Figma

## Design Tokens Used
All components use only CSS variables:
- Colors: --background, --foreground, --card, --border, --primary, --secondary, --success, --warning, --muted-foreground
- Spacing: --spacing-1 through --spacing-16
- Typography: --font-family-base, --text-sm, --text-base, --text-2xl, --font-weight-medium, --line-height-normal
- Radii: --radius-md, --radius-lg, --radius-xl
- Shadows: --shadow-md
- Transitions: --transition-fast

## Light/Dark Theme Support
All components use semantic tokens that automatically switch with theme toggle. No hardcoded colors.

## Responsive Behavior
- Desktop (≥1200px): 3-column grid
- Tablet (768-1199px): 2-column grid
- Mobile (≤767px): 1-column grid, adjusted padding

## Accessibility
- Keyboard navigation (Enter/Space on cards)
- ARIA labels on interactive elements
- Focus-visible outlines
- Semantic HTML (button, h1-h3, p)

## Current Implementation Notes
1. **PatientSearchInput**: Input emits immediate changes; debounce and API calls are orchestrated in Patients page logic.
2. **PatientCard**: Card open action routes through parent callback to page-level navigation handling.
3. **PatientCardSection**: Empty state includes iconography and responsive card grid behavior.
4. **PatientsPage**:
  - Uses IPatientService for load/search/create.
  - Uses 300ms cancellation-based debounce for search.
  - Surfaces operation outcomes through toast notifications.
5. **Loading behavior**: Skeleton loaders render while async patient queries are in-flight.

## Data Source
Patient list data is loaded from API-backed patient service calls (no page-level hardcoded sample set).

## Build Status
Build/test verification should be run from the current branch and environment before release.

## Testing Hooks
All components include data-testid attributes:
- patients-header
- patients-search
- patients-card-section
- patient-card-{id}

## Next Steps
1. Test Light/Dark theme toggle
2. Test responsive breakpoints
3. Integrate patient service
4. Implement navigation to patient detail page
5. Add loading skeleton for async operations
