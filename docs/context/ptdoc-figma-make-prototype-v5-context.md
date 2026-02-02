# PTDoc Figma Make Prototype v5 - Consolidated Context Document

**Last Updated:** January 29, 2026  
**Version:** 1.0  
**Status:** Stage 1 Complete  
**Target Platform:** .NET 8 Blazor (Web + MAUI)  
**Source Platform:** React/TypeScript Prototype

---

## 0. Reference Links

### Canonical Design Reference
- **Figma Make Prototype v5:** https://www.figma.com/make/1Fd3pzaGzvHboxFKuCz4dY/PTDoc-Prototype-v5?p=f&t=s9McithEAB55SH6O-0
  - Use **Figma Desktop → Map / Pages / Layers navigation** to locate design context when implementing .tsx conversions
  - This is the authoritative design reference for all UI implementation decisions

### Key Documentation
- React Prototype Design System: `/styles/globals.css` (CSS custom properties)
- .NET Implementation: Clean Architecture with Blazor Server + MAUI
- Database: Entity Framework Core with SQLite
- Authentication: JWT (mobile) + Cookie-based (web)

---

## 1. Executive Summary

### What is PTDoc v5?

PTDoc is an **enterprise healthcare documentation platform** for physical therapy practices, enabling clinicians to:
- Document patient care using structured SOAP notes
- Manage patient demographics, insurance, and authorizations
- Track treatment progress and outcomes
- Generate reports and analytics
- Maintain HIPAA-compliant clinical records

### Current State
- **React Prototype (v5):** Design system complete in Figma Make with component library
- **.NET Implementation:** Clean Architecture foundation with Blazor Server + MAUI support
- **Goal:** Convert React/TypeScript prototype to production Blazor/.NET application

### Explicit Goals
1. **Preserve design fidelity** - Match Figma v5 visual specifications exactly
2. **Maintain accessibility** - WCAG 2.1 AA compliance across all features
3. **Cross-platform support** - Blazor Web + MAUI (iOS, Android, macOS)
4. **Healthcare compliance** - HIPAA-conscious security and audit trails
5. **Clean Architecture** - Strict layer separation and testability

### Explicit Non-Goals
- NOT building a new prototype - converting existing React design to .NET
- NOT changing core workflows - preserving established UX patterns
- NOT supporting legacy browsers - modern evergreen browsers only
- NOT implementing real-time collaboration features (future phase)

---

## 2. Decision Drivers

### Architectural Constraints

