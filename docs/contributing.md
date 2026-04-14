# Contributing to ClipHive

Thank you for your interest in contributing! This document covers dev setup, code style, and the PR process.

## Dev Environment Setup

1. **Prerequisites**
   - Windows 10 1903+ or Windows 11
   - [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
   - [Git 2.40+](https://git-scm.com/)
   - IDE: Visual Studio 2022 or JetBrains Rider 2024+

2. **Clone and build**
   ```powershell
   git clone https://github.com/levitasOrg/ClipHive.git
   cd ClipHive
   dotnet restore
   dotnet build
   ```

3. **Run tests**
   ```powershell
   dotnet test
   # With coverage:
   dotnet test --collect:"XPlat Code Coverage" --results-directory coverage
   ```

4. **Run the app locally**
   ```powershell
   dotnet run --project src/ClipHive
   ```

5. **Build installer** (requires [Inno Setup 6](https://jrsoftware.org/isinfo.php))
   ```powershell
   dotnet publish src/ClipHive -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
   ISCC installer/ClipHive.iss
   ```

## Code Style

- **Braces:** Allman style (opening brace on new line)
- **Indentation:** 4 spaces (configured in `.editorconfig`)
- **Nullability:** always annotate — `string?` for nullable, `string` for non-nullable
- **`var`:** only use when the type is obvious from the right-hand side (`var list = new List<string>()`)
- **Immutability:** prefer `record` / `readonly` / `IReadOnlyList<T>` over mutable state
- **No `#region`:** keep files small enough that regions aren't needed (≤ 400 lines)
- **P/Invoke:** all `DllImport` declarations go in `Helpers/Win32.cs` only
- **XML doc comments:** required on all public APIs

## Test Requirements for PRs

- Every bug fix must include a regression test
- Every new public method must have at least one unit test
- Overall coverage must stay ≥ 80% (`dotnet test --collect:"XPlat Code Coverage"`)
- Tests must pass on `dotnet build -c Release /p:TreatWarningsAsErrors=true`

## PR Checklist

Before submitting a pull request:

- [ ] `dotnet build /p:TreatWarningsAsErrors=true` passes (zero warnings)
- [ ] `dotnet test` passes (all tests green)
- [ ] No new network calls added (`grep -r "HttpClient\|WebClient" src/` = 0 results)
- [ ] If touching `Contracts.cs` — discuss in issue first (breaking change for all agents)
- [ ] `CHANGELOG.md` updated under `[Unreleased]`
- [ ] PR description filled in using the template

## Branch Naming

```
feature/<issue-number>-short-description
fix/<issue-number>-short-description
docs/<topic>
chore/<topic>
```

## Commit Convention

We use [Conventional Commits](https://www.conventionalcommits.org/):

```
feat(storage): add pagination to GetAllAsync
fix(hotkey): prevent double registration on settings save
test(encryption): add tamper-detection round-trip test
docs(readme): add winget install instructions
chore(ci): pin dotnet-version to 8.0.x
```
