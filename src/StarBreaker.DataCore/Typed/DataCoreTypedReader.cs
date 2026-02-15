using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using StarBreaker.Common;

namespace StarBreaker.DataCore;

/// <summary>
/// Provides all infrastructure for reading typed DataCore records.
/// Generated code creates an instance and supplies the type dispatch function.
/// </summary>
public sealed class DataCoreTypedReader : IDataCoreBinary<DataCoreTypedRecord>, IDisposable
{
    public DataCoreDatabase Database { get; }

    private readonly Func<int, int, IDataCoreTypedReadable?> _readFromRecord;

    // Instance cache to prevent re-reading and handle circular references (thread-safe)
    private readonly ConcurrentDictionary<(int structIndex, int instanceIndex), IDataCoreTypedReadable> _instanceCache = new();

    // Enum parse cache: (Type, stringId) -> boxed enum value
    private readonly ConcurrentDictionary<(Type type, int stringId), object> _enumCache = new();

    // Track instances currently being read to detect circular references (per-thread)
    private readonly ThreadLocal<HashSet<(int structIndex, int instanceIndex)>> _currentlyReading = new(() => new());

    private readonly JsonSerializerOptions _jsonOptions;

    public DataCoreTypedReader(DataCoreDatabase database, Func<int, int, IDataCoreTypedReadable?> readFromRecord)
    {
        Database = database;
        _readFromRecord = readFromRecord;
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            ReferenceHandler = ReferenceHandler.IgnoreCycles,
            Converters =
            {
                new JsonStringEnumConverter(),
                new DataCoreRefJsonConverterFactory()
            }
        };
    }

    /// <summary>
    /// Validates that the database matches the expected schema.
    /// </summary>
    public void ValidateSchema(int expectedStructCount, int expectedEnumCount, int expectedStructsHash, int expectedEnumsHash)
    {
        if (expectedStructCount != Database.StructDefinitions.Length)
            throw new InvalidOperationException($"Struct count mismatch. Expected {expectedStructCount}, got {Database.StructDefinitions.Length}");

        if (expectedEnumCount != Database.EnumDefinitions.Length)
            throw new InvalidOperationException($"Enum count mismatch. Expected {expectedEnumCount}, got {Database.EnumDefinitions.Length}");

        if (expectedStructsHash != Database.StructsHash)
            throw new InvalidOperationException($"Structs hash mismatch. Expected {expectedStructsHash}, got {Database.StructsHash}");

        if (expectedEnumsHash != Database.EnumsHash)
            throw new InvalidOperationException($"Enums hash mismatch. Expected {expectedEnumsHash}, got {Database.EnumsHash}");
    }

    public DataCoreTypedRecord GetFromMainRecord(DataCoreRecord record)
    {
        var data = _readFromRecord(record.StructIndex, record.InstanceIndex);

        if (data == null)
            throw new InvalidOperationException($"Failed to read data from record {record}");

        return new DataCoreTypedRecord(record.GetFileName(Database), record.GetName(Database), record.Id, data);
    }

    public void SaveRecordToFile(DataCoreRecord record, string path)
    {
        var typedRecord = GetFromMainRecord(record);
        var filePath = Path.ChangeExtension(path, "json");

        using var fileStream = new FileStream(filePath, FileMode.Create);
        JsonSerializer.Serialize(fileStream, typedRecord, typedRecord.GetType(), _jsonOptions);
    }

    public void SaveStructToFile(int structIndex, string path)
    {
        using var fileStream = new FileStream(Path.ChangeExtension(path, "json"), FileMode.Create);
        using var writer = new Utf8JsonWriter(fileStream, new JsonWriterOptions { Indented = true });

        var structDefinition = Database.StructDefinitions[structIndex];

        writer.WriteStartObject();
        writer.WriteString("Name", structDefinition.GetName(Database));
        if (structDefinition.ParentTypeIndex != -1)
            writer.WriteString("Parent", Database.StructDefinitions[structDefinition.ParentTypeIndex].GetName(Database));

        writer.WriteStartArray("Properties");
        foreach (var prop in Database.GetProperties(structIndex))
        {
            writer.WriteStartObject();
            writer.WriteString("Name", prop.GetName(Database));
            writer.WriteString("Type", prop.GetTypeString(Database));
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    public void SaveEnumToFile(int enumIndex, string path)
    {
        using var fileStream = new FileStream(Path.ChangeExtension(path, "json"), FileMode.Create);
        using var writer = new Utf8JsonWriter(fileStream, new JsonWriterOptions { Indented = true });

        var enumDefinition = Database.EnumDefinitions[enumIndex];

        writer.WriteStartObject();
        writer.WriteString("Name", enumDefinition.GetName(Database));

        writer.WriteStartArray("Values");
        for (var i = 0; i < enumDefinition.ValueCount; i++)
        {
            var enumOption = Database.EnumOptions[enumDefinition.FirstValueIndex + i].ToString(Database);
            writer.WriteStringValue(enumOption);
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// Clears the instance cache and the current thread's circular reference tracking set.
    /// </summary>
    public void ClearCache()
    {
        _instanceCache.Clear();
        _enumCache.Clear();
        _currentlyReading.Value!.Clear();
    }

    public void Dispose()
    {
        _currentlyReading.Dispose();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Gets a cached instance or reads and caches it.
    /// </summary>
    public T? GetOrReadInstance<T>(int structIndex, int instanceIndex)
        where T : class, IDataCoreTypedReadable<T>
    {
        if (structIndex == -1 || instanceIndex == -1)
            return null;

        var key = (structIndex, instanceIndex);

        if (_instanceCache.TryGetValue(key, out var cached))
            return cached as T;

        var currentlyReading = _currentlyReading.Value!;
        if (currentlyReading.Contains(key))
        {
            Debug.WriteLine($"Circular reference detected at ({structIndex}, {instanceIndex})");
            return null;
        }

        currentlyReading.Add(key);

        try
        {
            var reader = Database.GetReader(structIndex, instanceIndex);
            var result = T.Read(this, ref reader);

            _instanceCache.TryAdd(key, result);
            return result;
        }
        finally
        {
            currentlyReading.Remove(key);
        }
    }

    /// <summary>
    /// Gets a cached instance using polymorphic dispatch via the readFromRecord delegate.
    /// </summary>
    public T? GetOrReadInstancePolymorphic<T>(int structIndex, int instanceIndex)
        where T : class, IDataCoreTypedReadable
    {
        if (structIndex == -1 || instanceIndex == -1)
            return null;

        var key = (structIndex, instanceIndex);

        if (_instanceCache.TryGetValue(key, out var cached))
            return cached as T;

        // Delegate to _readFromRecord which dispatches to GetOrReadInstance<T>,
        // where caching and circular reference tracking are handled.
        var result = _readFromRecord(structIndex, instanceIndex);
        return result as T;
    }

    #region Reference creation methods

    public DataCoreRef<T>? CreateRef<T>(DataCoreReference reference)
        where T : class, IDataCoreTypedReadable
    {
        return DataCoreRef<T>.FromReference(this, reference);
    }

    public DataCoreRef<T>? CreateRef<T>(DataCorePointer pointer)
        where T : class, IDataCoreTypedReadable
    {
        return DataCoreRef<T>.FromPointer(this, pointer);
    }

    public DataCoreRef<T>?[] ReadRefArray<T>(ref SpanReader reader)
        where T : class, IDataCoreTypedReadable
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();

        var array = new DataCoreRef<T>?[count];

        for (var i = firstIndex; i < firstIndex + count; i++)
        {
            array[i - firstIndex] = DataCoreRef<T>.FromReference(this, Database.ReferenceValues[i]);
        }

        return array;
    }

    public DataCoreRef<T>?[] ReadStrongRefArray<T>(ref SpanReader reader)
        where T : class, IDataCoreTypedReadable
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();

        var array = new DataCoreRef<T>?[count];

        for (var i = firstIndex; i < firstIndex + count; i++)
        {
            array[i - firstIndex] = DataCoreRef<T>.FromPointer(this, Database.StrongValues[i]);
        }

        return array;
    }

    public DataCoreRef<T>?[] ReadWeakRefArray<T>(ref SpanReader reader)
        where T : class, IDataCoreTypedReadable
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();

        var array = new DataCoreRef<T>?[count];

        for (var i = firstIndex; i < firstIndex + count; i++)
        {
            array[i - firstIndex] = DataCoreRef<T>.FromPointer(this, Database.WeakValues[i]);
        }

        return array;
    }

    #endregion

    #region Primitive read methods

    public T EnumParse<T>(DataCoreStringId stringId, T unknown) where T : struct, Enum
    {
        var key = (typeof(T), stringId.Id);

        if (_enumCache.TryGetValue(key, out var cached))
            return (T)cached;

        var value = stringId.ToString(Database);

        if (value == "")
            return unknown;

        if (!Enum.TryParse<T>(value, out var eVal))
        {
            Debug.WriteLine($"Error parsing Enum of type {typeof(T).Name} with value {value}. Setting to unknown.");
            _enumCache.TryAdd(key, unknown);
            return unknown;
        }

        _enumCache.TryAdd(key, eVal);
        return eVal;
    }

    public T[] ReadClassArray<T>(ref SpanReader reader, int structIndex)
        where T : class, IDataCoreTypedReadable<T>
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();

        var array = new T[count];

        for (var i = firstIndex; i < firstIndex + count; i++)
        {
            array[i - firstIndex] = GetOrReadInstance<T>(structIndex, i)
                                    ?? throw new InvalidOperationException($"ReadFromInstance failed to read instance of {typeof(T)}");
        }

        return array;
    }

    public T[] ReadEnumArray<T>(ref SpanReader reader) where T : struct, Enum
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();

        var array = new T[count];

        for (var i = firstIndex; i < firstIndex + count; i++)
        {
            array[i - firstIndex] = EnumParse<T>(Database.EnumValues[i], default);
        }

        return array;
    }

    public string[] ReadStringArray(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        var result = new string[count];

        for (var i = 0; i < count; i++)
            result[i] = Database.StringIdValues[firstIndex + i].ToString(Database);

        return result;
    }

    public string[] ReadLocaleArray(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        var result = new string[count];

        for (var i = 0; i < count; i++)
            result[i] = Database.LocaleValues[firstIndex + i].ToString(Database);

        return result;
    }

    public CigGuid[] ReadGuidArray(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.GuidValues.AsSpan(firstIndex, count).ToArray();
    }

    public sbyte[] ReadSByteArray(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.Int8Values.AsSpan(firstIndex, count).ToArray();
    }

    public short[] ReadInt16Array(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.Int16Values.AsSpan(firstIndex, count).ToArray();
    }

    public int[] ReadInt32Array(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.Int32Values.AsSpan(firstIndex, count).ToArray();
    }

    public long[] ReadInt64Array(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.Int64Values.AsSpan(firstIndex, count).ToArray();
    }

    public byte[] ReadByteArray(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.UInt8Values.AsSpan(firstIndex, count).ToArray();
    }

    public ushort[] ReadUInt16Array(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.UInt16Values.AsSpan(firstIndex, count).ToArray();
    }

    public uint[] ReadUInt32Array(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.UInt32Values.AsSpan(firstIndex, count).ToArray();
    }

    public ulong[] ReadUInt64Array(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.UInt64Values.AsSpan(firstIndex, count).ToArray();
    }

    public bool[] ReadBoolArray(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.BooleanValues.AsSpan(firstIndex, count).ToArray();
    }

    public float[] ReadSingleArray(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.SingleValues.AsSpan(firstIndex, count).ToArray();
    }

    public double[] ReadDoubleArray(ref SpanReader reader)
    {
        var count = reader.ReadInt32();
        var firstIndex = reader.ReadInt32();
        return Database.DoubleValues.AsSpan(firstIndex, count).ToArray();
    }

    #endregion
}
