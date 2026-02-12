# PTDoc (PFPT) – Blazor Component & Page Mapping

_Figma → Razor Component Specification_

Derived From: PFPT Master FSD + Backend TDD  
Audience: Blazor Developers, UI Engineers, QA  
Framework: .NET 8, Blazor (MAUI Hybrid + Web)

---

## 1. Purpose

This document maps each Figma screen and workflow to concrete Blazor Razor components, defining routing, component responsibilities, parameters, state, and service dependencies. It prevents UI drift and ensures design-to-code parity.

---

## 2. Application Shell

### 2.1 Layout Components

- `MainLayout.razor` — Global layout wrapper  
- `SidebarNav.razor` — Desktop navigation  
- `BottomNav.razor` — Mobile navigation  
- `TopBar.razor` — User menu, offline indicator  
- `PatientContextBanner.razor` — Persistent patient header  

---

## 3. Routing Overview

| Route                       | Page Component           | Notes              |
|----------------------------|--------------------------|--------------------|
| `/login`                   | `LoginPage.razor`        | PIN-based auth     |
| `/`                        | `DashboardPage.razor`    | Default landing    |
| `/patients`                | `PatientListPage.razor`  | Search/filter      |
| `/patient/{id}`            | `PatientOverviewPage.razor` | Chart hub      |
| `/intake/{id}`             | `IntakeWizardPage.razor` | Standalone         |
| `/patient/{id}/note/{noteId}` | `NoteWorkspacePage.razor` | Core workspace |
| `/settings`                | `SettingsPage.razor`     | Admin only         |

---

## 4. Dashboard

### 4.1 `DashboardPage.razor`

**Components Used**

- `MetricCard.razor`  
- `RecentActivityList.razor`  
- `QuickActionsPanel.razor`  

**State**

- `TodayAppointmentsCount`  
- `OpenDraftCount`  
- `POCAlertsCount`  

**Services**

- `IDashboardService`  

---

## 5. Patient Management

### 5.1 `PatientListPage.razor`

**Components**

- `PatientSearchBar.razor`  
- `PatientListItem.razor`  

**Services**

- `IPatientService`  

### 5.2 `PatientOverviewPage.razor`

**Tabs**

- Summary  
- Notes  
- Goals  
- Outcomes  
- Documents  

**Child Components**

- `NoteTimeline.razor`  
- `GoalList.razor`  
- `OutcomeTrendChart.razor`  

---

## 6. Intake Flow

### 6.1 `IntakeWizardPage.razor`

**Steps**

- `DemographicsStep.razor`  
- `PainMapStep.razor`  
- `RegionalDetailsStep.razor`  
- `ConsentStep.razor`  

**State Handling**

- Wizard state persisted locally  
- Submit locks `IntakeResponse`  

**Services**

- `IIntakeService`  

---

## 7. SOAP Note Workspace (Core)

### 7.1 `NoteWorkspacePage.razor`

**Tabs**

- Subjective  
- Objective  
- Assessment  
- Plan  
- Billing  

**Guards**

- Unsaved changes lock  
- Role-based button rendering  

### 7.2 `SubjectiveTab.razor`

**Inputs**

- Chief Complaint  
- Pain Scale  
- Functional Status  

**Carry-Forward Logic**

- Eval pulls Intake  
- Daily/PN pulls last signed note  

### 7.3 `ObjectiveTab.razor`

**Child Components**

- `BodyPartSelector.razor`  
- `RomMmtGrid.razor`  
- `OutcomeMeasurePanel.razor`  

### 7.4 `AssessmentTab.razor`

**Components**

- `AiGenerateButton.razor`  
- `EditableNarrativeBox.razor`  

**Flow**

Generate → Review → Accept  

### 7.5 `PlanTab.razor`

**Components**

- `GoalEditor.razor`  
- `InterventionChecklist.razor`  

### 7.6 `BillingTab.razor`

**Components**

- `CptPicker.razor`  
- `EightMinuteRuleValidator.razor`  

---

## 8. Goals & Outcomes

### 8.1 `GoalEditor.razor`

- SMART templates  
- Status toggles  

### 8.2 `OutcomeTrendChart.razor`

- Longitudinal graphs  

---

## 9. PDF & Export

### 9.1 `ExportButton.razor`

- Visible only when note is finalized  
- Triggers PDF generation  

---

## 10. Settings & Admin

### 10.1 `SettingsPage.razor`

**Sections**

- Roles & Permissions  
- Branding  
- Retention Rules  
- Audit Logs  

---

## 11. Shared UI Components

- `FormSection.razor` — Consistent spacing  
- `ReadonlyField.razor` — Locked data  
- `RoleGate.razor` — Permission guard  
- `OfflineBadge.razor` — Connectivity state  

---

## 12. Validation & QA Hooks

- Each page exposes `data-testid` attributes  
- Validation errors surfaced inline  

---

This document is binding for frontend implementation and must remain aligned with the Master FSD and Backend TDD.
