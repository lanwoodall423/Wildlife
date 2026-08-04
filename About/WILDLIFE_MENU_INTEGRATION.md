# Wildlife Tab Menu Integration

Mods can add ordered buttons to the Wildlife tab without patching its layout.

Call this public method after defs load:

```csharp
Herds.WildlifeMenuRegistry.Register(
    "your.package.unique-id",
    "Button Label",
    "Tooltip text",
    100,
    () => true,
    () => Find.WindowStack.Add(new YourWindow()));
```

Entries are ordered by numeric order, then label and ID. Built-in entries reserve:

- `0`: Wildlife Journal
- `10`: Horticulture - Novel Seeds (optional companion)
- `20`: Aquaculture (optional companion)
- `30`: Expeditions

The built-in `wildlife.overview` ID remains stable for compatibility and opens the Wildlife Journal Field Log.

The Journal's top-level destinations are Field Log, Knowledge, Region, and Chronicle. Existing
Field Guide, Living Atlas, Signals, Investigations, Expeditions, and Stories pages remain
available as contextual detail pages beneath those destinations.

Third-party integrations should normally use `100` or higher. Buttons wrap into responsive rows and the pawn table reserves the required vertical space automatically. Optional companions should register only while active and open their existing `MainButtonDef` through `Find.MainTabsRoot` rather than duplicating a window.

Optional integrations can call the same method through reflection to avoid making Wildlife a required assembly dependency.
