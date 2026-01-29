# Accessibility Utilities Usage Guide

## Overview

PTDoc is committed to WCAG 2.1 AA compliance for inclusive healthcare technology. This guide documents accessibility utilities and best practices for Blazor components.

## Healthcare Accessibility Requirements

Physical therapy applications must be accessible to:
- **Clinicians** with visual impairments (screen reader support)
- **Patients** using assistive technologies
- **Administrative staff** with motor impairments (keyboard navigation)
- **Users** in compliance with ADA and Section 508 requirements

## Core Accessibility Principles

### 1. Screen Reader Announcements

Announce dynamic content changes to assistive technology users.

**When to use:**
- Form submission confirmations
- Data save/update notifications
- Error messages
- Dynamic content updates
- Loading state changes

**Implementation:**
```csharp
@inject IJSRuntime JS

@code {
    private async Task AnnounceToScreenReader(string message, bool assertive = false)
    {
        var priority = assertive ? "assertive" : "polite";
        await JS.InvokeVoidAsync("announceToScreenReader", message, priority);
    }

    private async Task SavePatient()
    {
        // Save logic...
        await AnnounceToScreenReader("Patient record saved successfully");
    }

    private async Task HandleError()
    {
        // Error handling...
        await AnnounceToScreenReader("Error: Invalid date selected", assertive: true);
    }
}
```

**JavaScript implementation (wwwroot/js/accessibility.js):**
```javascript
window.announceToScreenReader = (message, priority) => {
    const announcer = document.getElementById('aria-live-announcer') 
        || createAnnouncer();
    announcer.setAttribute('aria-live', priority);
    announcer.textContent = message;
    
    // Clear after announcement
    setTimeout(() => { announcer.textContent = ''; }, 1000);
};

function createAnnouncer() {
    const div = document.createElement('div');
    div.id = 'aria-live-announcer';
    div.setAttribute('aria-live', 'polite');
    div.setAttribute('aria-atomic', 'true');
    div.className = 'sr-only'; // Visually hidden but screen-reader accessible
    document.body.appendChild(div);
    return div;
}
```

### 2. Focus Management

#### Trap Focus in Modals/Dialogs

Prevent keyboard users from tabbing outside modal dialogs.

**Use cases:**
- Patient information modals
- Confirmation dialogs
- Forms in overlays
- Critical alerts requiring user action

**Implementation:**
```razor
<div id="patient-modal" 
     role="dialog" 
     aria-modal="true"
     aria-labelledby="modal-title"
     @ref="modalElement">
    
    <h2 id="modal-title">Patient Information</h2>
    
    <!-- Focusable content -->
    <input type="text" aria-label="Patient Name" />
    <button @onclick="Save">Save</button>
    <button @onclick="CloseModal">Cancel</button>
</div>

@code {
    private ElementReference modalElement;
    private IJSObjectReference? _focusTrap;

    private async Task OpenModal()
    {
        StateHasChanged();
        await Task.Delay(100); // Ensure DOM is ready
        
        // Trap focus
        _focusTrap = await JS.InvokeAsync<IJSObjectReference>(
            "trapFocus", modalElement);
    }

    private async Task CloseModal()
    {
        // Restore focus
        if (_focusTrap != null)
        {
            await _focusTrap.InvokeVoidAsync("release");
            await _focusTrap.DisposeAsync();
            _focusTrap = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_focusTrap != null)
        {
            await _focusTrap.DisposeAsync();
        }
    }
}
```

#### Restore Focus After Actions

Return focus to triggering element after closing dialogs.

```csharp
private ElementReference _triggerButton;

private async Task OpenDialog()
{
    // Store current focus
    await JS.InvokeVoidAsync("storeFocus", _triggerButton);
    // Open dialog...
}

private async Task CloseDialog()
{
    // Close dialog...
    // Restore focus
    await JS.InvokeVoidAsync("restoreFocus");
}
```

### 3. Keyboard Navigation

#### Skip Links

Allow keyboard users to bypass repetitive navigation.

```razor
<!-- Place at top of layout -->
<a href="#main-content" class="skip-link">Skip to main content</a>

<nav>
    <!-- Navigation items -->
</nav>

<main id="main-content" tabindex="-1">
    @Body
</main>
```

**CSS:**
```css
.skip-link {
    position: absolute;
    top: -40px;
    left: 0;
    background: #22c55e;
    color: #000;
    padding: 8px;
    text-decoration: none;
    z-index: 100;
}

.skip-link:focus {
    top: 0;
}
```

#### Keyboard Event Handling

```razor
<div tabindex="0" 
     @onkeydown="HandleKeyDown"
     role="button"
     aria-label="Add Patient">
    <i class="icon-plus"></i> Add Patient
</div>

@code {
    private void HandleKeyDown(KeyboardEventArgs e)
    {
        // Support Enter and Space for activation (standard for buttons)
        if (e.Key == "Enter" || e.Key == " ")
        {
            e.PreventDefault();
            AddPatient();
        }
        
        // Support Escape to close
        if (e.Key == "Escape")
        {
            Close();
        }
    }
}
```

### 4. ARIA Labels and Descriptions

#### Form Fields

