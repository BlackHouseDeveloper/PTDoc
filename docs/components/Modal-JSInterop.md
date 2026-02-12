# Modal JS Interop Implementation

## Overview

The Add Patient modal uses JavaScript interop to provide enhanced user experience features that aren't available in pure Blazor/C#.

## Architecture

```
┌─────────────────────────────────────┐
│   AddPatientModal.razor (Blazor)    │
│                                     │
│  - Component lifecycle               │
│  - State management                  │
│  - Data binding                      │
└──────────────┬──────────────────────┘
               │
               │ JS Interop (IJSRuntime)
               │
┌──────────────▼──────────────────────┐
│   modal.js (JavaScript Module)      │
│                                     │
│  - ESC key handling                  │
│  - Body scroll lock                  │
│  - Focus trap                        │
│  - DOM manipulation                  │
└─────────────────────────────────────┘
```

## Files

- **Modal Component**: `/src/PTDoc.UI/Components/AddPatientModal.razor`
- **JS Module**: `/src/PTDoc.UI/wwwroot/js/modal.js`
- **CSS Styles**: `/src/PTDoc.UI/Components/AddPatientModal.razor.css`

## Implementation Details

### 1. ESC Key Handler

**Problem**: Blazor doesn't provide a built-in way to listen for global keyboard events.

**Solution**: JavaScript event listener that calls back to Blazor.

```csharp
// Blazor side (AddPatientModal.razor)
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (IsOpen && modalModule != null)
    {
        await modalModule.InvokeVoidAsync("registerEscapeHandler", modalId, dotNetRef);
    }
}

[JSInvokable]
public async Task CloseFromJs()
{
    await Close();
}
```

```javascript
// JavaScript side (modal.js)
export function registerEscapeHandler(modalId, dotNetRef) {
    const handler = (e) => {
        if (e.key === 'Escape' || e.key === 'Esc') {
            e.preventDefault();
            dotNetRef.invokeMethodAsync('CloseFromJs');
        }
    };
    // Store and register handler
}
```

**Key Points**:
- Handler is registered when modal opens
- Handler is unregistered when modal closes
- Uses DotNetObjectReference to call back to Blazor
- Prevents event propagation to avoid conflicts
- Supports both 'Escape' and 'Esc' key codes

### 2. Body Scroll Lock

**Problem**: When a modal is open, users should not be able to scroll the background content. CSS alone can cause layout shifts due to scrollbar disappearance.

**Solution**: JavaScript that calculates scrollbar width and prevents layout shift.

```javascript
// JavaScript side (modal.js)
export function lockBodyScroll() {
    const scrollbarWidth = getScrollbarWidth();
    
    // Prevent body scroll
    document.body.style.overflow = 'hidden';
    
    // Prevent layout shift
    if (scrollbarWidth > 0) {
        document.body.style.paddingRight = `${scrollbarWidth}px`;
    }
    
    document.body.classList.add('modal-open');
}
```

**Key Points**:
- Calculates scrollbar width to prevent layout shift
- Adds padding-right equal to scrollbar width
- Sets `overflow: hidden` on body
- Adds `modal-open` class for CSS hooks
- Restores original state on close

### 3. Focus Trap

**Problem**: Tab navigation should cycle within the modal, not escape to background elements.

**Solution**: JavaScript tracks first/last focusable elements and traps focus.

```javascript
// JavaScript side (modal.js)
export function setupFocusTrap(modalElement) {
    const focusableElements = modalElement.querySelectorAll(
        'button, [href], input, select, textarea, [tabindex]:not([tabindex="-1"])'
    );
    
    const trapHandler = (e) => {
        if (e.key === 'Tab') {
            // Handle Tab/Shift+Tab to cycle focus
        }
    };
    
    modalElement.addEventListener('keydown', trapHandler);
    return { dispose: () => modalElement.removeEventListener('keydown', trapHandler) };
}
```

**Key Points**:
- Finds all focusable elements in modal
- Cycles focus from last to first (and vice versa)
- Supports Shift+Tab for reverse navigation
- Returns disposable handler

## Component Lifecycle

```
Component Mounted
    │
    └─→ Load modal.js module (once)
    └─→ Create DotNetObjectReference
    
IsOpen = true
    │
    └─→ Lock body scroll
    └─→ Register ESC handler
    └─→ Focus first input
    
User presses ESC
    │
    └─→ JS calls CloseFromJs()
    └─→ Blazor updates IsOpen = false
    
IsOpen = false
    │
    └─→ Unlock body scroll
    └─→ Unregister ESC handler
    
Component Disposed
    │
    └─→ Dispose JS module
    └─→ Dispose DotNetObjectReference
    └─→ Clean up all handlers
```

## Memory Management

The component implements `IAsyncDisposable` to ensure proper cleanup:

```csharp
public async ValueTask DisposeAsync()
{
    if (modalModule != null)
    {
        await modalModule.InvokeVoidAsync("unlockBodyScroll");
        await modalModule.InvokeVoidAsync("unregisterEscapeHandler", modalId);
        await modalModule.DisposeAsync();
    }
    dotNetRef?.Dispose();
}
```

**Critical**: Always dispose:
- IJSObjectReference (modalModule)
- DotNetObjectReference (dotNetRef)
- Event listeners (via JS module)

## Testing Checklist

- [ ] ESC key closes modal
- [ ] Background is not scrollable when modal is open
- [ ] No layout shift when modal opens/closes
- [ ] Focus stays within modal when tabbing
- [ ] Tab cycles from last to first element
- [ ] Shift+Tab cycles backward
- [ ] First input is focused on modal open
- [ ] All handlers are cleaned up on disposal
- [ ] No memory leaks after multiple open/close cycles

## Browser Compatibility

All features use standard Web APIs:
- `document.addEventListener('keydown', ...)` - All modern browsers
- `DOMElement.focus()` - All modern browsers
- `window.getComputedStyle()` - All modern browsers
- ES6 Modules (`import`/`export`) - All modern browsers

Supports: Chrome 90+, Firefox 88+, Safari 14+, Edge 90+

## Performance Considerations

1. **Module Loading**: JS module is loaded once on first render, cached for subsequent uses
2. **Event Listeners**: Only attached when modal is open, removed when closed
3. **Scrollbar Calculation**: Cached after first calculation
4. **DotNetObjectReference**: Reused for all modal instances of the same component

## Common Issues & Solutions

### Issue: ESC key doesn't close modal
**Solution**: Check browser console for JS errors. Ensure modal.js is being loaded correctly.

### Issue: Background still scrollable
**Solution**: Verify `lockBodyScroll()` is being called. Check if another script is resetting body styles.

### Issue: Layout shifts when modal opens
**Solution**: Scrollbar width calculation may be incorrect. Check if custom scrollbar styles are interfering.

### Issue: Memory leaks
**Solution**: Ensure `DisposeAsync()` is being called. Check browser DevTools Memory profiler for retained objects.

---

**Last Updated**: February 12, 2026
**Status**: ✅ Production Ready
