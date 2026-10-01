# Copilot Instructions

## General Guidelines
- Use Azure Best Practices: When generating code for Azure, running terminal commands for Azure, or performing operations related to Azure, invoke your `azure_development-get_best_practices` tool if available.

## Code Style
- Use .less as the source for styles and generate .css from it; do not treat .css as the primary editable source.

## NuGet Packaging
- Every publishable library is listed in `CloudCommerce.Package/Program.cs` (the `Projects` list). When you add a new library project that must ship as a NuGet package (a new provider adapter, division library, or component library), add a `new CloudPackProject("<ProjectFolderName>")` entry there.
- Keep the list ordered so dependencies come before the projects that reference them (e.g. `CloudPayments` before `CloudPayments.Stripe`, and the division libraries before `CloudCommerce`).
- A packable project needs a `PackageId`, `Description` and `PackageReadmeFile` (with its `README.md` packed), matching the existing projects. Also add it to the package table in the root `README.md`.
- Do not add the demo, tests or other non-packable projects (`IsPackable=false`) to the list.
- `CloudCommerce.Package` writes the shared version and metadata (Authors, Company, icon) into every listed project, so do not hand-edit those per project; change the version in `CloudCommerce.Package.csproj`.
