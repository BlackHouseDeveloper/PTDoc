Blazor Application Behavior – Key Mechanics and Guardrails for PTDoc
Blazor Component Rendering and Lifecycle

Rendering Flow: Blazor components render by building an in-memory render tree that efficiently updates the browser DOM. A component always renders on first use, and thereafter re-renders only when triggered by events or data changes. By default, re-renders happen when a parent supplies new parameters, a cascading value changes, an event handler runs, or the component explicitly requests an update via StateHasChanged(). The framework avoids unnecessary work – for example, if none of a component’s parameters changed (for primitive or known types) or its ShouldRender() returns false, Blazor will skip a re-render. This means a component may not refresh unless its state truly changes or you manually force it. In general, you rarely need to call StateHasChanged yourself except in special scenarios, because Blazor’s conventions propagate state changes and trigger child updates automatically.

Lifecycle of a component on first render: OnInitialized/OnParametersSet run (waiting for any async tasks), then the component renders. Subsequent events also trigger re-renders as needed.

Lifecycle Methods: Blazor offers structured lifecycle callbacks on the base class ComponentBase for component initialization and after rendering. Key events include:

OnInitialized / OnInitializedAsync – called when the component is first instantiated and its initial parameters are set. Use these to kick off loading data or other setup. If OnInitializedAsync returns an incomplete task (e.g. awaiting an API call), Blazor will await it and then automatically rerender the component with the loaded data. The synchronous OnInitialized (if overridden) runs before the async one.

OnParametersSet / OnParametersSetAsync – called each time after a component’s incoming parameters have been updated (during initial render and any subsequent re-renders caused by the parent). Again, if OnParametersSetAsync is asynchronous, Blazor waits for it to finish then rerenders the component. This ensures a child can finish processing new inputs before the UI is updated.

ShouldRender – a method you can override to control whether to allow a render. By default it returns true (always render on changes), but you can return false to skip a render cycle. This is rarely overridden except for performance tuning.

OnAfterRender / OnAfterRenderAsync – called after each render is finished and the DOM is updated. The bool parameter firstRender tells you if this is the first time (useful for one-time setup like JavaScript interop calls). Important: In prerendered server-side Blazor, OnAfterRender is not called during the initial static render, only when the component becomes interactive on the client. Thus, put initialization code that requires a fully interactive DOM (e.g. JS interop) inside an if (firstRender) guard so it runs only when the client is ready.

IDisposable.OnDisposing – not a lifecycle method per se, but if a component implements IDisposable/IAsyncDisposable, Blazor calls Dispose when the component is removed. Use this to clean up timers, event handlers, or JS objects to prevent memory leaks.

Event Handling and State Updates: When a UI event occurs (button click, etc.), Blazor runs the bound event handler and then triggers a re-render on that component and any affected children. If the event handler returns a Task (i.e. is async), Blazor will await it; any state changes made mid-handler won’t show until the handler completes because the UI update is queued to run after the event logic finishes. You generally do not need to call StateHasChanged inside normal event handlers – the framework handles it. However, for long-running or multi-phase async events, you can force interim UI updates. For example, an async method performing several await steps might call StateHasChanged() between steps to refresh partial results (the first call queues a render, which executes when the method yields control). Blazor ensures that redundant calls to StateHasChanged won’t stack up extra renders – if you call it multiple times in a loop without yielding, only one re-render occurs afterwards. Avoid calling StateHasChanged more than necessary: it incurs rendering cost. Typically you use it only when state changes happen outside the normal Blazor flow (e.g. a .NET Timer or an event callback from a non-Blazor service), because those won’t automatically trigger UI updates. When using such external events, make sure to call them on the correct context – Blazor will throw if StateHasChanged is invoked from a non-UI thread without using InvokeAsync.

Render cycle summary: if not the first render and ShouldRender() returns false, Blazor skips updating the UI. Otherwise it diffs the render tree and updates the DOM, then calls OnAfterRender hooks.

Component State and Re-rendering: It’s crucial to understand that Blazor uses unidirectional data flow. Parent components pass parameters down to children, and events or callback delegates let children send information up to parents. A parent re-rendering will update its child parameters, potentially causing child re-renders. If a parent calls StateHasChanged() (or otherwise re-renders) without changing a parameter value, the child will not rerender if all its parameters are the same as last time. This is by design to prevent unnecessary work. Pitfall: Avoid writing code where a child component’s markup or behavior depends on some global variable or external state that isn’t passed in as a parameter or cascading parameter – if the parent doesn’t know that state changed, it won’t re-render the child. Always make important state part of the parameter model or call the child’s methods via event callbacks to trigger updates.

Finally, note that Blazor components render hierarchically. A parent’s output is computed first to know which child components should exist in the UI. So parent lifecycle methods run before child initialization (for synchronous code), though asynchronous init can blur the order (an async parent and async child may complete in any order). Once rendered, each component’s UI is isolated except for the data you pass – one component’s re-render doesn’t automatically refresh its siblings unless the parent causes it by changing their inputs.

Component Parameters and Data Binding