**Clean Architecture Enforcement** (Source: [docs/ARCHITECTURE.md](#architecture))
```
Core (Domain) → No dependencies
Application (Interfaces) → Depends only on Core
Infrastructure (EF Core, Services) → Implements Application interfaces
Presentation (API, Web, MAUI) → Wires up DI, references Infrastructure
```

**Dependency Rules:**
- Core MUST have zero dependencies
- Application defines contracts (interfaces), never implementations
- Infrastructure implements Application interfaces
- Presentation layers never reference each other directly

### Platform Constraints

**Runtime Targets** (Source: [docs/RUNTIME_TARGETS.md](#runtime-targets))

| Platform | Data Access | Authentication | Local Storage |
|----------|-------------|----------------|---------------|
| **Web (Blazor Server)** | API-only HTTP calls | Cookie-based (15min inactivity, 8hr absolute) | Memory/localStorage only |
| **MAUI (iOS/Android/macOS)** | EF Core + SQLite local | JWT tokens in SecureStorage | SQLite database |

**Critical Differences:**
- Web has NO local database - stateless client
- MAUI is offline-first with sync capability
- Android emulator uses `http://10.0.2.2:5170` for API (not localhost)
- iOS/Mac use `http://localhost:5170`

### Technology Principles

**Framework Requirements:**
- .NET 8.0.417 enforced via `global.json`
- Blazor components MUST use PascalCase naming
- All parameters marked `[Parameter]` - never mutate after initialization
- Use `OnInitializedAsync` for data loading with loading indicators
- NEVER call `StateHasChanged()` unless handling external events

**Blazor Component Lifecycle** (Source: [docs/Blazor-Context.md](#blazor-context))
1. `OnInitialized{Async}` - First run, fetch data
2. `OnParametersSet{Async}` - Runs on every parameter update
3. Render occurs
4. `OnAfterRender{Async}` - DOM ready, JS interop safe (use `firstRender` flag)

**Critical Blazor Rules:**
- Parameters are READ-ONLY inputs from parent
- Use internal state for component-managed values
- Two-way binding requires `[Parameter] T Value` + `[Parameter] EventCallback<T> ValueChanged`
- Always show loading states during async operations
- Components in new namespaces MUST be added to `_Imports.razor`

### Healthcare Compliance

**HIPAA Requirements:**
- Session timeouts: 15min inactivity (web), 8hr absolute maximum
- Audit trails for all PHI access
- Secure token storage (SecureStorage for mobile)
- Cookie security: HttpOnly, Secure, SameSite=Strict

---

## 3. Product Overview

### Target Users

**Primary Users:**
- **Physical Therapists (PT, DPT)** - Clinical documentation, treatment planning
- **Physical Therapist Assistants (PTA)** - Treatment execution, progress notes
- **Clinic Administrators** - Scheduling, billing, insurance management
- **Office Staff** - Patient intake, appointment coordination

### Key User Goals

**For Clinicians:**
1. Document patient encounters quickly (SOAP notes under 5 minutes)
2. Track patient progress over time with visual charts
3. Access patient history and prior authorizations instantly
4. Generate reports for compliance and outcome tracking

**For Administrators:**
5. Manage patient demographics and insurance information
6. Monitor authorization expiration dates
7. Generate billing and productivity reports
8. Control user roles and permissions

### Core Workflows

**1. Patient Intake Flow**
```
New Patient → Demographics → Insurance → Medical History → 
Presenting Condition → Consent Forms → Review & Submit
```

**2. Clinical Documentation Flow**
```
Select Patient → Create SOAP Note → 
[Subjective | Objective | Assessment | Plan] → 
Billing Codes → Sign & Lock
```

**3. Progress Tracking Flow**
```
View Patient → Progress Tracker → 
[Goals | Measurements | Functional Status] → 
Generate Report
```

---

## 4. Information Architecture / Navigation Map

### Primary Navigation Structure

```
┌─────────────────────────────────────────────┐
│  PTDoc Header                               │
│  [Dashboard] [Patients] [Schedule] [⋯]      │
└─────────────────────────────────────────────┘
         │
         ├──> Dashboard (Default landing)
         │    ├─ Today's Appointments
         │    ├─ Active Patients Count
         │    ├─ Pending Notes Alert
         │    └─ Auth Expiration Warnings
         │
         ├──> Patients
         │    ├─ Patient List (search/filter)
         │    ├─ Patient Detail
         │    │  ├─ Demographics Tab
         │    │  ├─ Insurance Tab
         │    │  ├─ Clinical History Tab
         │    │  └─ Progress Tab
         │    └─ New Patient Intake
         │
         ├──> Schedule (Future)
         │
         ├──> Notes (SOAP)
         │    ├─ Active Notes (drafts)
         │    ├─ Completed Notes
         │    ├─ Templates
         │    └─ AI Assist
         │
         ├──> Reports
         │    ├─ Financial Performance
         │    ├─ Clinical Outcomes
         │    ├─ Operational Efficiency
         │    └─ Compliance Reports
         │
         ├──> Export Center
         │    └─ Custom data exports
         │
         └──> Settings
              ├─ Profile
              ├─ Appearance (Theme toggle)
              ├─ Clinics
              ├─ Integrations
              ├─ Notifications
              ├─ Security (2FA)
              └─ Role Management
```

### Responsive Breakpoints

| Breakpoint | Width | Behavior |
|------------|-------|----------|
| **Mobile** | ≤767px | Single column, hamburger menu, 44px touch targets |
| **Tablet** | 768-1199px | 2-column hybrid, collapsible sidebar |
| **Desktop** | ≥1200px | Full sidebar + 3-4 column grid layouts |

---

## 5. UI Architecture

### Page Structure

All pages follow this template:
```
┌─────────────────────────────────────────────┐
│  Header (Persistent)                        │
│  [← Back] Page Title     [Actions] [Avatar] │
├─────────────────────────────────────────────┤
│  Subheader / Filters / Tabs (Optional)      │
├─────────────────────────────────────────────┤
│                                             │
│  Main Content Area                          │
│  (Forms, Cards, Tables, Charts)             │
│                                             │
└─────────────────────────────────────────────┘
```

### Layouts

**MainLayout.razor** (Primary)
- Persistent header with navigation
- Responsive sidebar (desktop) / hamburger menu (mobile)
- Theme toggle in header
- User profile dropdown

**BlankLayout.razor** (Auth)
- No navigation
- Centered content
- Used for Login/Signup pages

### Core Pages and Screens

| Page | Route | Purpose | Figma Screen |
|------|-------|---------|--------------|
| **Login** | `/login` | Authentication | Login.tsx |
| **Signup** | `/signup` | New user registration | SignUp.tsx |
| **Dashboard** | `/` or `/dashboard` | Overview landing page | Dashboard.tsx |
| **Patients List** | `/patients` | Patient directory | Patients.tsx |
| **Patient Detail** | `/patients/{id}` | Individual patient view | PatientDetail.tsx |
| **Patient Intake** | `/patients/new` | Multi-step intake form | Intake.tsx |
| **SOAP Notes** | `/notes` | Clinical documentation | Notes.tsx |
| **Progress Tracker** | `/patients/{id}/progress` | Outcome tracking | ProgressTracker.tsx |
| **Reports** | `/reports` | Analytics dashboard | Reports.tsx |
| **Export Center** | `/export` | Data export interface | ExportCenter.tsx |
| **Settings** | `/settings` | User preferences | Settings.tsx |
| **Role Management** | `/settings/roles` | User/permission admin | RoleManagement.tsx |

---

## 6. Component Catalog

### Foundational Components

#### PTDocButton

**Responsibility:** Primary interactive element for user actions

**Props/State:**
```csharp
[Parameter] public string Variant { get; set; } = "primary"; // primary | secondary | outline | destructive | ghost
[Parameter] public string Size { get; set; } = "default"; // sm | default | lg
[Parameter] public string Text { get; set; } = "";
[Parameter] public bool IsLoading { get; set; } = false;
[Parameter] public bool Disabled { get; set; } = false;
[Parameter] public EventCallback OnClick { get; set; }
[Parameter] public RenderFragment? ChildContent { get; set; }
```

**Variants:**
- `primary` - Emerald green (#16a34a light / #22c55e dark)
- `secondary` - Navy blue (#1a2b50 light / #000000 dark)
- `outline` - Transparent with border
- `destructive` - Red (#d93025 light / #ef4444 dark)
- `ghost` - No background, hover only

**Figma Reference:** Component → Button (all variants)

**Known .tsx Path:** `components/ui/button.tsx`

**Edge Cases:**
- Loading state shows spinner, disables interaction
- Disabled state reduces opacity to 0.5, removes pointer events
- Must meet 44px minimum touch target on mobile

**Source:** [BLAZOR_DESIGN_SYSTEM_EXPORT.md, BLAZOR_COMPONENTS_ADVANCED.md]

---

#### PTDocCard

**Responsibility:** Container for grouped content with consistent padding and borders

**Props/State:**
```csharp
[Parameter] public RenderFragment? ChildContent { get; set; }
[Parameter] public string Title { get; set; } = "";
[Parameter] public RenderFragment? Header { get; set; }
[Parameter] public RenderFragment? Footer { get; set; }
[Parameter] public string CssClass { get; set; } = "";
```

**Variants:**
- Default: White/dark card with subtle border
- Elevated: With shadow for emphasis
- Flat: No border, minimal styling

**Figma Reference:** Component → Card

**Known .tsx Path:** `components/ui/card.tsx`

**Edge Cases:**
- Header and Footer are optional
- Respects theme (light/dark mode)
- Min-height ensures content doesn't collapse

**Source:** [BLAZOR_DESIGN_SYSTEM_EXPORT.md]

---

#### PTDocInput

**Responsibility:** Text input field with label, validation, and helper text

**Props/State:**
```csharp
[Parameter] public string Id { get; set; } = Guid.NewGuid().ToString();
[Parameter] public string Label { get; set; } = "";
[Parameter] public string Type { get; set; } = "text"; // text | email | password | date | number
[Parameter] public string Value { get; set; } = "";
[Parameter] public EventCallback<string> ValueChanged { get; set; }
[Parameter] public string Placeholder { get; set; } = "";
[Parameter] public bool Required { get; set; } = false;
[Parameter] public string? ErrorMessage { get; set; }
[Parameter] public string? HelperText { get; set; }
[Parameter] public bool Disabled { get; set; } = false;
```

**Variants:**
- Text, email, password
- Date picker integration
- Number with validation

**Figma Reference:** Component → Input Field

**Known .tsx Path:** `components/ui/input.tsx`

**Edge Cases:**
- Error state shows red border + error message
- Required indicator (asterisk) automatically displayed
- Disabled state uses muted colors
- Min-height 48px (12) for accessibility

**Source:** [BLAZOR_DESIGN_SYSTEM_EXPORT.md, ACCESSIBILITY_USAGE.md]

---

#### PTDocBadge

**Responsibility:** Status indicators and labels

**Props/State:**
```csharp
[Parameter] public string Variant { get; set; } = "default"; // default | success | warning | error | info
[Parameter] public string Text { get; set; } = "";
[Parameter] public RenderFragment? ChildContent { get; set; }
```

**Variants:**
- `success` - Green (#34a853)
- `warning` - Yellow/orange (#f9ab00)
- `error` - Red (#d93025)
- `info` - Blue (#4285f4)

**Figma Reference:** Component → Badge

**Known .tsx Path:** `components/ui/badge.tsx`

**Edge Cases:**
- Text auto-truncates with ellipsis
- Icon support via ChildContent
- Inline or block display

**Source:** [THEME_VISUAL_GUIDE.md]

---

#### PTDocAlert

**Responsibility:** Contextual feedback messages and notifications

**Props/State:**
```csharp
[Parameter] public string Variant { get; set; } = "info"; // info | success | warning | error
[Parameter] public string Title { get; set; } = "";
[Parameter] public string Description { get; set; } = "";
[Parameter] public bool Dismissible { get; set; } = false;
[Parameter] public EventCallback OnDismiss { get; set; }
[Parameter] public string Icon { get; set; } = "";
```

**Variants:**
- `info` - Blue background with info icon
- `success` - Green background with checkmark
- `warning` - Yellow background with warning icon
- `error` - Red background with error icon

**Figma Reference:** Component → Alert

**Known .tsx Path:** `components/ui/alert.tsx`

**Edge Cases:**
- Auto-dismiss option (timeout)
- Dismissible shows close button
- Icon can be overridden or omitted

**Source:** [BLAZOR_COMPONENTS_ADVANCED.md]

---

#### PTDocModal

**Responsibility:** Modal dialog overlay for forms, confirmations, and detail views

**Props/State:**
```csharp
[Parameter] public bool IsOpen { get; set; } = false;
[Parameter] public EventCallback<bool> IsOpenChanged { get; set; }
[Parameter] public string Title { get; set; } = "";
[Parameter] public RenderFragment? ChildContent { get; set; }
[Parameter] public RenderFragment? Footer { get; set; }
[Parameter] public string Size { get; set; } = "default"; // sm | default | lg | xl
[Parameter] public bool CloseOnBackdropClick { get; set; } = true;
```

**Variants:**
- Small (sm) - Confirmations
- Default - Forms
- Large (lg) - Multi-section content
- Extra Large (xl) - Full details

**Figma Reference:** Component → Modal / Dialog

**Known .tsx Path:** `components/ui/modal.tsx`

**Edge Cases:**
- Focus trap when open (accessibility)
- Restores focus to trigger element on close
- ESC key closes modal
- Backdrop click configurable
- Mobile: Full-screen on small viewports

**Source:** [BLAZOR_COMPONENTS_ADVANCED.md, ACCESSIBILITY_USAGE.md]

---

#### PTDocTable

**Responsibility:** Tabular data display with sorting, filtering, and pagination

**Props/State:**
```csharp
[Parameter] public RenderFragment? TableHeader { get; set; }
[Parameter] public RenderFragment? TableBody { get; set; }
[Parameter] public string AriaLabel { get; set; } = "Data table";
[Parameter] public bool Striped { get; set; } = false;
[Parameter] public bool Hoverable { get; set; } = true;
```

**Variants:**
- Default: Basic table
- Striped: Alternating row colors
- Hoverable: Highlight on row hover
- Compact: Reduced padding

**Figma Reference:** Component → Table

**Known .tsx Path:** `components/ui/table.tsx`

**Edge Cases:**
- Responsive: Horizontal scroll on mobile
- Empty state message when no rows
- Loading skeleton during data fetch
- Pagination controls (separate component)
- Column sorting indicators

**Source:** [BLAZOR_COMPONENTS_ADVANCED.md]

---

### Composite Components

#### PTDocMetricCard

**Responsibility:** Dashboard KPI display with trend indicators

**Props/State:**
```csharp
[Parameter] public string Title { get; set; } = "";
[Parameter] public string Value { get; set; } = "";
[Parameter] public string Trend { get; set; } = ""; // e.g., "+12%"
[Parameter] public string TrendDirection { get; set; } = "neutral"; // up | down | neutral
[Parameter] public string Icon { get; set; } = "";
```

**Figma Reference:** Dashboard → Metric Cards

**Known .tsx Path:** `components/dashboard/MetricCard.tsx`

**Edge Cases:**
- Trend color: green (up/positive), red (down/negative), gray (neutral)
- Icon optional
- Value can be number, currency, percentage

**Source:** [DASHBOARD_VISUAL_GUIDE.md]

---

#### SOAPNoteEditor

**Responsibility:** Structured SOAP note documentation interface

**Props/State:**
```csharp
[Parameter] public int? PatientId { get; set; }
[Parameter] public int? NoteId { get; set; } // Null for new notes
[Parameter] public string VisitType { get; set; } = "Initial Evaluation";
[Parameter] public EventCallback OnSave { get; set; }
```

**Sections:**
- **S**ubjective: Patient complaints, history
- **O**bjective: Measurements, observations
- **A**ssessment: Clinical interpretation
- **P**lan: Treatment plan, goals

**Figma Reference:** Notes → SOAP Note Editor

**Known .tsx Path:** `pages/notes/SOAPEditor.tsx`

**Edge Cases:**
- Auto-save draft every 2 minutes
- AI-powered suggestions (optional feature)
- Billing code recommendations
- Note locking after signature
- Template support

**Source:** [NOTES_VISUAL_GUIDE.md]

---

#### PatientIntakeForm

**Responsibility:** Multi-step patient registration form

**Props/State:**
```csharp
[Parameter] public EventCallback<PatientDTO> OnComplete { get; set; }
```

**Steps:**
1. Demographics (name, DOB, contact)
2. Insurance (primary/secondary)
3. Medical History
4. Presenting Condition
5. Consent Forms

**Figma Reference:** Intake → Multi-Step Form

**Known .tsx Path:** `pages/intake/IntakeForm.tsx`

**Edge Cases:**
- Progress indicator (60% complete, Step 3 of 5)
- Save draft between steps
- Validation per step before advancing
- Back navigation preserves data
- Insurance verification integration (future)

**Source:** [INTAKE_VISUAL_GUIDE.md]

---

### Navigation Components

#### Sidebar

**Responsibility:** Primary navigation menu (desktop)

**Props/State:**
```csharp
[Parameter] public bool IsCollapsed { get; set; } = false;
[Parameter] public EventCallback<bool> IsCollapsedChanged { get; set; }
```

**Figma Reference:** Layout → Sidebar Navigation

**Known .tsx Path:** `components/layout/Sidebar.tsx`

**Edge Cases:**
- Collapsed shows icons only
- Active route highlighted
- Mobile: Off-canvas drawer

**Source:** [BLAZOR_COMPONENTS_ADVANCED.md]

---

## 7. Design System / Tokens

### Color Tokens

**Primary Palette:**

| Token | Light Mode | Dark Mode | Usage |
|-------|------------|-----------|-------|
| `--primary` | #16a34a | #22c55e | Primary actions, buttons, links |
| `--primary-foreground` | #ffffff | #000000 | Text on primary color |
| `--secondary` | #1a2b50 | #000000 | Headers, badges |
| `--secondary-foreground` | #ffffff | #e5e5e5 | Text on secondary |
| `--background` | #ffffff | #262626 | Main app background |
| `--foreground` | #4a4a4a | #e5e5e5 | Primary text color |
| `--card` | #ffffff | #2a2a2a | Card backgrounds |
| `--border` | rgba(0,0,0,0.1) | rgba(34,197,94,0.2) | Borders |
| `--input-background` | #f9fafb | #3a3a3a | Input field backgrounds |

**Semantic Colors:**

| Token | Light | Dark | Usage |
|-------|-------|------|-------|
| `--destructive` | #d93025 | #ef4444 | Errors, delete actions |
| `--success` | #34a853 | #22c55e | Success messages |
| `--warning` | #f9ab00 | #fbbf24 | Warnings |
| `--info` | #4285f4 | #60a5fa | Info messages |

**Source:** [THEME_VISUAL_GUIDE.md, design-tokens.md]

---

### Typography

**Font Family:**
```css
font-family: 'Inter', sans-serif;
@import url('https://fonts.googleapis.com/css2?family=Inter:wght@400;500;600&display=swap');
```

**Font Scale:**

| Token | Size | Usage |
|-------|------|-------|
| `--text-xs` | 0.75rem (12px) | Small labels, captions |
| `--text-sm` | 0.875rem (14px) | Secondary text |
| `--text-base` | 1rem (16px) | Body text (base) |
| `--text-lg` | 1.125rem (18px) | Subheadings |
| `--text-xl` | 1.25rem (20px) | Section headings |
| `--text-2xl` | 1.5rem (24px) | Page titles |
| `--text-3xl` | 1.875rem (30px) | Large headings |

**Font Weights:**
- `400` - Normal body text
- `500` - Medium (labels, UI elements)
- `600` - Semibold (headings, emphasis)

**Source:** [THEME_VISUAL_GUIDE.md]

---

### Spacing Scale

| Token | Value | Pixels |
|-------|-------|--------|
| `--spacing-1` | 0.25rem | 4px |
| `--spacing-2` | 0.5rem | 8px |
| `--spacing-3` | 0.75rem | 12px |
| `--spacing-4` | 1rem | 16px |
| `--spacing-5` | 1.25rem | 20px |
| `--spacing-6` | 1.5rem | 24px |
| `--spacing-8` | 2rem | 32px |
| `--spacing-10` | 2.5rem | 40px |
| `--spacing-12` | 3rem | 48px |

**Source:** [THEME_VISUAL_GUIDE.md]

---

### Border Radius

| Token | Value |
|-------|-------|
| `--radius` | 0.625rem (10px) |

Applied to: Buttons, cards, inputs, modals

---

### Touch Targets (Mobile)

- **Minimum:** 44px × 44px (iOS/Android guidelines)
- **Recommended:** 48px for primary actions
- **Spacing:** Minimum 8px between targets

**Source:** [ACCESSIBILITY_USAGE.md, RESPONSIVE_VISUAL_GUIDE.md]

---

## 8. Data + Domain Model

### Core Entities

#### User (Therapist)

```csharp
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public string FullName { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string PinHash { get; set; } = ""; // 4-digit PIN
    public string Role { get; set; } = "Therapist"; // Therapist | PTA | Admin | Staff
    public string LicenseType { get; set; } = "PT"; // PT | PTA | DPT
    public string LicenseNumber { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public bool TwoFactorEnabled { get; set; } = false;
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
}
```

**Source:** [BACKEND_SQLITE_EF_INTEGRATION.md]

---

#### Patient

```csharp
public class Patient
{
    public int Id { get; set; }
    public string MRN { get; set; } = ""; // Medical Record Number (unique)
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateTime DateOfBirth { get; set; }
    public string Gender { get; set; } = ""; // M | F | Other
    public string Email { get; set; } = "";
    public string Phone { get; set; } = "";
    public string Address { get; set; } = "";
    public string City { get; set; } = "";
    public string State { get; set; } = "";
    public string ZipCode { get; set; } = "";
    
    // Clinical
    public string PrimaryDiagnosis { get; set; } = "";
    public string DiagnosisCode { get; set; } = ""; // ICD-10
    public bool IsActive { get; set; } = true;
    
    // Relationships
    public ICollection<PatientInsurance> Insurances { get; set; } = new List<PatientInsurance>();
    public ICollection<SOAPNote> Notes { get; set; } = new List<SOAPNote>();
    public ICollection<PriorAuthorization> Authorizations { get; set; } = new List<PriorAuthorization>();
}
```

**Source:** [BACKEND_SQLITE_EF_INTEGRATION.md]

---

#### SOAPNote

```csharp
public class SOAPNote
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int TherapistId { get; set; }
    public User Therapist { get; set; } = null!;
    
    public DateTime DateOfService { get; set; }
    public string VisitType { get; set; } = "Initial Evaluation"; // Initial Evaluation | Follow-Up | Re-Evaluation | Discharge
    public string Location { get; set; } = "";
    public int DurationMinutes { get; set; } = 45;
    
    // SOAP Content
    public string Subjective { get; set; } = "";
    public string Objective { get; set; } = "";
    public string Assessment { get; set; } = "";
    public string Plan { get; set; } = "";
    
    // Billing
    public string BillingCodes { get; set; } = ""; // Comma-separated CPT codes
    
    // Status
    public string Status { get; set; } = "Draft"; // Draft | Completed | Signed | Locked
    public DateTime? SignedAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ModifiedAt { get; set; }
}
```

**Source:** [BACKEND_SQLITE_EF_INTEGRATION.md, NOTES_VISUAL_GUIDE.md]

---

#### PatientInsurance

```csharp
public class PatientInsurance
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    public int InsuranceCompanyId { get; set; }
    public InsuranceCompany InsuranceCompany { get; set; } = null!;
    
    public string PolicyNumber { get; set; } = "";
    public string GroupNumber { get; set; } = "";
    public string SubscriberName { get; set; } = "";
    public string Relationship { get; set; } = "Self"; // Self | Spouse | Child | Other
    public bool IsPrimary { get; set; } = true;
    public DateTime EffectiveDate { get; set; }
    public DateTime? TerminationDate { get; set; }
}
```

**Source:** [BACKEND_SQLITE_EF_INTEGRATION.md]

---

#### PriorAuthorization

```csharp
public class PriorAuthorization
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public Patient Patient { get; set; } = null!;
    
    public string AuthNumber { get; set; } = "";
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int ApprovedVisits { get; set; }
    public int UsedVisits { get; set; }
    public string Status { get; set; } = "Active"; // Active | Expired | Exhausted
}
```

**Source:** [BACKEND_SQLITE_EF_INTEGRATION.md]

---

### Entity Relationships

```
User (1) ──< SOAPNote >── (M) Patient
                            │
                            ├──< PatientInsurance >──(M) InsuranceCompany
                            │
                            └──< PriorAuthorization
```

---

## 9. API / Integration Contracts

### Authentication Endpoints

**Login (POST /auth/token)**

Request:
```json
{
  "username": "jane.smith@ptdoc.com",
  "password": "SecurePass123!"
}
```

Response (200 OK):
```json
{
  "accessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "refreshToken": "a4b8c3d2e1f0...",
  "expiresIn": 900,
  "tokenType": "Bearer"
}
```

**Source:** [docs/SECURITY.md, src/PTDoc.Api/Auth/AuthEndpoints.cs]

---

**Refresh Token (POST /auth/refresh)**

Request:
```json
{
  "refreshToken": "a4b8c3d2e1f0..."
}
```

Response (200 OK): Same as login response

---

### Patient Endpoints

**List Patients (GET /api/patients)**

Query Parameters:
- `search` (string, optional) - Search by name, MRN, diagnosis
- `isActive` (bool, optional) - Filter by active status
- `page` (int, default: 1)
- `pageSize` (int, default: 20)

Response (200 OK):
```json
{
  "data": [
    {
      "id": 1,
      "mrn": "12345",
      "firstName": "Sarah",
      "lastName": "Johnson",
      "dateOfBirth": "1975-01-15",
      "primaryDiagnosis": "Right Shoulder Pain",
      "diagnosisCode": "M25.511",
      "isActive": true,
      "insuranceStatus": "Active",
      "authExpiration": "2026-03-15"
    }
  ],
  "totalCount": 57,
  "page": 1,
  "pageSize": 20
}
```

**Source:** [PATIENTS_VISUAL_GUIDE.md]

---

**Get Patient Detail (GET /api/patients/{id})**

Response (200 OK):
```json
{
  "id": 1,
  "mrn": "12345",
  "firstName": "Sarah",
  "lastName": "Johnson",
  "dateOfBirth": "1975-01-15",
  "gender": "F",
  "email": "sarah.j@email.com",
  "phone": "(555) 123-4567",
  "address": "123 Main St",
  "city": "Springfield",
  "state": "IL",
  "zipCode": "62701",
  "primaryDiagnosis": "Right Shoulder Pain",
  "diagnosisCode": "M25.511",
  "isActive": true,
  "insurances": [
    {
      "insuranceCompany": "Blue Cross Blue Shield",
      "policyNumber": "ABC123456789",
      "isPrimary": true,
      "effectiveDate": "2025-01-01",
      "terminationDate": null
    }
  ],
  "authorizations": [
    {
      "authNumber": "AUTH-2026-001",
      "startDate": "2026-01-15",
      "endDate": "2026-03-15",
      "approvedVisits": 12,
      "usedVisits": 3,
      "status": "Active"
    }
  ]
}
```

---

### SOAP Note Endpoints

**Create Note (POST /api/notes)**

Request:
```json
{
  "patientId": 1,
  "dateOfService": "2026-01-28",
  "visitType": "Initial Evaluation",
  "location": "Clinic A - Room 2",
  "durationMinutes": 45,
  "subjective": "Patient reports...",
  "objective": "Observation and measurements...",
  "assessment": "Clinical interpretation...",
  "plan": "Treatment plan...",
  "billingCodes": "97161,97110"
}
```

Response (201 Created):
```json
{
  "id": 42,
  "status": "Draft",
  "createdAt": "2026-01-28T14:30:00Z"
}
```

**Source:** [NOTES_VISUAL_GUIDE.md]

---

### Error Responses

**Standard Error Format:**
```json
{
  "error": "ValidationError",
  "message": "Patient MRN is required",
  "details": {
    "field": "mrn",
    "code": "REQUIRED_FIELD"
  }
}
```

**HTTP Status Codes:**
- `400` - Bad Request (validation error)
- `401` - Unauthorized (missing/invalid token)
- `403` - Forbidden (insufficient permissions)
- `404` - Not Found
- `409` - Conflict (duplicate MRN, etc.)
- `412` - Precondition Failed (ETag mismatch)
- `500` - Internal Server Error

**Source:** [docs/RUNTIME_TARGETS.md]

---

## 10. Key UX Rules

### Validation Patterns

**Real-Time Validation:**
- Email: RFC 5322 format validation
- Phone: US format (XXX) XXX-XXXX
- Date of Birth: Must be in past, reasonable age range (0-120 years)
- MRN: Alphanumeric, 5-10 characters, unique
- Required fields: Show asterisk (*), validate on blur

**Error Display:**
- Inline errors below field (red text, red border)
- Form-level errors at top in alert banner
- Screen reader announcement for new errors

**Source:** [ACCESSIBILITY_USAGE.md, SIGNUP_LOGIN_VISUAL_GUIDE.md]

---

### Empty States

**Patient List (No Results):**
```
┌────────────────────────────────┐
│   🔍                           │
│   No patients found            │
│   Try adjusting your filters   │
│   [Clear Filters]              │
└────────────────────────────────┘
```

**SOAP Notes (No Active Notes):**
```
┌────────────────────────────────┐
│   📝                           │
│   No active notes              │
│   Create your first note       │
│   [➕ New Note]                │
└────────────────────────────────┘
```

**Source:** All visual guides

---

### Loading States

**Skeleton Loaders:**
- Use for initial data fetch (patient list, dashboard metrics)
- Preserve layout dimensions
- Subtle animation (pulse or shimmer)

**Spinner:**
- Use for button actions (saving, submitting)
- Modal overlays during async operations

**Progress Indicators:**
- Multi-step forms (intake)
- File uploads
- Report generation

**Source:** [BLAZOR_COMPONENT_SHOWCASE.md]

---

### Accessibility Requirements

**Keyboard Navigation:**
- All interactive elements accessible via Tab/Shift+Tab
- Enter/Space activate buttons
- Escape closes modals/dropdowns
- Arrow keys for select/radio groups

**Screen Reader Support:**
- Semantic HTML (header, nav, main, footer)
- ARIA labels for icon-only buttons
- Live regions for dynamic content (aria-live="polite")
- Form field associations (label[for] + input[id])

**Focus Management:**
- Visible focus indicators (2px emerald outline)
- Focus trap in modals
- Restore focus after modal close

**Color Contrast:**
- All text meets WCAG AA (4.5:1 minimum)
- Interactive elements meet 3:1 minimum

**Source:** [docs/ACCESSIBILITY_USAGE.md]

---

## 11. Implementation Notes

### Routing (Blazor)

**Router Configuration:**
```razor
<Router AppAssembly="@typeof(App).Assembly"
        AdditionalAssemblies="@(new[] { typeof(PTDoc.UI.Components.Routes).Assembly })">
    <Found Context="routeData">
        <AuthorizeRouteView RouteData="@routeData" DefaultLayout="@typeof(MainLayout)">
            <NotAuthorized>
                <RedirectToLogin />
            </NotAuthorized>
        </AuthorizeRouteView>
    </Found>
    <NotFound>
        <LayoutView Layout="@typeof(MainLayout)">
            <p>Page not found</p>
        </LayoutView>
    </NotFound>
</Router>
```

**Source:** [docs/Blazor-Context.md]

---

### State Management

**Options:**

1. **Scoped Services (Recommended):**
   - One instance per circuit (Blazor Server) or app session (WASM/MAUI)
   - Inject via `@inject` directive
   - Use for user context, selected patient, theme state

2. **Cascading Parameters:**
   - For deeply nested components
   - Theme provider, user authentication state

3. **Singleton Services:**
   - Safe for MAUI (single user)
   - Risky for Blazor Server (multi-user) - avoid for user-specific data

**Source:** [docs/Blazor-Context.md]

---

### Database Migrations

**Create Migration:**
```bash
EF_PROVIDER=sqlite dotnet ef migrations add MigrationName \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api
```

**Apply Migrations:**
```bash
EF_PROVIDER=sqlite dotnet ef database update \
  -p src/PTDoc.Infrastructure \
  -s src/PTDoc.Api
```

**Database Path:**
1. `PFP_DB_PATH` environment variable (highest priority)
2. `ConnectionStrings:DefaultConnection` in appsettings.json
3. Fallback: `Data Source=PTDoc.db`

**Source:** [docs/EF_MIGRATIONS.md]

---

### Performance Considerations

**Blazor Component Optimization:**
- Use `@key` directive for list items (virtualization)
- Avoid excessive `StateHasChanged()` calls
- Use `OnAfterRenderAsync(firstRender)` for JS interop
- Implement `ShouldRender()` for expensive components (rare)

**MAUI Considerations:**
- Keep WebView DOM lightweight (max ~1000 elements)
- Use `<Virtualize>` for long lists
- Lazy-load images
- Test on low-end Android devices (min API 21)

**Source:** [docs/Blazor-Context.md]

---

### Testing (Planned)

**Unit Tests:**
- xUnit for service layer
- bUnit for Blazor components

**Integration Tests:**
- WebApplicationFactory for API endpoints
- In-memory SQLite for database tests

**E2E Tests:**
- Playwright for browser automation

**Source:** [docs/CI.md]

---

## 12. Figma ↔ Code Mapping Notes

**Legend:**
- **Mapped:** Confirmed Figma node → file path association with explicit cross-reference
- **Unmapped:** Inferred from naming conventions or pending Stage 2 detailed inventory

### Confirmed Mappings

| Figma Screen | .tsx File Path | Blazor Target |
|--------------|----------------|---------------|
| Login | `pages/Login.tsx` | `/Pages/Login.razor` |
| Signup | `pages/SignUp.tsx` | `/Pages/SignUp.razor` |
| Dashboard | `pages/Dashboard.tsx` | `/Pages/Dashboard.razor` |
| Patients | `pages/Patients.tsx` | `/Pages/Patients.razor` |
| Patient Detail | `pages/PatientDetail.tsx` | `/Pages/PatientDetail.razor` |
| Intake | `pages/Intake.tsx` | `/Pages/Intake.razor` |
| SOAP Notes | `pages/Notes.tsx` | `/Pages/Notes.razor` |
| Progress Tracker | `pages/ProgressTracker.tsx` | `/Pages/ProgressTracker.razor` |
| Reports | `pages/Reports.tsx` | `/Pages/Reports.razor` |
| Export Center | `pages/ExportCenter.tsx` | `/Pages/ExportCenter.razor` |
| Settings | `pages/Settings.tsx` | `/Pages/Settings.razor` |
| Role Management | `pages/RoleManagement.tsx` | `/Pages/RoleManagement.razor` |

### Component Mappings

| Figma Component | .tsx File | Blazor Component |
|-----------------|-----------|------------------|
| Button | `components/ui/button.tsx` | `PTDocButton.razor` |
| Card | `components/ui/card.tsx` | `PTDocCard.razor` |
| Input | `components/ui/input.tsx` | `PTDocInput.razor` |
| Badge | `components/ui/badge.tsx` | `PTDocBadge.razor` |
| Alert | `components/ui/alert.tsx` | `PTDocAlert.razor` |
| Modal | `components/ui/modal.tsx` | `PTDocModal.razor` |
| Table | `components/ui/table.tsx` | `PTDocTable.razor` |
| Metric Card | `components/dashboard/MetricCard.tsx` | `PTDocMetricCard.razor` |

### Navigation Discovery Process

When implementing a new screen:
1. Open Figma Desktop → PTDoc Prototype v5
2. Use Map → Pages navigation to find screen
3. Inspect Layers panel for component hierarchy
4. Note component names and props in Figma Design panel
5. Cross-reference with .tsx file structure
6. Implement in Blazor using equivalent patterns

---

## 13. Open Questions / TODOs

### Clarifications Needed

1. **AI-Powered Suggestions:**
   - SOAP note AI assist scope?
   - Integration with external AI API or in-house model?
   - Privacy/HIPAA considerations for patient data in AI prompts?

2. **Real-Time Features:**
   - Is real-time collaboration (multiple users editing same note) required?
   - SignalR for live dashboard updates (appointment notifications)?

3. **Offline Mode (MAUI):**
   - Full offline support or read-only?
   - Conflict resolution strategy for sync?
   - Background sync frequency?

4. **Report Export Formats:**
   - PDF only or also Excel/CSV?
   - Template customization by clinic?

5. **Mobile-Specific Features:**
   - Camera integration for wound photos?
   - Voice-to-text for SOAP notes?
   - Biometric authentication (fingerprint/face)?

---

## 14. Conflicts & Clarifications Needed

### Conflict 1: Authentication Flow Discrepancy

**Description:** Web uses cookie-based auth (no JWT), while MAUI uses JWT tokens.

**Conflicting Statements:**
- [docs/SECURITY.md] - "Web Application: Cookie authentication with 15min/8hr timeouts"
- [src/PTDoc.Api/Auth/AuthEndpoints.cs] - Provides JWT token endpoint for login
- [SIGNUP_LOGIN_VISUAL_GUIDE.md] - Shows unified login form

**Question:** Should the web login also obtain JWT tokens (for API calls) but store them in cookies? Or are cookies purely for session management separate from API authorization?

**Source Locations:**
- docs/SECURITY.md (lines 5-30)
- src/PTDoc.Api/Program.cs (JWT configuration)
- src/PTDoc.Web/Program.cs (Cookie authentication)

---

### Conflict 2: Designer Role vs Therapist Role

**Description:** Visual guides reference "designer" role in some places, unclear if this is a real user role or prototype artifact.

**Conflicting Statements:**
- [ROLE_MANAGEMENT_VISUAL_GUIDE.md] - Shows roles: Physical Therapist, PTA, Admin, Staff (no designer)
- Some Figma screens show "designer" in dropdowns

**Question:** Is "designer" a placeholder or an actual user role? Should it be removed from Blazor implementation?

**Source Locations:**
- ROLE_MANAGEMENT_VISUAL_GUIDE.md
- Figma prototype (needs verification via desktop map)

---

### Conflict 3: Patient MRN Generation

**Description:** Unclear if MRN (Medical Record Number) is auto-generated or user-entered during intake.

**Conflicting Statements:**
- [INTAKE_VISUAL_GUIDE.md] - Shows MRN field but doesn't specify if editable
- [BACKEND_SQLITE_EF_INTEGRATION.md] - Shows MRN as string (suggests manual entry)
- Database unique constraint implies system-generated might be safer

**Question:** Should MRN be auto-generated (UUID or sequential) or manually entered by staff? If manual, what validation rules?

**Source Locations:**
- docs/visual-guides/INTAKE_VISUAL_GUIDE.md
- Models/Patient.cs (MRN property)

---

## 15. Appendix A: Source Map

This consolidated document synthesizes information from **46 source documents**.

**Document Inventory Status:**
- **12 documents** currently exist in `/docs` (PTDoc .NET implementation guides) - remain active as complementary references
- **34 documents** were ingested directly from attachments (React prototype visual/migration guides) - content synthesized into this consolidated doc without creating separate files

**Rationale:** The 34 visual/migration guide files were processed and their content integrated directly into relevant sections (Design System, Component Catalog, UI Architecture, etc.) rather than creating duplicate standalone files. This keeps the repository lean while preserving all critical information. The 12 existing .NET implementation docs remain active because they provide complementary technical details that this consolidated doc references but does not duplicate.

### Existing docs/**/*.md Files (Complementary References)

1. **ACCESSIBILITY_USAGE.md** - WCAG 2.1 AA compliance guide, screen reader patterns, keyboard navigation, focus management for Blazor components
2. **ARCHITECTURE.md** - Clean Architecture layers, project structure, dependency rules, cross-cutting concerns (logging, validation, security)
3. **Blazor-Context.md** - Blazor component lifecycle (OnInitialized, OnParametersSet, OnAfterRender), parameter rules, state management, rendering flow, common pitfalls, and MAUI Hybrid architecture (BlazorWebView, native↔Blazor communication, JavaScript interop, platform differences)
5. **BUILD.md** - Build instructions for all platforms (Web, MAUI, iOS, Android, macOS), prerequisites, platform-specific commands
6. **CI.md** - CI/CD pipeline stages (commit→build→test→deploy), enforced SDK version, build standards, future GitHub Actions workflows
7. **DEVELOPMENT.md** - Development workflow, daily routines, setup scripts (PTDoc-Foundry.sh), environment verification, developer diagnostics configuration (PTDOC_DEVELOPER_MODE), platform-specific diagnostic setup
8. **EF_MIGRATIONS.md** - Entity Framework migrations commands, database schema management, provider configuration (SQLite)
9. **RUNTIME_TARGETS.md** - Platform differences (Web vs MAUI), data access patterns, authentication models, concurrency control (ETags)
10. **SECURITY.md** - Authentication policies, session timeouts (15min/8hr), JWT configuration, HIPAA compliance requirements
11. Ingested Visual Guide Files (Content Synthesized, Not Saved Separately)

12. **THEME_VISUAL_GUIDE.md** - Color system (emerald green theme), typography (Inter font), spacing scale, component visual specs
    - *Note: Standalone file exists at `docs/design-system/THEME_VISUAL_GUIDE.md` for quick reference*

13. **THEME_VISUAL_GUIDE.md** - Color system (emerald green theme), typography (Inter font), spacing scale, component visual specs
14. **SIGNUP_LOGIN_VISUAL_GUIDE.md** - Authentication screens (login, signup, 2FA), validation states, responsive layouts
15. **SIGNUP_VISUAL_GUIDE.md** - Sign-up form detailed specs, field validation, error states
16. **SETTINGS_VISUAL_GUIDE.md** - Settings page layout, profile management, appearance/theme toggle, clinic config
17. **ROLE_MANAGEMENT_VISUAL_GUIDE.md** - User/role management interface, permission assignment, 2FA enforcement
18. **RESPONSIVE_VISUAL_GUIDE.md** - Breakpoint behaviors (mobile/tablet/desktop), touch targets, responsive patterns
19. **REPORTS_VISUAL_GUIDE.md** - Reports dashboard, KPI cards, chart specs, export options
20. **PROGRESS_TRACKER_VISUAL_GUIDE.md** - Patient progress visualization, goal tracking, measurement trends
21. **PATIENTS_VISUAL_GUIDE.md** - Patient list, search/filter UI, patient card layout, detail view
22. **NOTES_VISUAL_GUIDE.md** - SOAP note editor, AI suggestions, billing codes, note locking
23. **INTAKE_VISUAL_GUIDE.md** - Multi-step patient intake form, validation per step, progress indicator
24. **EXPORT_CENTER_VISUAL_GUIDE.md** - Custom export interface, date range selection, format options
25. **DASHBOARD_VISUAL_GUIDE.md** - Dashboard layout, metric cards, appointment list, quick actions
26. **PTDOC_DESIGN_SYSTEM.md** - Comprehensive design system (colors, typography, components, animations)
27. **design-tokens.md** - Complete token reference (CSS variables), semantic naming, usage guidelines
28. **BLAZOR_MIGRATION_GUIDE.md** - React→Blazor conversion patterns, CSS export, component templates
29. **BLAZOR_IMPLEMENTATION_GUIDE.md** - Step-by-step Blazor implementation, project setup, EF Core integration
30. **BLAZOR_HANDOFF_INDEX.md** - Navigation guide for all Blazor documentation
31. **BLAZOR_DESIGN_SYSTEM_EXPORT.md** - Complete design system for Blazor (tokens, components, theme switching)
32. **BLAZOR_COMPONENTS_ADVANCED.md** - Advanced components (Alert, Modal, Table, Sidebar, SOAP editor)
33. **BLAZOR_COMPONENT_SHOWCASE.md** - Visual component library reference with ASCII diagrams
34. **BACKEND_SQLITE_EF_INTEGRATION.md** - Database models, DbContext config, data services, migrations

---

## Stage 1 Complete - Next Steps
Stage 1 Completed:**
✅ Consolidated context document created  
✅ Design system tokens documented  
✅ Component catalog established  
✅ Architecture and runtime patterns captured  
✅ Data model and API contracts defined  
✅ Figma reference and mapping process documented

**Stage 2 Completed:**
✅ Visual guides ingested directly (content synthesized into relevant sections)  
✅ Migration guides integrated (React→Blazor patterns documented)  
✅ .github/copilot-instructions.md finalized with citation requirements  
✅ Archive strategy implemented (existing docs identified as complementary, no archiving needed)  
✅ .gitignore updated to exclude future archive files

**Directory Structure:**
- `docs/context/` - This consolidated document (single source of truth)
- `docs/design-system/` - THEME_VISUAL_GUIDE.md (standalone reference)
- `docs/visual-guides/` - Reserved for future screen-specific addendums
- `docs/migration/` - Reserved for future conversion pattern details
- `docs/_archive/` - Reserved for superseded documentation (currently empty)

**Note:** The 12 existing .NET implementation docs in `/docs` remain active as complementary references. They provide technical implementation details that this consolidated doc references but does not duplicate.

**Open Conflicts:** 3 clarifications needed (see Section 14)

---

**End of Consolidated Context Document - Stage 2 Complete
**End of Stage 1 Consolidated Context Document**
