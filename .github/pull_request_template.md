## Summary
<!-- What does this PR do? 1-3 bullet points -->

## Type of change
- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update
- [ ] Refactoring / tech debt

## Testing
- [ ] Unit tests added / updated
- [ ] All existing tests pass (`dotnet test`)
- [ ] Build passes with zero warnings (`dotnet build /p:TreatWarningsAsErrors=true`)
- [ ] Test coverage maintained at ≥ 80%

## Privacy checklist
- [ ] No network calls added (`grep -r "HttpClient\|WebClient" src/` returns 0 results)
- [ ] No new plaintext storage (clipboard data always encrypted before DB write)
- [ ] No new dependencies without licence review in `THIRD_PARTY_LICENSES.txt`

## Screenshots (if UI change)
<!-- Before / after screenshots or GIF -->

## Related issues
Closes #