Parameter Basics: To pass data into a child component, you define a public property in the child marked with [Parameter]. The parent can then specify an attribute in the component’s tag to set that parameter. Parameters can be of any serializable type (primitives, strings, complex objects, even RenderFragments). For example, if a child component has \[Parameter] public string Title { get; set; }, a parent can include <MyChild Title="Hello"/> to pass that in. If a parent omits a parameter, the child’s property just keeps its default value (parameters can have default field initializers as seen in docs). However, do not confuse initial parameter values with ongoing state – a component should treat parameters as read-only inputs from its parent. In fact, the official guidance is never to write to your own [Parameter] properties after the first render. Overwriting parameters internally can lead to inconsistent UIs or being clobbered by the parent on the next update. The Blazor framework will simply re-assign the [Parameter] property whenever the parent re-renders, so any value the child stored there could be lost. For example, if a child has a parameter InitiallyExpanded and in its own code it does InitiallyExpanded = true when a user clicks something, that state might be overwritten when the parent updates that child (since the parent might still be passing InitiallyExpanded=false as originally). The recommended approach is: don’t mutate parameters; instead, if the component needs to change something, either raise an event to ask the parent to change the value, or use an internal property/field for tracking state changes. In scenarios where you want two-way binding (parent and child both updating a value), use the @bind- syntax which under the covers wires an event callback to propagate changes up.

Required Parameters: If a component truly cannot function without a certain parameter, you can enforce it by decorating the property with [EditorRequired] in addition to [Parameter]. This will prompt editors or build tools to warn if the parent doesn’t provide that parameter. It’s preferable to using C# required or init-only setters on components, which are not honored by the Blazor parameter binding process. For example, you might mark a Chart component’s DataSource parameter as required so that any omission is caught during development.

Child Content and RenderFragment: One special kind of parameter is RenderFragment (or RenderFragment<T>) which allows a parent to pass a chunk of UI to be rendered inside the child. This is how components can act as wrappers or templates. By convention, a component can define public RenderFragment? ChildContent { get; set; } [Parameter] to capture anything placed between its opening and closing tags. The Blazor compiler looks for a parameter exactly named ChildContent to assign the inner content. If you’re building a wrapper component (like a card that wraps arbitrary content), always include a [Parameter] RenderFragment ChildContent and then render @ChildContent in your component’s markup where appropriate. Failing to do this means any inner HTML provided by the parent will be ignored (or cause a compile error). By default, you don’t need to explicitly name the parameter when using the component; for example <MyCardComponent>Some text</MyCardComponent> will automatically pass “Some text” into the ChildContent fragment of MyCardComponent. You can also have multiple RenderFragment parameters (with different names) or generic ones for templated controls, but the usage is more advanced. Just remember: to project content, use ChildContent. Also, you cannot bind events directly to a RenderFragment parameter – events must be handled inside the fragment or passed as separate [Parameter] callbacks; the fragment is purely for UI content.

Data Binding (One-way vs Two-way): Standard usage is one-way down (parent to child). If you use @bind-Value="someField" on a child component (assuming it has a [Parameter] public T Value { get; set; } and a corresponding [Parameter] public EventCallback<T> ValueChanged { get; set; }), Blazor sets up two-way binding such that changes in the child call ValueChanged to update the parent’s someField. In PTDoc, ensure that if you generate form-like components or inputs, you follow the Blazor pattern for two-way bindable properties (Parameter + EventCallback pair named with Changed). Otherwise, the parent won’t know about updates.

Avoiding Parameter Pitfalls: To summarize safe parameter usage in guardrails for our agents:

Never have a component set its own [Parameter] property internally after initialization. Use internal state instead, or design a callback to inform the parent of needed changes. This prevents weird bugs like parent overwriting child state or infinite render loops.

If a parameter is essential, mark it with [EditorRequired] so missing assignments are caught early.

When adding a new component in a shared library or folder, remember to update _Imports.razor or add @using in consuming pages so the component’s namespace is known. Otherwise, you’ll get build warnings or it might fail to find the component (e.g. a <PTDocMetricCard> tag not recognized until you import its namespace).

Use correct casing when referencing components in markup: the first letter must be uppercase or Blazor will treat it as an HTML element and not as a component. For example, <PTDocMetricCard /> is valid if PTDocMetricCard is a component class, but <PTDocMetricCard> (lowercase) will not work and would likely just render nothing or give an error. Always name component files and classes in PascalCase (UpperCamelCase).

If your component uses cascading parameters (via [CascadingParameter] for ambient context like theme or user info), ensure that some ancestor provides a matching [CascadingValue]. Otherwise, the cascading parameter will remain at its default value. Also be cautious not to unintentionally consume cascading values by name matching – it’s better to use the Name property on CascadingParameter and CascadingValue to explicitly link them by name if you have multiple of the same type in scope.

Integration Caveats (Blazor WebAssembly vs Server, MAUI, and MVC)

Client-Side (WebAssembly) vs Server-Side Blazor: PTDoc uses Blazor in a hybrid manner – a Blazor WASM web app and a .NET MAUI app embedding Blazor. In Blazor WebAssembly, the entire app (components, rendering logic, .NET runtime) runs in the browser sandbox. This means there is no persistent server connection required for UI updates (everything is local once loaded, and data calls go through HTTP APIs). One caveat is that initial load time can be significant if the DLLs are large, so keep an eye on payload size and use ahead-of-time compile or trimming if needed for performance. Also, since state is purely in the browser, a page refresh or navigating away will reset the state unless you explicitly use browser storage or other persistence. Agents should remember to use features like PersistentComponentState (for prerendered scenarios) or saving to localStorage via JS interop if some state should survive reloads.

In Blazor Server, the components run on the server and UI updates are real-time via a SignalR connection. This has different considerations:

Each user connection (called a Circuit) holds component state in memory on the server. We must avoid using static variables for anything user-specific, as that would be shared across all users (leading to data leakage). Always use DI services with appropriate lifetimes (Scoped services are one-per-circuit in server-side) or store per-user state in CascadingParameters, not statics. In WASM, a static is fine for app-wide state (since one user = one runtime), but in Server it’s global.

If a circuit disconnects (network issue or user closes the app), any transient state in memory is gone. Recommend to handle circuit events if needed (for example, you can log or attempt to save state when a circuit is down via CircuitHandler). Also design components to be resilient – e.g., if a user reconnects, they may see a fresh component with no state unless you used PersistentComponentState to persist it across prerender and interactive phase.

Prerendering: In a Blazor Server (or Blazor Web App in .NET 8) scenario, the app often prerenders the UI as static HTML first (for faster first paint or SEO) and then establishes the interactive circuit. During prerender, components run their OnInitialized{Async} and OnParametersSet normally, but as noted, they do not run OnAfterRender until the interactive phase. Also, the framework waits for all async tasks in these lifecycle methods to complete (called quiescence) before sending the HTML to the client. This can lead to a situation where a slow operation in OnInitializedAsync will delay the entire page from rendering any UI, essentially showing a blank page while waiting. This is a known pitfall – the user might not even see a loading indicator because the component hasn’t rendered yet! To mitigate this: always show a placeholder or loading state synchronously, then perform long loads asynchronously. For example, set a flag like isLoading=true initially, start loading data in OnInitializedAsync, and in your markup show a spinner or “Loading…” message when isLoading is true. Once data is loaded (isLoading=false), Blazor will re-render and show the real content. This way, during prerender the user sees at least the loading message instead of nothing. In .NET 8, you can also use the new Streaming rendering feature: applying [StreamRendering(true)] on a component allows it to render partial output (like that loading message) immediately during prerender, and then stream the updated content later without blocking the whole page. Streaming rendering is opt-in because it can cause slight content shift when the real data appears, but it greatly improves perceived performance for slow components. PTDoc should consider using @attribute [StreamRendering] on dashboard widgets that fetch data, so the overview page isn’t blank if the network is slow.

Double initialization: One subtle effect of prerender + interactive is that certain components might run their init logic twice (once on the server, once on the client after SignalR connects). If you use [StreamRendering] or not careful, you could trigger duplicate loads (e.g., fetching the same data twice). To avoid that, .NET provides PersistentComponentState where you can stash the result from prerender and reuse it on the client so the second render doesn’t redo the work. Our internal guidelines can note: when using prerendering, guard against double-execution of expensive calls (one strategy: only load data if not already loaded, e.g. Data ??= await LoadDataAsync() as shown in docs, and consider marking data with [PersistentState] so it carries over). For PTDoc’s web app (WASM) this isn’t a concern, but if we ever prerender (or if MAUI does something similar on first load), it’s good to be aware.

.NET MAUI Integration: In the PTDoc MAUI app, Blazor is used via a BlazorWebView. This means the Blazor UI runs in a WebView component within a native app. Most of the Blazor behavior is the same as WASM (it’s essentially running a Blazor WebAssembly instance within the app). One caveat is that calling into platform features or file system may require dependency injection or JS interop with platform-specific code – ensure any such usage is abstracted (the PTDoc architecture likely separates concerns so that Blazor calls APIs provided by .NET MAUI or .NET libraries). For our purposes, agents should ensure components work in a constrained WebView environment (e.g., no assumptions about having a full browser’s capabilities like certain DOM APIs might not function in WebView sandbox, and file paths might differ). Also, do not use Window-specific JS calls (like window.alert) in a MAUI BlazorWebView – those might not behave as expected on mobile/desktop.

Using Components in Razor Pages or MVC: Although PTDoc is Blazor-centric, note that Blazor components can be embedded in traditional MVC/Razor Pages apps via the <component> tag helper. For example, one could render a Blazor component in a Razor Page with: <component type="typeof(MyComponent)" render-mode="WebAssemblyPrerendered" />. The render-mode is important – Static means just render once to HTML and that’s it (no interactivity), ServerPrerendered means include the initial HTML and bootstrap a Blazor Server connection for interactivity, WebAssemblyPrerendered means prerender and then bootstrap Blazor WASM, etc. If PTDoc were to integrate Blazor components into any non-Blazor page, ensure the correct render-mode is chosen. The docs note that statically-rendered components (no interactivity) cannot be updated or removed after rendering. So if you need a live, updating component, use one of the interactive modes. Also ensure the Blazor script is included on the page (blazor.server.js or blazor.webassembly.js as appropriate). This likely doesn’t apply to PTDoc currently, but it’s useful knowledge if in the future we embed certain components elsewhere.

Styling and Layout: Blazor uses standard CSS, and you can use either global styles or scoped CSS for components. When using scoped CSS (a .razor.css file alongside the component), Blazor will auto-scope those styles by adding unique attributes. Just remember that if a component’s elements need to be styled from outside (e.g., by a parent page’s CSS or a third-party stylesheet), scoped CSS might prevent that – you might need to turn off scoping for certain global styles or ensure consistent class names. For dashboard components like PTDocMetricCard, ensure any required CSS or icon libraries are available in both the web and MAUI host. In MAUI BlazorWebView, you might need to add the CSS files in the wwwroot of the Blazor part and confirm they are loaded.

Also, be mindful of layout components (like if using a LayoutComponent with @Body). If a new component relies on being inside a specific layout that provides context (e.g., a CascadingParameter for user theme), using it elsewhere without that layout could break it. Document any such assumptions.