```razor
<!-- Explicit label association -->
<label for="patient-dob">Date of Birth</label>
<input id="patient-dob" 
       type="date" 
       aria-required="true"
       aria-describedby="dob-hint" />
<span id="dob-hint" class="hint-text">Format: MM/DD/YYYY</span>

<!-- aria-label for icon-only buttons -->
<button aria-label="Delete patient record">
    <i class="icon-trash"></i>
</button>

<!-- aria-labelledby for complex labels -->
<div role="group" aria-labelledby="pain-scale-label">
    <span id="pain-scale-label">Pain Level (0-10)</span>
    <input type="range" min="0" max="10" aria-valuetext="@GetPainDescription()" />
</div>
```

#### Dynamic Content

```razor
<div aria-live="polite" aria-atomic="true">
    @if (isLoading)
    {
        <p>Loading patient data...</p>
    }
    else if (patients.Any())
    {
        <p>Loaded @patients.Count patients</p>
    }
</div>
```

### 5. Color Contrast

PTDoc uses WCAG AA compliant color ratios:

**Text on Dark Background:**
```css
/* Primary text: 4.5:1 ratio minimum */
color: #e5e5e5;  /* On #000000 background */

/* Secondary text: 4.5:1 ratio minimum */
color: rgba(229, 229, 229, 0.7);

/* Success indicators */
background: #22c55e;
color: #000000;  /* 8.6:1 ratio */
```

**Interactive Elements:**
```css
/* Focus indicators: 3:1 ratio minimum */
outline: 2px solid #22c55e;
outline-offset: 2px;
```

### 6. Accessible Components

#### Loading Indicators

```razor
<div role="status" aria-live="polite" aria-busy="@isLoading.ToString().ToLower()">
    @if (isLoading)
    {
        <div class="spinner" aria-hidden="true"></div>
        <span class="sr-only">Loading patient data...</span>
    }
</div>
```

#### Error Messages

```razor
<div role="alert" 
     aria-live="assertive"
     class="error-message">
    <i class="icon-alert" aria-hidden="true"></i>
    <span>@errorMessage</span>
</div>
```

#### Tables

```razor
<table aria-label="Patient appointment schedule">
    <caption class="sr-only">Upcoming appointments for Dr. Johnson</caption>
    <thead>
        <tr>
            <th scope="col">Patient Name</th>
            <th scope="col">Date</th>
            <th scope="col">Time</th>
            <th scope="col">Actions</th>
        </tr>
    </thead>
    <tbody>
        @foreach (var appointment in appointments)
        {
            <tr>
                <th scope="row">@appointment.PatientName</th>
                <td>@appointment.Date.ToString("MM/dd/yyyy")</td>
                <td>@appointment.Time</td>
                <td>
                    <button aria-label="View details for @appointment.PatientName">
                        View
                    </button>
                </td>
            </tr>
        }
    </tbody>
</table>
```

## Testing Accessibility

### Automated Testing

```bash
# Install axe-core for automated WCAG testing
npm install --save-dev @axe-core/cli

# Run accessibility audit
axe http://localhost:5145 --tags wcag2a,wcag2aa
```

### Manual Testing Checklist

- [ ] All functionality accessible via keyboard (no mouse required)
- [ ] Focus indicators visible on all interactive elements
- [ ] Screen reader announces dynamic content changes
- [ ] Color contrast meets WCAG AA standards (4.5:1 text, 3:1 interactive)
- [ ] Images have alt text or aria-label
- [ ] Forms have associated labels
- [ ] Error messages clearly identify the problem
- [ ] Modals trap focus and restore on close
- [ ] Skip links allow bypassing navigation

### Screen Reader Testing

**Test with:**
- **NVDA** (Windows) - Free, widely used
- **JAWS** (Windows) - Industry standard
- **VoiceOver** (macOS/iOS) - Built-in
- **TalkBack** (Android) - Built-in

**Key scenarios:**
1. Navigate entire form using only Tab/Shift+Tab
2. Fill out patient intake form
3. Submit form and verify success announcement
4. Trigger validation errors and verify announcements
5. Open/close modal dialogs

## PTDoc-Specific Patterns

### Clinical Documentation

```razor
<!-- SOAP note section with proper structure -->
<section aria-labelledby="subjective-heading">
    <h3 id="subjective-heading">Subjective</h3>
    <textarea aria-label="Patient's subjective complaints"
              aria-describedby="subjective-hint">
    </textarea>
    <span id="subjective-hint" class="hint-text">
        Include patient's chief complaint and history
    </span>
</section>
```

### Pain Scale Widget

```razor
<div role="group" aria-labelledby="pain-scale-label">
    <label id="pain-scale-label">Pain Level</label>
    <input type="range" 
           min="0" 
           max="10" 
           @bind="painLevel"
           aria-valuemin="0"
           aria-valuemax="10"
           aria-valuenow="@painLevel"
           aria-valuetext="@GetPainDescription(painLevel)" />
    <output>@painLevel - @GetPainDescription(painLevel)</output>
</div>

@code {
    private int painLevel = 0;
    
    private string GetPainDescription(int level) => level switch
    {
        0 => "No pain",
        1 or 2 or 3 => "Mild pain",
        4 or 5 or 6 => "Moderate pain",
        7 or 8 or 9 => "Severe pain",
        10 => "Worst possible pain",
        _ => ""
    };
}
```

## Resources

- [WCAG 2.1 Guidelines](https://www.w3.org/WAI/WCAG21/quickref/)
- [ARIA Authoring Practices](https://www.w3.org/WAI/ARIA/apg/)
- [WebAIM Resources](https://webaim.org/resources/)
- [Section 508 Standards](https://www.section508.gov/)
- [ADA Requirements](https://www.ada.gov/resources/web-guidance/)
