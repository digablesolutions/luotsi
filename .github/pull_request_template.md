## Summary

- what changed
- why it changed

## Validation

- [ ] `dotnet build .\Luotsi.sln`
- [ ] `dotnet test .\Luotsi.sln`
- [ ] focused validation for the touched slice was run when applicable
- [ ] output/replay handoff checked with `luotsi help output` guidance when command output, artifacts, or agent behavior changed

## Checklist

- [ ] docs updated if command behavior, artifacts, or operator UX changed
- [ ] JSON envelope or JSONL contract impact reviewed
- [ ] artifact behavior reviewed for device-facing changes
- [ ] first follow-up command points to `data.recommended_next_action.command` or `luotsi replay open --artifacts <artifact-root> --dry-run` where applicable
- [ ] screenshots, logs, replay artifacts, or example payloads attached when they add clarity
