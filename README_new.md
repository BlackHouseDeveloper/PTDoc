### 5. Using the PTDoc-Foundry.sh Script

As mentioned, the repository includes a script (`PTDoc-Foundry.sh`) to automate environment setup tasks. Here's a quick reference on using it:

**Usage:**
- Running the script with no arguments will scaffold or update the solution structure (adding missing files, normalizing project settings). It's idempotent – it won't overwrite existing code, and you can run it after pulling updates to ensure your local projects match the expected configuration.
- `--create-migration`: Generates the initial EF Core migration (if not already present) and updates the database. This is typically done once at project setup. If an initial migration already exists, the script will skip creating a duplicate.
- `--seed`: Populates the database with sample data. Safe to run multiple times; it only inserts data if not already present (e.g., it won't add duplicate patients if you run it again).
- `--help`: Shows usage info. You can also open the script in a text editor – it's heavily commented to explain each step it performs.

### 6. Design System and Styling

PTDoc uses a token-driven design system aligned to the Figma Make Prototype v5:
- **Design Tokens:** See `src/PTDoc.UI/wwwroot/css/tokens.css` for color, spacing, typography, and other design tokens
- **Global Styles:** Base styles in `src/PTDoc.UI/wwwroot/css/app.css`
- **Component Styles:** Scoped CSS files alongside components (`.razor.css`)
- **Theme Support:** Light and dark themes with token switching

For detailed styling guidelines, see `docs/style-system.md` and `docs/context/ptdoc-figma-make-prototype-v5-context.md`.

### 7. Troubleshooting & FAQ

**Database not found / issues:** Ensure you have run the migration (`--create-migration`) and seeding steps. The SQLite database file `dev.PTDoc.db` should be present in the project root and referenced by `appsettings.Development.json`. Override it via `PFP_DB_PATH` environment variable if you need a different location.

**iOS build issues:** If building for iOS, make sure you've opened the project in Xcode at least once to accept any license agreements, and that you have an iOS simulator selected. You might also need to adjust code signing settings in Xcode for the iOS target if you deploy to a physical device.

**Hot Reload/Live Reload:** When running the MAUI app, .NET Hot Reload should work if you launch from Visual Studio. For the Blazor Web app, code changes generally require rebuilding (`dotnet run` will pick up changes on restart). Develop with the approach that suits you (for quick UI iteration, the Web app is convenient; for testing native features, use the MAUI app).

For detailed troubleshooting, see `docs/TROUBLESHOOTING.md`.

For any other issues, please check the repository's issue tracker or contact the maintainers. Happy documenting!

---

## Contributing

We welcome contributions to PTDoc! Our enterprise-grade development environment includes automated workflows to ensure healthcare compliance and code quality.

### Quick Start for Contributors

1. **Fork and Clone**: Fork the repository and clone your fork
2. **Environment Setup**: Run `./PTDoc-Foundry.sh` to set up development environment
3. **Validate Setup**: Run `gh workflow run mcp-copilot-setup-validation.yml -f validation_scope=full`
4. **Create Branch**: Use conventional naming (`feat/`, `fix/`, `docs/`, `clinical/`)
5. **Make Changes**: Follow coding standards and write tests
6. **Auto-formatting**: Our MCP workflows will auto-format code on PR submission
7. **Test Thoroughly**: Run `dotnet test` and verify all platforms build
8. **Submit PR**: Include clear description and reference any related issues

### Enterprise Development Standards

- **Code Style**: Auto-enforced via MCP workflows with StyleCop and Roslynator
- **Healthcare Compliance**: HIPAA considerations validated in all changes
- **Testing**: Comprehensive test coverage with platform-specific validation
- **Architecture**: Clean Architecture with healthcare-focused domain modeling
- **Accessibility**: WCAG 2.1 AA/AAA compliance for inclusive healthcare technology
- **Documentation**: Clinical documentation standards and API documentation

### Automated Quality Assurance

Our MCP framework automatically:
- **Validates environment setup** for all contributors
- **Formats code** according to healthcare software standards
- **Tests accessibility compliance** for clinical users
- **Validates PDF reports** for clinical documentation standards
- **Checks database changes** for HIPAA compliance
- **Generates comprehensive release notes** with clinical impact assessment

### Healthcare-Specific Contribution Guidelines

- **Clinical Workflow Impact**: Assess how changes affect clinical workflows
- **Patient Data Privacy**: Ensure all changes maintain HIPAA compliance
- **Accessibility**: Test with screen readers and keyboard navigation
- **Multi-platform Testing**: Validate changes across iOS, Android, macOS, and web
- **Clinical Terminology**: Use standardized medical terminology and coding systems

For detailed contribution guidelines and healthcare-specific development practices, see:
- `docs/DEVELOPMENT.md` - Comprehensive development guide
- `docs/ARCHITECTURE.md` - Technical architecture and patterns
- `.github/copilot-instructions.md` - Copilot and MCP usage guide

### Community & Support

- **Discussions**: Join community discussions for questions and ideas
- **Healthcare Provider Support**: Specialized support for clinical workflow questions
- **Security Issues**: Report security vulnerabilities via GitHub Security Advisories
- **Documentation**: Comprehensive guides in the `docs/` directory

---

*Happy documenting! 🏥📱*