Known Pitfalls Impacting Dashboards and Visibility

Loading and Async Data: As mentioned, a common pitfall for dashboard widgets is doing heavy work on first render without feedback. If the PTDocMetricCard was not showing anything, perhaps it was waiting on data with no indication. The user might see a blank space. To prevent this, always initialize visible state immediately. For example, if PTDocMetricCard loads metrics from an API, the component should at least render a placeholder card or spinner while the data loads asynchronously. This ensures the “Overview” page isn’t mysteriously empty or incomplete-looking. Agents updating such components should implement an isLoading flag or similar pattern as part of our standards.

Conditional Rendering: Another cause of invisible components can be misused conditional logic. If the wrapper component only renders its content when certain conditions are true, double-check the logic. For instance, <PTDocMetricCard>@if(data != null){ ... }</PTDocMetricCard> could render nothing if data is null. It might be better to move the if inside the component and show a loading state. Also ensure conditions are not inadvertently always false due to scoping issues (e.g., using @code variable vs a passed-in parameter incorrectly).

Parameters Not Passed: If a component expects a parameter (especially marked EditorRequired) and the parent doesn’t provide it, the component might render a default state or nothing. In an “Overview” dashboard scenario, if PTDocMetricCard needed a MetricsModel parameter and it wasn’t given, the card might have no data to display. The fix is either provide the param or have the component fetch its own data. This again points to using EditorRequired to catch such mistakes at design time. It’s a good practice we should add: Agents must mark critical input parameters with [EditorRequired] and heed any warnings about unset required params when reviewing Copilot suggestions.

Component Registration and Imports: A component that compiles and runs in one project (say the Shared RCL) but “does not render” in the app could simply be that the app didn’t know about it. In Blazor, if you add a new component to a class library, you must reference that library in the host app and usually add an @using YourLibrary.ComponentsNamespace either in each usage or globally in _Imports.razor. If PTDocMetricCard was added to PTDoc.Shared but the web project’s _Imports.razor wasn’t updated, the <PTDocMetricCard> tag in a page might be silently ignored or produce a build error (depending on whether the tooling caught it). The docs explicitly show adding @using BlazorSample.AdminComponents in _Imports.razor when using a custom folder of components. So our guardrail: whenever an agent creates a new component or moves one to a new namespace, update the relevant _Imports.razor so it’s included by default in pages. Also, adhere to the file naming conventions: if one of our components was named with a lowercase first letter, it simply will not work in Razor markup – always use PascalCase file/class names (the PTDoc codebase likely already enforces this, but it’s worth reinforcing).

Wrapper Components and Child Content: If PTDocMetricCard is a wrapper (e.g., a styled card that wraps arbitrary content or a chart), ensure that any content passed inside is being rendered. This means verifying that the component defines ChildContent and uses it. A mistake here could lead to the card appearing blank because the inner content never gets displayed. Our internal instructions should remind: when generating a wrapper, always include the standard ChildContent parameter and place @ChildContent in the markup where needed.

State Management across Components: For dashboard scenarios, multiple components might share some state (like a selected date range or patient ID). Using cascading parameters or a dedicated state container service is preferable to drilling too many parameters through. But be careful with service lifetimes: a singleton service holding UI state (like UI preferences) is fine, but if it holds per-user data in a server app, it could mix users. In PTDoc’s case, with WASM and MAUI (no multi-user server), a singleton state service is effectively per user, so that’s safe. Just note: if we ever introduce server-hosted Blazor, we’d switch such services to Scoped.

JS Interop and Visibility: If a component relies on a JS library (say a charting library) to render itself, ensure the JS is properly initialized after the component appears. Often this is done in OnAfterRenderAsync(firstRender) to activate a chart on a canvas element. If PTDocMetricCard was showing a chart and it didn’t call the JS, the card might appear empty. This ties back to OnAfterRender and prerender: in a server scenario, calling JS on firstRender (which might be prerender) would fail; you might need to wrap JS calls like: if (firstRender && JsRuntime.IsBrowser()) or simply ensure the component only rendered on the client. Agents adding JS interop should follow the pattern: call JS in OnAfterRenderAsync and ensure the script is loaded in the page (for MAUI, add to index.html or use the proper file scheme).

Layout and CSS Pitfalls: If a component is present but just not visible (0px height etc.), it could be a CSS issue (e.g., parent container with display:none or a flex container not stretching). While not directly a Blazor framework issue, our guidelines can include: After adding a new UI component, verify its CSS classes and surrounding layout to confirm it’s not inadvertently hidden. For instance, a common mistake is forgetting to add the component to the page’s grid or container. If Overview.razor had a grid and the agent forgot to place PTDocMetricCard in the markup properly, it simply wouldn’t show. Always double-check the final Razor page to see that the component tag is present in the expected location and not inside a conditional that never executes.

Updates to Copilot-Instructions.md and Agents.md (Guardrails for AI Contributions)

To prevent the kinds of issues noted above, we will add specific rules to our AI guidance files:

Additions to Copilot-Instructions.md: (for guiding GitHub Copilot code suggestions)

Component Naming & Declaration: “When Copilot suggests creating a new Blazor component, always use PascalCase for file and class names (e.g. MyComponent.razor), and ensure the class name inside matches the file. The component’s first letter must be uppercase. If adding a component in a new folder/namespace, have Copilot include an @using of that namespace in the appropriate _Imports.razor so the component is recognized by the app. (This avoids components that silently fail to render due to casing or namespace issues.)”

