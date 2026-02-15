using System.Diagnostics;
using System.Text.Json.Serialization;
using StarBreaker.Common;

namespace StarBreaker.DataCore;

/// <summary>
/// A lazy reference wrapper that resolves and caches the referenced object on first access.
/// </summary>
/// <typeparam name="T">The type of the referenced object</typeparam>
[DebuggerDisplay("{DebuggerDisplay,nq}")]
public sealed class DataCoreRef<T>
    where T : class, IDataCoreTypedReadable
{
    private readonly DataCoreTypedReader _reader;

    // Raw reference stored for deferred resolution (null for pointer-based refs)
    private readonly CigGuid _rawRecordId;

    // Resolved fields (set directly for pointers, lazily for references)
    private int _structIndex;
    private int _instanceIndex;
    private bool _isMainRecord;
    private string? _recordPath;
    private volatile bool _isReferenceResolved;

    private T? _cachedValue;
    private bool _isResolved;

    // Constructor for pointer (already resolved)
    private DataCoreRef(DataCoreTypedReader reader, int structIndex, int instanceIndex)
    {
        _reader = reader;
        _structIndex = structIndex;
        _instanceIndex = instanceIndex;
        _rawRecordId = CigGuid.Empty;
        _isReferenceResolved = true;
    }

    // Constructor for reference (lazy)
    private DataCoreRef(DataCoreTypedReader reader, CigGuid recordId)
    {
        _reader = reader;
        _rawRecordId = recordId;
        _isReferenceResolved = false;
    }

    private void EnsureResolved()
    {
        if (_isReferenceResolved)
            return;

        var db = _reader.Database;
        if (db.TryGetRecordInfo(_rawRecordId, out var info))
        {
            _structIndex = info.StructIndex;
            _instanceIndex = info.InstanceIndex;
            _isMainRecord = info.IsMainRecord;
            _recordPath = info.IsMainRecord ? db.GetString(info.FileNameOffset) : null;
        }

        _isReferenceResolved = true;
    }

    /// <summary>
    /// Creates a reference from a DataCoreReference (used for Reference types).
    /// Zero lookups at construction time — all resolution is deferred.
    /// </summary>
    public static DataCoreRef<T>? FromReference(DataCoreTypedReader reader, DataCoreReference reference)
    {
        if (reference.RecordId == CigGuid.Empty || reference.InstanceIndex == -1)
            return null;

        return new DataCoreRef<T>(reader, reference.RecordId);
    }

    /// <summary>
    /// Creates a reference from a DataCorePointer (used for Strong/Weak pointers)
    /// </summary>
    public static DataCoreRef<T>? FromPointer(DataCoreTypedReader reader, DataCorePointer pointer)
    {
        if (pointer.StructIndex == -1 || pointer.InstanceIndex == -1)
            return null;

        return new DataCoreRef<T>(reader, pointer.StructIndex, pointer.InstanceIndex);
    }

    /// <summary>
    /// The resolved value. Accessing this property triggers lazy loading and caching.
    /// Uses polymorphic dispatch to resolve the actual derived type.
    /// </summary>
    [JsonIgnore]
    public T? Value
    {
        get
        {
            if (Volatile.Read(ref _isResolved))
                return _cachedValue;

            EnsureResolved();

            _cachedValue = _reader.GetOrReadInstancePolymorphic<T>(_structIndex, _instanceIndex);
            Volatile.Write(ref _isResolved, true);
            return _cachedValue;
        }
    }

    /// <summary>
    /// The record ID if this is a reference (not a pointer)
    /// </summary>
    public CigGuid RecordId => _rawRecordId;

    /// <summary>
    /// True if this references a main record (separate file)
    /// </summary>
    public bool IsExternalFile
    {
        get
        {
            EnsureResolved();
            return _isMainRecord;
        }
    }

    /// <summary>
    /// The file path if this is an external main record reference
    /// </summary>
    public string? ExternalFilePath
    {
        get
        {
            EnsureResolved();
            return _recordPath;
        }
    }

    /// <summary>
    /// The struct index in the database
    /// </summary>
    [JsonIgnore]
    public int StructIndex
    {
        get
        {
            EnsureResolved();
            return _structIndex;
        }
    }

    /// <summary>
    /// The instance index in the database
    /// </summary>
    [JsonIgnore]
    public int InstanceIndex
    {
        get
        {
            EnsureResolved();
            return _instanceIndex;
        }
    }

    /// <summary>
    /// Whether the value has been resolved yet
    /// </summary>
    [JsonIgnore]
    public bool IsResolved => _isResolved;

    private string DebuggerDisplay => _isReferenceResolved
        ? (_isMainRecord
            ? $"DataCoreRef<{typeof(T).Name}> -> External: {_recordPath}"
            : $"DataCoreRef<{typeof(T).Name}> -> ({_structIndex}, {_instanceIndex}) {(_isResolved ? "[Resolved]" : "[Pending]")}")
        : $"DataCoreRef<{typeof(T).Name}> -> [Unresolved ref: {_rawRecordId}]";
}
