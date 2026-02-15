# StarBreaker.DataCore

Star Citizen stores most of its game data (items, vehicles, weapons, missions, etc.) in a binary format called **DataCore** (`.dcb` files). This project reads that format and, together with the type generator, provides fully typed C# access to every record.

## Quick start

```csharp
// 1. Open the game archive and load the database
var p4k = P4kFile.FromFile(@"C:\...\Game.p4k");
var dcbEntry = p4k.Entries.First(e => e.Name.EndsWith("Game2.dcb"));
var db = new DataCoreDatabase(p4k.OpenStream(dcbEntry));

// 2. Create the typed reader (generated code wires up the type dispatch)
var dataCore = new DataCoreBinary(db);
var reader = dataCore.Reader;

// 3. Load all records — each one is a typed C# object
var allRecords = db.MainRecords
    .AsParallel()
    .Select(id => reader.GetFromMainRecord(db.GetRecord(id)))
    .ToList();

// 4. Query with LINQ — everything is strongly typed
var weapons = allRecords
    .Where(r => r.Data is EntityClassDefinition)
    .Select(r => (EntityClassDefinition)r.Data)
    .Where(e => e.Components.Any(c => c?.Value is SCItemWeaponComponentParams))
    .ToList();

foreach (var entity in weapons)
{
    var weapon = entity.Components.Select(c => c?.Value).OfType<SCItemWeaponComponentParams>().First();
    var ammo = weapon.ammoContainerRecord?.Value;  // follows the reference, returns typed object
    // ...
}
```

## How it works

The system has three layers: the binary database, the typed reader infrastructure, and the generated code.

### Layer 1: DataCoreDatabase

`DataCoreDatabase` parses the raw `.dcb` binary into its component tables:

- **Struct definitions** — schema for each type (name, parent type, property list)
- **Property definitions** — field metadata (name, data type, offset)
- **Enum definitions** — names and values
- **Value tables** — flat arrays for every data type (ints, floats, strings, booleans, references, pointers, etc.)
- **Instance data** — raw bytes for every struct instance, addressable by `(structIndex, instanceIndex)`
- **Main records** — the top-level entries, each with a GUID, file path, and root struct instance

The database is read-only after construction. `GetReader(structIndex, instanceIndex)` returns a `SpanReader` positioned at the raw bytes for a given instance.

### Layer 2: DataCoreTypedReader

`DataCoreTypedReader` is the runtime infrastructure that turns raw bytes into C# objects:

- **Instance cache** — a `ConcurrentDictionary` keyed by `(structIndex, instanceIndex)`. Each struct instance is read at most once; subsequent accesses return the cached object.
- **Circular reference detection** — a per-thread `HashSet` tracks which instances are currently being read. If a cycle is detected, it returns `null` to break the loop.
- **Enum cache** — maps `(Type, stringId)` to boxed enum values, avoiding repeated string parsing.
- **Polymorphic dispatch** — a delegate (`readFromRecord`) supplied by the generated code maps a struct index to the correct `GetOrReadInstance<T>()` call.
- **Read helpers** — methods like `ReadClassArray<T>`, `ReadRefArray<T>`, `ReadEnumArray<T>`, `ReadStringArray`, etc. that generated `Read` methods call into.

### Layer 3: Generated code

The `dcb-generate` CLI command (`StarBreaker.DataCore.TypeGenerator`) reads the database schema and emits:

**One C# record per struct type** (~6,400 types), each with a static `Read` method:

```csharp
public record AmmoParams : IDataCoreTypedReadable<AmmoParams>
{
    public required float speed { get; init; }
    public required float lifetime { get; init; }
    public required DataCoreRef<ProjectileParams>? projectileParams { get; init; }
    // ...

    public static AmmoParams Read(DataCoreTypedReader dataCore, ref SpanReader reader)
    {
        // Reads fields sequentially from the binary stream
        var _speed = reader.Read<float>();
        var _lifetime = reader.Read<float>();
        var _projectileParams = dataCore.CreateRef<ProjectileParams>(reader.Read<DataCorePointer>());
        return new AmmoParams { speed = _speed, lifetime = _lifetime, projectileParams = _projectileParams };
    }
}
```

**One C# enum per enum type** (~740 enums).

**A `DataCoreBinary` dispatcher class** with a switch expression mapping struct indices to types:

```csharp
private IDataCoreTypedReadable? ReadFromRecord(int structIndex, int instanceIndex)
{
    return structIndex switch
    {
        0 => Reader.GetOrReadInstance<SomeType>(structIndex, instanceIndex),
        1 => Reader.GetOrReadInstance<AnotherType>(structIndex, instanceIndex),
        // ... 6,390 entries
    };
}
```

This is what enables polymorphism — when a `DataCoreRef<SWeaponActionParams>` is resolved, the dispatch function sees the actual struct index and returns the concrete derived type (e.g. `SWeaponActionFireRapidParams`).

### DataCoreRef\<T\> — lazy references

The database has two kinds of links between objects:

- **Pointers** (`DataCorePointer`) — a direct `(structIndex, instanceIndex)` pair. Already resolved at construction time.
- **References** (`DataCoreReference`) — a GUID that identifies a record. Resolved lazily on first access.

`DataCoreRef<T>` wraps both. Accessing `.Value` triggers resolution and caching:

```
weapon.ammoContainerRecord?.Value   // follows the reference, reads + caches the target
weapon.ammoContainerRecord?.Value   // second access returns the cached object
```

References can point to any record in the database, including main records (top-level entries). This is transparent to the consumer — `.Value` works the same regardless.

## Regenerating types

When the game updates and the schema changes, regenerate the types:

```sh
StarBreaker.Cli dcb-generate --p4k "C:\...\Game.p4k" --output "src/StarBreaker.Sandbox/Generated"
```

The generated `DataCoreBinary` constructor validates the schema hash at runtime, so a mismatch between the generated code and the database will throw immediately rather than silently returning wrong data.