Parameters and Binding: “Copilot must mark input properties with [Parameter]. Do not write code that sets those properties within the component after initialization – treat them as read-only inputs. If the component needs to change something (e.g., toggle a panel open/closed), use internal state fields or two-way binding (with EventCallback). Avoid any design where a parent’s re-render would overwrite a child’s state unexpectedly. For required inputs, Copilot should use the [EditorRequired] attribute so missing parameters are caught at design time. Also, ensure any event-callback pairs follow the naming convention (e.g., Value with ValueChanged) so two-way binding can be used when appropriate.”

Wrapper Components & ChildContent: “When suggesting a ‘wrapper’ component (one that encapsulates other markup), Copilot should include a [Parameter] RenderFragment ChildContent { get; set; } property and render @ChildContent in the component’s Razor body by convention. This ensures any inner content passed by the parent will be displayed. Omit this only if the component is explicitly designed never to have inner content.”

Lifecycle and Async: “Copilot should prefer using OnInitializedAsync for startup logic that involves fetching data (with await calls). It should provide a loading indicator or placeholder in markup while async data is being fetched. For example, suggest an if (loading) ... else ... pattern, so the UI isn’t blank during load. If generating code in a server-prerendered context (or MAUI hybrid), avoid long blocking operations in lifecycle methods – instead, do minimal synchronous work and do heavy lifting asynchronously (with proper loading UI).”

StateHasChanged & UI Updates: “Copilot must not sprinkle StateHasChanged() calls unless necessary. The code suggestions should rely on Blazor’s automatic re-render on events and parameter changes. Only in cases like timer callbacks or external events (which it should rarely suggest without prompt) should it use InvokeAsync(StateHasChanged) pattern. Unnecessary calls cause performance issues.”

Error Handling & Debugging UI: “If Copilot suggests a complex UI update and something might not render, it should also suggest checking for errors in the browser console or .NET output. (This is more of a dev guidance, but we can encode it: if a component is blank, look for exceptions in console – often a null reference or similar prevented rendering.) Ensure Copilot includes null-checks where appropriate (e.g., @someObject?.Property in Razor) to avoid runtime errors that break component rendering.”

Additions to Agents.md: (for AI coding agents making direct changes)

Under the Blazor Guidelines section, we’ll insert rules like:

Blazor Component Standards: “Agents must follow Blazor component naming and usage rules: Components should be created in PascalCase and placed in the proper folder (Pages for routable pages, Shared/Components for others, unless otherwise specified). After adding a component, update _Imports.razor or relevant files to include its namespace. Verify that the component appears in the UI by running the app if possible.”

Ensure Component Visibility: “Before concluding a UI change, the agent should verify that new components actually render content. For example, if adding PTDocMetricCard to Overview.razor, ensure it’s not inside a never-true conditional and that any data it needs is provided. Agents should add fallback content (like “No data” or loading spinners) so that a component isn’t empty while waiting for inputs.”

Lifecycle Usage: “Agents must utilize Blazor lifecycle methods appropriately. Use OnInitializedAsync for async setup (and avoid long work in synchronous OnInitialized). If using OnAfterRenderAsync, check the firstRender flag to run one-time setup (like JavaScript interop) to avoid repeat execution. Do not call OnAfterRender code during prerender that assumes interactivity – guard it or delay it until connected (especially important for MAUI and future server-side usage).”

No Parameter Self-Overwrite: “Agents are forbidden from writing code that sets a [Parameter] property inside the component after first render. This can cause parent-child synchronization bugs. Instead, if a component needs to change due to user action, either emit an EventCallback to inform the parent or use an internal private field. For example, do not do ParameterX = newValue in the child; that should be handled via binding or parent logic.”

Dashboard Specific: “When modifying dashboard cards or metrics components, agents must ensure that each component handles loading states and error states. Provide user feedback for slow data (e.g., a <em>Loading...</em> text). Also, components that depend on context (like selected patient or date range) should use cascading parameters or parameters from the parent – the agent should not introduce hidden dependencies. Document in the component’s code comments what it expects (to ease future integration).”

Testing Interactive Behavior: “Agents should add or update unit tests for critical components if possible (though UI logic can be tricky to unit test, consider using bUnit for Blazor components). At minimum, manually verify that after an agent’s changes, the component appears and updates as expected in both the Blazor Web app and the MAUI app. For instance, after fixing PTDocMetricCard, ensure it shows up on the Overview page with real data and responds to any user interaction.”

By encoding the above in our Copilot and Agents instructions, we create a safety net. The AI assistant will be guided to produce Blazor components that correctly declare parameters, manage state, and integrate without the common pitfalls that break rendering. In summary, the overarching rules for any AI-generated Blazor code in PTDoc are: respect the Blazor component model (proper parameters, lifecycle, naming), don’t break the render flow (no self-parameter writes, unnecessary StateHasChanged, etc.), and always account for loading and integration context to avoid invisible or non-functional UI. Following the official .NET 8 Blazor guidance on these points will prevent issues like the missing PTDocMetricCard and improve overall stability of the PTDoc application.

Sources:

Microsoft Docs – ASP.NET Core Razor components (Blazor fundamentals on components, parameters, naming)

Microsoft Docs – Razor component lifecycle and rendering (lifecycle events, rendering triggers, StateHasChanged usage)

Microsoft Docs – Avoid overwriting parameters in Blazor (why components should not write to their own parameters)

Microsoft Docs – Blazor asynchronous rendering and loading states (quiescence, streaming rendering, placeholders during async tasks)

