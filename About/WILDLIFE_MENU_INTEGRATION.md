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

- `0`: Wildlife Overview
- `10`: Wildlife Expeditions

Third-party integrations should normally use `100` or higher. Buttons wrap into responsive rows and the pawn table reserves the required vertical space automatically.

Optional integrations can call the same method through reflection to avoid making Wildlife a required assembly dependency.
