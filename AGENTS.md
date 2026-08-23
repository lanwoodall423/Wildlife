# Content Mod Development

This repository is a RimWorld content mod.

For source changes, RimTest owns build, generation, local deployment,
RimWorld lifecycle, affected-test selection, and runtime validation.

Use:

rimtest doctor --json

before validation, and normally:

rimtest affected --run --json

after source changes.

Do not manually copy assemblies into the live mod directory, manually
substitute RimWorld launches for RimTest lifecycle operations, or call
RimContext/DevBridge2 directly when RimTest owns the workflow.

Artifact freshness must be proven before runtime results are accepted.