PTDoc Agents Guide – Agents.md (project-specific standards to be extended with Blazor rules)

## Blazor Hybrid (MAUI) – Architecture, Lifecycle, and Integration

### BlazorWebView Architecture in .NET MAUI (Blazor Hybrid)

In PTDoc's .NET MAUI client, Blazor is integrated via the BlazorWebView control, creating a hybrid application. This means Razor components run natively on the device within the MAUI app's .NET process, and render their output to an embedded Web View UI element. Unlike Blazor WebAssembly (WASM) or Server, components in a BlazorWebView do not run in a browser sandbox or require WebAssembly at all – they execute on the normal .NET runtime (with full access to the device's capabilities) and communicate with the WebView through a local interop channel. The Blazor UI is essentially hosted inside a WebView, but all UI updates are driven by .NET code. In practice, this hybrid setup behaves much like client-side Blazor in terms of UI logic, just hosted in a native app instead of a standalone browser tab.

**How it's set up:** A MAUI Blazor app typically includes a wwwroot/index.html (the Blazor host page) and uses BlazorWebView on a XAML page to load it. For example, the MAUI MainPage.xaml might contain:

```xml
<BlazorWebView HostPage="wwwroot/index.html">
    <BlazorWebView.RootComponents>
        <RootComponent Selector="#app" ComponentType="{x:Type local:Main}" />
    </BlazorWebView.RootComponents>
</BlazorWebView>
```

This XAML points the BlazorWebView to the Blazor app's host page and identifies the root Razor component to load (e.g. Main.razor) and where to render it in the DOM (Selector="#app" matches an element in index.html). Under the hood, the BlazorWebView will load the HTML from the app's resources and initialize the Blazor runtime. In the MAUI startup (MauiProgram.cs), you must register Blazor services by calling `builder.Services.AddMauiBlazorWebView();` (and usually enable debugging helpers in development via `builder.Services.AddBlazorWebViewDeveloperTools();`). Once configured, the MAUI app and Blazor share the same dependency injection (DI) container, allowing services and data to be easily shared across the native and web portions of the app.

**Browser Engine Differences:** Because rendering is done in a WebView, the exact web engine varies by platform. For example, MAUI uses WebView2 (Edge Chromium) on Windows, Chromium WebView on Android, and WKWebView (Safari) on iOS/Mac. This means that some HTML/CSS or JavaScript might behave slightly differently on each platform. Platform-specific web APIs may only be available on certain engines, and styling can differ. Always test UI on all target platforms to catch any inconsistencies. Also note that on Windows, end users must have the WebView2 runtime installed for BlazorWebView to function (the developer template usually displays a notice or handles this).

### Component Lifecycle in a MAUI Blazor Hybrid App

Blazor components in a BlazorWebView follow the same lifecycle conventions as in standard Blazor (since the Blazor framework running inside the WebView is essentially the same as Blazor WASM/Server). Each component goes through initialization (OnInitialized{Async}), parameter setting (OnParametersSet{Async} on each render), rendering, and after-render events (OnAfterRender{Async}) just as it would on the web.

**No prerendering phase:** There is no server prerendering phase in a typical MAUI Blazor app, so you don't have the split between a static prerender and interactive render as in Blazor Server. This means that `OnAfterRenderAsync(firstRender:true)` will run exactly once on the first render when the WebView has fully loaded the component, so you can safely put JavaScript initialization calls there without worrying about a non-interactive prerender pass. (In contrast, on Blazor Server prerendering, OnAfterRender is delayed until the client connects – not an issue for Blazor Hybrid, since it's all local.)

**Avoid heavy work on UI thread:** Just as in web or server Blazor, long synchronous operations in lifecycle methods (e.g. doing a big computation in OnInitialized) will block the UI from rendering. In a MAUI hybrid app, that can lead to visible delays or even the OS feeling the app is unresponsive. Always prefer asynchronous loading patterns: do minimal setup synchronously, then use await for longer tasks so the UI thread can render interim feedback.

**First Render and OnAfterRender:** On the first render of a component, Blazor will invoke `OnAfterRenderAsync(true)` after the HTML is rendered to the WebView's DOM. Use the firstRender flag to run one-time setup code here, such as initializing JS libraries or performing DOM manipulation that requires the element to be present. Subsequent renders will call `OnAfterRenderAsync(false)` and you typically should not repeat the initialization logic in those calls (to avoid duplicates).

**Rule:** Do not call JavaScript-dependent code during component initialization or before the WebView is ready. Instead, use OnAfterRenderAsync and conditional logic (`if (firstRender)`) so that such code runs only when the DOM is interactive.

### Native and Blazor Integration Patterns

One of the powerful features of Blazor Hybrid is that your Blazor components and native MAUI code can interact and share data, but this requires explicit bridges – the Blazor UI is essentially running in an isolated WebView, so it cannot directly reference MAUI UI elements, and vice versa.

**Mixing Native and Blazor UI:** If you need to show native MAUI controls (say a Camera preview or a platform-specific picker) alongside Blazor content, the approach is to compose them in the MAUI XAML layout, not to embed one into the other. For example, you might create a MAUI ContentPage that has a Grid with a BlazorWebView in one row and a native control (or overlay) in another row or on top. Do not attempt to directly add a MAUI control in the Razor component markup – that won't work, as the Razor markup only knows how to render HTML/Blazor components inside the WebView.

