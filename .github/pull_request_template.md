# Pull Request

## Summary

<!-- Provide a brief description of what this PR accomplishes -->

### Title
<!-- A concise title summarizing the change (e.g., "Add user authentication", "Fix database migration error") -->

### Description
<!-- Detailed description of changes. Include: -->
<!-- - **What Changed**: List of modified/added/removed files or features -->
<!-- - **Why**: Business justification or problem being solved -->
<!-- - **How**: Technical approach or implementation details -->
<!-- - **References**: Links to relevant docs, issues, or design files -->

### Type of Change
<!-- Check all that apply -->
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Performance improvement
- [ ] Code refactoring

### Impact Areas
<!-- Check all affected areas -->
- [ ] User interface (PTDoc.UI shared components)
- [ ] Core business logic (PTDoc.Core)
- [ ] Application services (PTDoc.Application)
- [ ] Infrastructure/data access (PTDoc.Infrastructure)
- [ ] API endpoints (PTDoc.Api)
- [ ] Database schema/migrations
- [ ] Build system
- [ ] CI/CD workflows

---

## Checklist

### Core Requirements
- [ ] **Architecture Compliance**: Web is DB-stateless (no DbContext/EF packages, no Infrastructure reference)
- [ ] **Platform Support**: Devices (MAUI) include EF Core + SQLite and publish successfully
- [ ] **CHANGELOG Updated**: Added entry to `[Unreleased]` section in [docs/CHANGELOG.md](docs/CHANGELOG.md)
- [ ] **Documentation**: All changes emphasized on success in relevant documentation files

### Code Quality & Testing
- [ ] StyleCop formatting passes (`dotnet format --verify-no-changes`)
- [ ] Roslynator analysis passes (`roslynator analyze PTDoc.sln --severity-level info`)
- [ ] Unit tests pass for affected components
- [ ] Cross-platform compatibility verified (Web, Android, iOS, macOS)

### Build Artifacts
<!-- Check all target frameworks that build successfully -->
- [ ] net8.0 (Web/API projects)
- [ ] net8.0-android (Android MAUI)
- [ ] net8.0-ios (iOS MAUI)
- [ ] net8.0-maccatalyst (macOS MAUI)
- [ ] Blazor Web App (hybrid Server/WASM deployment)

### Functional Testing
<!-- Check all applicable areas -->
- [ ] Patient management operations (if applicable)
- [ ] Assessment creation and editing (if applicable)
- [ ] Authentication/authorization (if applicable)
- [ ] Database operations (if applicable)
- [ ] Cross-platform UI consistency
- [ ] Performance: No significant degradation in startup time, responsiveness, or memory usage

### Platform-Specific Validation
<!-- Only check platforms you've tested -->
- [ ] **Android**: APK builds and installs successfully
- [ ] **iOS**: IPA builds (unsigned) successfully
- [ ] **Web**: Blazor Web App loads and functions correctly
- [ ] **macOS**: App builds via Catalyst successfully

### Documentation & Communication
- [ ] Changes documented in appropriate files (ARCHITECTURE.md, CHANGELOG.md, etc.)
- [ ] Breaking changes clearly identified
- [ ] User-facing changes explained
- [ ] Developer impact documented (new services, APIs, components)

---

## Testing Instructions

<!-- Provide step-by-step instructions for reviewers to test your changes -->

### 1. Setup
```bash
# Clone and checkout branch
git checkout <your-branch-name>
./PTDoc-Foundry.sh

# Build solution
dotnet build PTDoc.sln -c Release
```

### 2. Functional Testing
<!-- Replace with specific test scenarios for your changes -->
```bash
# Example: Run web application
dotnet run --project src/PTDoc.Web
# Navigate to http://localhost:5001/<your-page>

# Example: Test specific feature
# 1. Navigate to ...
# 2. Click on ...
# 3. Verify that ...
```

**Expected Behavior:**
<!-- List expected outcomes -->
- [ ] Expected behavior 1
- [ ] Expected behavior 2
- [ ] Expected behavior 3

### 3. Accessibility Testing (if UI changes)
<!-- Only include if PR contains UI changes -->
```bash
# Keyboard Navigation Test
# - Tab through all interactive elements
# - Press Enter/Space on actionable items
# - Press Escape to dismiss modals/dialogs

# Screen Reader Test (macOS)
# - Enable VoiceOver: Cmd+F5
# - Verify all components have proper ARIA labels
# - Verify interactive elements are announced correctly
```

### 4. Cross-Platform Testing (if applicable)
<!-- Only include platforms affected by your changes -->
```bash
# Android
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-android

# iOS (requires Mac)
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-ios

# macOS Catalyst
dotnet build src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-maccatalyst
```

---

## Verification Commands

<!-- Reviewers: Run these commands to verify the changes -->

```bash
# Code quality checks
dotnet format PTDoc.sln --verify-no-changes --verbosity diagnostic
roslynator analyze PTDoc.sln --severity-level info

# Build verification - All projects
dotnet build PTDoc.sln -c Release

# Build verification - Individual projects (if needed)
dotnet build src/PTDoc.Web/PTDoc.Web.csproj -c Release
dotnet build src/PTDoc.Api/PTDoc.Api.csproj -c Release

# Build verification - MAUI targets (if applicable)
dotnet publish src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-android
dotnet publish src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-ios
dotnet publish src/PTDoc.Maui/PTDoc.Maui.csproj -c Release -f net8.0-maccatalyst

# Test execution
dotnet test PTDoc.sln -c Release
dotnet test --filter "Category=Unit"
```

---

## Review Feedback

**For Reviewers**: Please comment on the following areas (check all that you verified):

### Design & Usability (if UI changes)
- [ ] **Design Fidelity**: Components match Figma specifications (if applicable)
- [ ] **Accessibility**: Keyboard navigation and screen reader support work correctly
- [ ] **Responsive Design**: Tested at multiple breakpoints (desktop/tablet/mobile)
- [ ] **Theme Support**: Light/dark mode appearance is correct

### Code Quality
- [ ] **Component Structure**: Components are properly decomposed and reusable
- [ ] **Architecture**: Adheres to Clean Architecture principles (correct layer boundaries)
- [ ] **Code Style**: Follows project conventions and coding standards
- [ ] **Error Handling**: Appropriate error handling and user feedback

### Performance & Testing
- [ ] **Performance**: Page load and interaction responsiveness is acceptable
- [ ] **Cross-Platform**: Tested on at least 2 platforms (if applicable)
- [ ] **Test Coverage**: Changes include appropriate unit/integration tests

### Documentation
- [ ] **Code Comments**: Complex logic is well-documented
- [ ] **User Documentation**: User-facing changes are documented
- [ ] **Developer Documentation**: Technical changes are explained for future maintainers

---

## Additional Context

<!-- Add any other context about the PR here -->
<!-- Examples: -->
<!-- - Related issues or PRs -->
<!-- - Future work or follow-up tasks -->
<!-- - Known limitations or trade-offs -->
<!-- - Migration notes (if breaking changes) -->
<!-- - Screenshots or recordings (for UI changes) -->

---

**Healthcare Context Reminder**: PTDoc is a HIPAA-conscious application. Ensure all patient data handling includes:
- Audit trails for data access/modifications
- Secure session management
- Appropriate authentication/authorization checks
- No sensitive data in logs or error messages