**Blazor-to-Native Calls (DI Bridge Pattern):** The recommended pattern is to use dependency injection and an intermediary service to funnel calls or data between Blazor and native code. For example, you can create a singleton service (say, `IDeviceService`) that is added to the DI container in MauiProgram. In a Razor component, you inject this DeviceService and call a method (e.g. `DeviceService.TriggerCameraCapture()`), which internally fires an event. On the native side (in the MAUI page or wherever appropriate), you have access to the same singleton service. The native code can subscribe to the event and when it sees the "TriggerCameraCapture" event, it invokes the actual platform-specific API.

**Example DI Bridge:**
```csharp
// In MauiProgram.cs
builder.Services.AddSingleton<DeviceBridge>();

// A simple bridge service
public class DeviceBridge {
    public event EventHandler? OnCaptureRequested; 
    
    public void RequestCapture() {
        OnCaptureRequested?.Invoke(this, EventArgs.Empty);
    }
    
    public event EventHandler<string>? OnDataReceived;
    public void RaiseDataReceived(string data) {
        OnDataReceived?.Invoke(this, data);
    }
}

// In Razor component
@inject DeviceBridge Bridge
<button @onclick="@(()=> Bridge.RequestCapture())">Take Photo</button>

// In MAUI page code-behind
var bridge = this.Handler.MauiContext.Services.GetService<DeviceBridge>();
if(bridge != null) {
    bridge.OnCaptureRequested += async (s,e) => {
        string photoPath = await CapturePhotoAsync();
        bridge.RaiseDataReceived(photoPath);
    };
}
```

**Native-to-Blazor Calls:** Another approach provided by the framework is `BlazorWebView.TryDispatchAsync`, which allows native code to run a callback within the Blazor renderer's synchronization context. For example:

```csharp
await _blazorWebView.TryDispatchAsync(sp => {
    var navManager = sp.GetRequiredService<NavigationManager>();
    navManager.NavigateTo("some/blazor/page");
});
```

This will safely obtain the Blazor NavigationManager from the Blazor WebView's service provider and call it to navigate within the Blazor routing context.

**JavaScript Interop vs. Direct .NET Calls:** In a Blazor Hybrid app, you have the full .NET API surface available directly, so you usually don't need JS Interop to call device APIs – you'd call them in .NET via MAUI libraries or dependency injection. JS interop is still available, but consider whether a direct .NET approach is cleaner. For instance, to get geolocation in a hybrid app, you could just use Xamarin/MAUI Essentials `Geolocation.GetLastKnownLocationAsync()` in a service, rather than using JS to call navigator.geolocation.

### JavaScript Interop in Blazor Hybrid Apps

**JS Runtime Environment:** In BlazorWebView, JavaScript executes inside the embedded WebView's context (e.g., WebView2 or WKWebView JavaScript engine). Calls you make with `IJSRuntime.InvokeAsync` from .NET will be dispatched to that WebView's JavaScript runtime, just like in a web app. Performance of interop is generally fast on modern devices, but not as fast as an in-process method call, so still treat it as an asynchronous, potentially UI-blocking operation.

**Including Scripts and Libraries:** Any JS libraries your components need should be added to your wwwroot/index.html or served as static files in the wwwroot folder, similar to a Blazor WASM app. If a component uses certain CSS (e.g., from a library or an icon font), make sure the MAUI host includes those static assets; otherwise the component might appear broken on the native app.

**Limitations of the WebView Sandbox:** Certain browser APIs might be restricted or not available. For example, `window.alert` or `window.open` may behave differently on mobile. In general, assume the JS environment is constrained – if you need functionality outside of pure web capabilities, use .NET to accomplish it.

**Use Case – JS for UI Libraries:** One common use is embedding a JS UI library (like charts or maps) in a Blazor component. In a hybrid app, include the JS and CSS, and initialize the library in `OnAfterRenderAsync(firstRender)`. If something isn't showing up, double-check that:
- The script is actually loaded
- You only call the JS after the element is in the DOM (hence the OnAfterRender(firstRender) pattern)
- In MAUI, ensure any special web security settings are handled if needed

### State Management and Persistence in Blazor Hybrid

**Per-User State:** In a MAUI app, each app instance is a single user environment. Thus, PTDoc's hybrid app can use singletons or static singletons for things like a selected patient ID, without risk of leaking data between users (there's no server-side shared memory concern).

**Scoped vs. Singleton Services:** Services registered as Scoped in a BlazorWebView are tied to the lifetime of the BlazorWebView's navigation context. For practical purposes, many apps just use Singleton services for most data or state so that it persists as long as the app runs (because mobile apps can be suspended or resumed, but if not fully killed, the singleton remains in memory).

**Persisting State Across App Restarts:** In a MAUI app, there is no "browser refresh," but the user can close the app or it can be terminated by the OS. If your application has important transient state, you should explicitly persist it. Options:
- Use the WebView's local storage via `IJSRuntime.InvokeAsync("localStorage.setItem", ...)`
- Use .NET MAUI storage APIs – `Preferences.Set("key", value)` or `SecureStorage` for sensitive info
- Use a SQLite database via EF Core/SQLite

**Cascading Parameters and State Containers:** Within Blazor, use proper state propagation patterns (unidirectional data flow). If multiple Blazor components need to share state, prefer using a CascadingParameter or a dedicated state container service rather than relying on global variables.

### Navigation and Routing in a Hybrid Blazor App

Navigation in a Blazor hybrid application can happen at two levels: within the Blazor WebView (internal routing) and at the native app level (switching MAUI pages or shells).

**Blazor Internal Navigation:** If your MAUI app is primarily a single BlazorWebView that hosts a full Blazor SPA, you will typically use Blazor's built-in routing (NavLink, NavigationManager.NavigateTo, etc.) to navigate between views. The BlazorWebView.StartPath can be set (e.g., `StartPath = "reports"` in XAML) so that when the Blazor app loads, it navigates to that route instead of the default.

**Native Navigation (shell and multi-page apps):** Some .NET MAUI apps use the Shell or multiple ContentPages to structure the app. In such cases, navigating between a Blazor page and a XAML page is outside of Blazor's scope – you must use MAUI's navigation mechanisms (e.g., `Shell.Current.GoToAsync("//NativePage")`).

**Intercepting Links:** By default, if a user clicks an anchor in a BlazorWebView, the UrlLoading event of BlazorWebView is fired. You can handle this event to decide what to do – perhaps open the link in an external browser instead of within the WebView.

**Hardware Back Button (Android):** On Android devices, the hardware back button by default might close the app if not handled. If your Blazor app has its own navigation stack, you might want to intercept back presses to navigate back in Blazor rather than exiting.

### Platform-Specific Considerations and Best Practices

**Mobile UI/UX Adjustments:** A Blazor app running in a mobile app should feel like a native mobile experience, not a website. You might want to disable text selection for non-editable content and remove default tap highlights via CSS:

```css
*:not(input) { user-select: none; -webkit-user-select: none; }
* { -webkit-tap-highlight-color: transparent; }
```

**Safe Area and Screen Notches:** On iOS (and some Android devices), use CSS environment variables to pad your Blazor content within safe areas:

```css
@supports (-webkit-touch-callout: none) {
  body {
    padding-top: env(safe-area-inset-top);
    padding-bottom: env(safe-area-inset-bottom);
    padding-left: env(safe-area-inset-left);
    padding-right: env(safe-area-inset-right);
  }
}
```

**Performance on Low-End Devices:** Mobile devices (especially older Androids) might struggle if your Blazor UI has very large DOMs or does extremely heavy processing on the UI thread. Keep list virtualization in mind (e.g., use `<Virtualize>` for long lists) and avoid unnecessary re-rendering.

**WebView Quirks:** Each platform's WebView has its quirks. For example, if your app has a dark theme, you might see a white flash during startup. To avoid this, you can customize the WebView background:

```csharp
#if IOS || MACCATALYST
    Microsoft.Maui.Handlers.BlazorWebViewHandler.Mapper.AppendToMapping("BgFix", (handler, view) => {
        handler.PlatformView.Opaque = false;
    });
#endif
```

**Styling and Fonts:** Ensure that any fonts or icons your Blazor app needs are included. If the web version references Google Fonts via an internet URL, consider bundling those fonts for offline use in the app or include them in the MAUI Resources.

### Common Pitfalls and Guidance for Blazor MAUI Hybrid

**Don't Overlook Loading Indicators:** A common cause of "invisible" or blank UI in hybrid apps is a component that is waiting on data without any UI feedback. Always initialize components with some visible content (loading spinners, skeletons, "Loading…" text) immediately, then fill in data later.

**Conditional Rendering Logic:** Be careful with @if blocks or other conditions that might inadvertently prevent content from rendering. Better pattern: render a placeholder when the real content should not be shown yet, rather than not instantiating the component at all.

**Ensure Components Are Registered & Imported:** If a Blazor component isn't appearing at all in the MAUI app, it might be a registration issue. You need to have that library referenced by the MAUI project and its namespace imported (in _Imports.razor or in the usage page).

**No In-Place Parameter Mutation:** Do not have a Blazor component set its own [Parameter] properties internally after initial render. This can cause state to get out of sync, especially if the parent isn't aware. Instead, use internal state or notify the parent.

**JS Interop Timing:** Calling JS too early or without proper guards can fail. Make sure it's in OnAfterRenderAsync and not during OnInitialized. Also ensure the JS script is added to index.html.

**File System and Paths:** If your Blazor code deals with file paths, remember that in hybrid the "current directory" or base URL might differ. If you need to load a local file, consider using .NET file APIs (with FileSystem.AppDataDirectory etc.) via dependency injection.

**Device Permissions & WebView:** If your Blazor code tries to use a browser API that requires permission (like Geolocation), you should use the native permission request via MAUI Essentials (like `Permissions.RequestAsync<Permissions.LocationWhenInUse>()`) on the native side before invoking the JS or .NET functionality.

**Testing on Real Devices:** It's crucial to test Blazor components on actual devices or emulators. Some issues (like the soft keyboard overlapping input, or performance on a low-end Android) are only evident there.

**Logging and Diagnostics:** BlazorWebView can log diagnostic info if you configure logging providers in the MAUI app. You can also attach to the WebView's dev tools: on Windows, press Alt+Shift+D to open the dev tools window for WebView2, or use browser dev tools via the device (for Android, Chrome's inspect devices feature; for iOS, Safari's Web Inspector). In Debug builds, `AddBlazorWebViewDeveloperTools()` enables this.

**Summary:** Blazor Hybrid combines two worlds – as an agent, always consider both sides: the web-based UI paradigms and the native app realities. Follow the official .NET MAUI Blazor guidance to ensure smooth, responsive, and correct functionality on all platforms. This will prevent issues like components not appearing on certain devices, unresponsive UI due to sync calls, or broken integrations between the Blazor and native parts of PTDoc.